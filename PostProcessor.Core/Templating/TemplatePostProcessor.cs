using System.Collections.Generic;
using PostProcessor.Core.IR;
using PostProcessor.Core.Kinematics;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 模板驱动的 NC 生成器。
/// 负责：轴模式判断、事件触发、上下文构建、模板渲染输出。
/// </summary>
public sealed class TemplatePostProcessor
{
    private readonly PostOptions _options;

    public TemplatePostProcessor(PostOptions? options = null)
    {
        _options = options ?? new PostOptions();
    }

    public List<string> Generate(ToolpathProgram program, TemplateDefinition template)
    {
        var axisMode = ResolveAxisMode(program);
        return Generate(program, template, axisMode);
    }

    /// <summary>
    /// 按指定轴模式生成（便于外部复用同一判断结果）。
    /// </summary>
    public List<string> Generate(ToolpathProgram program, TemplateDefinition template, AxisMode axisMode)
    {
        var output = new List<string>();
        var state = new OutputState();
        var seq = 1;
        var hasExplicitPaths = false;
        foreach (var b in program.Blocks)
        {
            if (b is PathStartBlock)
            {
                hasExplicitPaths = true;
                break;
            }
        }
        var perPathAxisMode = hasExplicitPaths ? BuildPerPathAxisMode(program) : null;
        var activeAxisMode = axisMode;

        // 1) 头部
        var headerContext = TemplateContextFactory.BuildHeaderContext(program);
        TemplateContextFactory.AddAxisModeContext(headerContext, axisMode);
        TemplateRenderer.AppendSection(output, template.GetSection("HEADER"), headerContext);

        // 2) 事件：START_PROGRAM / START_PATH
        var startProgramContext = TemplateContextFactory.BuildHeaderContext(program);
        TemplateContextFactory.AddAxisModeContext(startProgramContext, axisMode);
        TemplateContextFactory.AddEventContext(startProgramContext, "START_PROGRAM", seq++);
        AppendEventSection(output, template, "START_PROGRAM", startProgramContext);

        // 若 CLS 内没有显式的 TOOL PATH/.. END-OF-PATH 分段，则维持旧逻辑：整个文件作为一个 PATH。
        if (!hasExplicitPaths)
        {
            state.CurrentPathName = program.ProgramName;
            var startPathContext = TemplateContextFactory.BuildHeaderContext(program);
            TemplateContextFactory.AddAxisModeContext(startPathContext, activeAxisMode);
            TemplateContextFactory.AddEventContext(startPathContext, "START_PATH", seq++);
            AppendEventSection(output, template, "START_PATH", startPathContext);
        }

        // 3) 主循环：逐块生成 NC
        foreach (var block in program.Blocks)
        {
            switch (block)
            {
                case PathStartBlock pathStart:
                {
                    state.CurrentPathName = pathStart.PathName ?? string.Empty;
                    ResetPerPathOutputState(state);
                    if (perPathAxisMode != null && perPathAxisMode.TryGetValue(pathStart.Sequence, out var mode))
                    {
                        activeAxisMode = mode;
                    }
                    else
                    {
                        activeAxisMode = axisMode;
                    }

                    var context = TemplateContextFactory.BuildPathStartContext(program, pathStart);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "START_PATH", seq++);
                    AppendEventSection(output, template, "START_PATH", context);
                    break;
                }
                case PathEndBlock:
                {
                    var context = TemplateContextFactory.BuildHeaderContext(program);
                    context["PathName"] = state.CurrentPathName;
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "END_PATH", seq++);
                    AppendEventSection(output, template, "END_PATH", context);
                    break;
                }
                case HoleCycleStartBlock cycleStart:
                {
                    // 进入孔循环：缓存参数，等待后续 HoleCycleHoleBlock 输出“第一孔”初始化（定位 + MCALL）
                    state.CycleActive = true;
                    state.CycleInitialized = false;
                    state.CycleFamily = cycleStart.CycleFamily;
                    state.CycleVariant = cycleStart.CycleVariant;
                    state.CycleParameters = cycleStart.Parameters;

                    // 常用参数缓存（RAPTO/FEDTO/MMPM）
                    state.CycleRapTo = TryGetParamDouble(state.CycleParameters, "RAPTO") ?? 0.0;
                    state.CycleFedTo = TryGetParamDouble(state.CycleParameters, "FEDTO") ?? 0.0;
                    state.CycleFeedRate = TryGetParamDouble(state.CycleParameters, "MMPM");

                    var context = TemplateContextFactory.BuildCycleStartContext(program, cycleStart, state);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "CYCLE_START", seq++);
                    AppendCycleSection(output, template, BuildCycleCandidates(state, suffix: "START"), context);
                    break;
                }
                case HoleCycleHoleBlock hole:
                {
                    var isFirstHole = state.CycleActive && !state.CycleInitialized;
                    var context = TemplateContextFactory.BuildCycleHoleContext(program, hole, state, activeAxisMode, isFirstHole);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);

                    // 3+2 模式：第一次锁轴时输出 A/C + CYCLE800（钻孔同样适用）
                    if (activeAxisMode == AxisMode.ThreePlusTwo && state.AxisJustLocked)
                    {
                        var rotaryContext = new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase);
                        TemplateContextFactory.AddEventContext(rotaryContext, "ROTARY_SETUP", seq++, "ROTARY_SETUP");
                        AppendEventSection(output, template, "ROTARY_SETUP", rotaryContext);
                    }

                    var suffix = isFirstHole ? "FIRST_HOLE" : "HOLE";
                    TemplateContextFactory.AddEventContext(context, "CYCLE_" + suffix, seq++, "CYCLE_" + suffix);
                    AppendCycleSection(output, template, BuildCycleCandidates(state, suffix), context);

                    if (isFirstHole)
                    {
                        state.CycleInitialized = true;
                    }
                    break;
                }
                case HoleCycleEndBlock cycleEnd:
                {
                    var context = TemplateContextFactory.BuildCycleEndContext(program, cycleEnd, state);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "CYCLE_END", seq++);
                    AppendCycleSection(output, template, BuildCycleCandidates(state, suffix: "END"), context);

                    state.CycleActive = false;
                    state.CycleInitialized = false;
                    state.CycleFamily = string.Empty;
                    state.CycleVariant = string.Empty;
                    state.CycleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    state.CycleRapTo = 0.0;
                    state.CycleFedTo = 0.0;
                    state.CycleFeedRate = null;
                    break;
                }
                case ToolChangeBlock tool:
                {
                    var toolKey =
                        !string.IsNullOrWhiteSpace(tool.ToolName) ? tool.ToolName.Trim() :
                        tool.ToolNumber.HasValue ? tool.ToolNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                        string.Empty;

                    // 空刀具 / 同刀重复：跳过换刀输出
                    if (toolKey.Length == 0 || string.Equals(toolKey, state.LastToolKey, System.StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    var context = TemplateContextFactory.BuildToolChangeContext(program, tool);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "TOOL_CHANGE", seq++);
                    AppendEventSection(output, template, "TOOL_CHANGE", context);
                    state.LastToolKey = toolKey;
                    break;
                }
                case SpindleBlock spindle:
                {
                    var context = TemplateContextFactory.BuildSpindleContext(program, spindle);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "SPINDLE", seq++);
                    AppendEventSection(output, template, "SPINDLE", context);
                    break;
                }
                case FeedBlock feed:
                {
                    var context = TemplateContextFactory.BuildFeedContext(program, feed, state);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    TemplateContextFactory.AddEventContext(context, "FEED", seq++);
                    AppendEventSection(output, template, "FEED", context);
                    break;
                }
                case MotionBlock motion:
                {
                    var context = TemplateContextFactory.BuildMotionContext(program, motion, state, activeAxisMode);
                    TemplateContextFactory.AddAxisModeContext(context, activeAxisMode);
                    var baseSection = motion.Kind switch
                    {
                        MotionKind.Rapid => "RAPID",
                        MotionKind.Linear => "LINEAR",
                        MotionKind.Arc => motion.ArcClockwise ? "ARC_CW" : "ARC_CCW",
                        _ => "LINEAR"
                    };

                    // 3+2 模式：第一次锁轴时输出 A/C + CYCLE800
                    if (activeAxisMode == AxisMode.ThreePlusTwo && state.AxisJustLocked)
                    {
                        var rotaryContext = new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase);
                        TemplateContextFactory.AddEventContext(rotaryContext, "ROTARY_SETUP", seq++, "ROTARY_SETUP");
                        AppendEventSection(output, template, "ROTARY_SETUP", rotaryContext);
                    }

                    TemplateContextFactory.AddEventContext(context, baseSection, seq++, baseSection);
                    AppendEventSection(output, template, baseSection, context);
                    break;
                }
            }
        }

        // 4) 事件：END_PATH / END_PROGRAM
        if (!hasExplicitPaths)
        {
            var endPathContext = TemplateContextFactory.BuildHeaderContext(program);
            TemplateContextFactory.AddAxisModeContext(endPathContext, activeAxisMode);
            TemplateContextFactory.AddEventContext(endPathContext, "END_PATH", seq++);
            AppendEventSection(output, template, "END_PATH", endPathContext);
        }

        var endProgramContext = TemplateContextFactory.BuildHeaderContext(program);
        TemplateContextFactory.AddAxisModeContext(endProgramContext, axisMode);
        TemplateContextFactory.AddEventContext(endProgramContext, "END_PROGRAM", seq++);
        AppendEventSection(output, template, "END_PROGRAM", endProgramContext);

        // 5) 尾部
        TemplateRenderer.AppendSection(output, template.GetSection("FOOTER"), headerContext);

        // 6) 行号（N1, N2, ...）
        LineNumbering.Apply(output, start: 10, step: 10);

        return output;
    }

    /// <summary>
    /// 每个 PATH 开始时重置输出缓存：
    /// - 避免跨 PATH 的 X/Y/Z/F 去重导致第一行缺轴
    /// - 3+2 锁轴状态按 PATH 重新开始（不同工序可能有不同锁轴角度）
    /// </summary>
    private static void ResetPerPathOutputState(OutputState state)
    {
        state.LastX = null;
        state.LastY = null;
        state.LastZ = null;
        state.LastFeed = null;

        state.LastA = null;
        state.LastC = null;
        state.AxisLocked = false;
        state.AxisJustLocked = false;

        state.CycleActive = false;
        state.CycleInitialized = false;
        state.CycleFamily = string.Empty;
        state.CycleVariant = string.Empty;
        state.CycleParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        state.CycleRapTo = 0.0;
        state.CycleFedTo = 0.0;
        state.CycleFeedRate = null;
    }

    private static double? TryGetParamDouble(IReadOnlyDictionary<string, string> parameters, string key)
    {
        if (parameters == null)
        {
            return null;
        }

        if (!parameters.TryGetValue(key, out var text))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    private static string[] BuildCycleCandidates(OutputState state, string suffix)
    {
        var family = (state.CycleFamily ?? string.Empty).Trim().ToUpperInvariant();
        var variant = (state.CycleVariant ?? string.Empty).Trim().ToUpperInvariant();

        // 优先级：最具体 -> family -> 通用
        if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(variant))
        {
            return new[]
            {
                $"CYCLE_{family}_{variant}_{suffix}",
                $"CYCLE_{family}_{suffix}",
                $"CYCLE_{suffix}"
            };
        }

        if (!string.IsNullOrWhiteSpace(family))
        {
            return new[]
            {
                $"CYCLE_{family}_{suffix}",
                $"CYCLE_{suffix}"
            };
        }

        return new[] { $"CYCLE_{suffix}" };
    }

    private static void AppendCycleSection(List<string> output, TemplateDefinition template, string[] candidates, Dictionary<string, string> context)
    {
        foreach (var baseSection in candidates)
        {
            var eventSection = "EVENT_" + baseSection;
            if (template.HasSection(eventSection))
            {
                TemplateRenderer.AppendSection(output, template.GetSection(eventSection), context);
                return;
            }
            if (template.HasSection(baseSection))
            {
                TemplateRenderer.AppendSection(output, template.GetSection(baseSection), context);
                return;
            }
        }

        // 找不到任何区块：不输出
    }

    /// <summary>
    /// 返回当前配置下的轴模式判断结果。
    /// </summary>
    public AxisMode GetAxisMode(ToolpathProgram program)
    {
        return ResolveAxisMode(program);
    }

    /// <summary>
    /// 事件模板优先级：EVENT_XXX 存在则优先，否则回退到 XXX。
    /// </summary>
    private static void AppendEventSection(List<string> output, TemplateDefinition template, string baseSection, Dictionary<string, string> context)
    {
        var eventSection = "EVENT_" + baseSection;
        var section = template.HasSection(eventSection) ? template.GetSection(eventSection) : template.GetSection(baseSection);
        TemplateRenderer.AppendSection(output, section, context);
    }

    /// <summary>
    /// 轴模式判断：
    /// - 刀轴向量变化 => 五轴
    /// - 刀轴向量固定且 A/C 不为 0 => 3+2
    /// - 否则三轴
    /// </summary>
    private AxisMode ResolveAxisMode(ToolpathProgram program)
    {
        var axisMode = AnalyzeAxisMode(program);
        if (axisMode == AxisMode.ThreePlusTwo && !_options.EnableThreePlusTwoRotation)
        {
            axisMode = AxisMode.FiveAxis;
        }

        return axisMode;
    }

    /// <summary>
    /// 对每个 TOOL PATH 段单独做轴模式判断，避免“多个 3+2 工序角度不同”导致全局被判成五轴。
    /// 返回：PathStartBlock.Sequence -> AxisMode。
    /// </summary>
    private Dictionary<int, AxisMode> BuildPerPathAxisMode(ToolpathProgram program)
    {
        var map = new Dictionary<int, AxisMode>();

        var blocks = program.Blocks;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not PathStartBlock start)
            {
                continue;
            }

            // 收集该 PATH 段的内容（不包含 PathStart/PathEnd 本身）
            var segment = new List<IRBlock>();
            for (var j = i + 1; j < blocks.Count; j++)
            {
                if (blocks[j] is PathEndBlock)
                {
                    break;
                }
                segment.Add(blocks[j]);
            }

            var mode = AnalyzeAxisMode(segment);
            if (mode == AxisMode.ThreePlusTwo && !_options.EnableThreePlusTwoRotation)
            {
                mode = AxisMode.FiveAxis;
            }
            map[start.Sequence] = mode;
        }

        return map;
    }

    private static AxisMode AnalyzeAxisMode(ToolpathProgram program)
    {
        (double I, double J, double K)? first = null;
        foreach (var block in program.Blocks)
        {
            // 轴模式判断需要依赖“刀轴向量”。
            // 运动块与钻孔孔位块都可能携带 IJK。
            double? i = null;
            double? j = null;
            double? k = null;
            switch (block)
            {
                case MotionBlock motion:
                    i = motion.ToolAxisI;
                    j = motion.ToolAxisJ;
                    k = motion.ToolAxisK;
                    break;
                case HoleCycleHoleBlock hole:
                    i = hole.ToolAxisI;
                    j = hole.ToolAxisJ;
                    k = hole.ToolAxisK;
                    break;
            }

            if (!i.HasValue || !j.HasValue || !k.HasValue)
            {
                continue;
            }

            var current = (i.Value, j.Value, k.Value);
            if (first == null)
            {
                first = current;
                continue;
            }

            if (!NearlyEqual(first.Value.I, current.Item1) ||
                !NearlyEqual(first.Value.J, current.Item2) ||
                !NearlyEqual(first.Value.K, current.Item3))
            {
                return AxisMode.FiveAxis;
            }
        }

        if (!first.HasValue)
        {
            return AxisMode.ThreeAxis;
        }

        if (AcHeadKinematics.TrySolveAc(first.Value.I, first.Value.J, first.Value.K, out var aDeg, out var cDeg))
        {
            if (IsNearZero(aDeg) && IsNearZero(cDeg))
            {
                return AxisMode.ThreeAxis;
            }
        }

        return AxisMode.ThreePlusTwo;
    }

    private static AxisMode AnalyzeAxisMode(IReadOnlyList<IRBlock> blocks)
    {
        (double I, double J, double K)? first = null;
        foreach (var block in blocks)
        {
            double? i = null;
            double? j = null;
            double? k = null;
            switch (block)
            {
                case MotionBlock motion:
                    i = motion.ToolAxisI;
                    j = motion.ToolAxisJ;
                    k = motion.ToolAxisK;
                    break;
                case HoleCycleHoleBlock hole:
                    i = hole.ToolAxisI;
                    j = hole.ToolAxisJ;
                    k = hole.ToolAxisK;
                    break;
            }

            if (!i.HasValue || !j.HasValue || !k.HasValue)
            {
                continue;
            }

            var current = (i.Value, j.Value, k.Value);
            if (first == null)
            {
                first = current;
                continue;
            }

            if (!NearlyEqual(first.Value.I, current.Item1) ||
                !NearlyEqual(first.Value.J, current.Item2) ||
                !NearlyEqual(first.Value.K, current.Item3))
            {
                return AxisMode.FiveAxis;
            }
        }

        if (!first.HasValue)
        {
            return AxisMode.ThreeAxis;
        }

        if (AcHeadKinematics.TrySolveAc(first.Value.I, first.Value.J, first.Value.K, out var aDeg, out var cDeg))
        {
            if (IsNearZero(aDeg) && IsNearZero(cDeg))
            {
                return AxisMode.ThreeAxis;
            }
        }

        return AxisMode.ThreePlusTwo;
    }

    private static bool NearlyEqual(double a, double b)
    {
        return System.Math.Abs(a - b) <= 1e-6;
    }

    private static bool IsNearZero(double value)
    {
        return System.Math.Abs(value) <= 1e-6;
    }
}
