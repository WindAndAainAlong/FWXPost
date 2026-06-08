using System;
using System.Collections.Generic;
using System.Globalization;
using PostProcessor.Core.IR;
using PostProcessor.Core.Kinematics;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 上下文构建器：将 Motion/Tool/Spindle 等对象转为模板变量字典。
/// 这里包含 3+2 旋转逻辑与 A/C 解算。
/// </summary>
internal static class TemplateContextFactory
{
    public static Dictionary<string, string> BuildHeaderContext(ToolpathProgram program)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 程序名与路径名（通常相同）
            ["ProgramName"] = program.ProgramName,
            ["PathName"] = program.ProgramName,
            ["ToolName"] = string.Empty
        };

        return dict;
    }

    public static Dictionary<string, string> BuildPathStartContext(ToolpathProgram program, PathStartBlock start)
    {
        var dict = BuildHeaderContext(program);
        dict["PathName"] = start.PathName ?? string.Empty;
        dict["ToolName"] = start.ToolName ?? string.Empty;
        return dict;
    }

    public static Dictionary<string, string> BuildPathEndContext(ToolpathProgram program)
    {
        // END-OF-PATH 行本身没有额外信息，这里保留当前 ProgramName/PathName 即可。
        // 若未来需要输出“离开/回零”等，可在模板里根据事件类型处理。
        return BuildHeaderContext(program);
    }

    public static Dictionary<string, string> BuildToolChangeContext(ToolpathProgram program, ToolChangeBlock tool)
    {
        var dict = BuildHeaderContext(program);
        // 换刀号/刀名（两者可能只有一个有效）
        dict["ToolNumber"] = tool.ToolNumber.HasValue ? tool.ToolNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        dict["ToolName"] = tool.ToolName ?? string.Empty;

        // 模板直接用 ToolCall 输出，避免在模板里写复杂 IF
        if (!string.IsNullOrWhiteSpace(dict["ToolName"]))
        {
            dict["ToolCall"] = "T=\"" + dict["ToolName"] + "\"";
        }
        else if (!string.IsNullOrWhiteSpace(dict["ToolNumber"]))
        {
            dict["ToolCall"] = "T" + dict["ToolNumber"];
        }
        else
        {
            dict["ToolCall"] = string.Empty;
        }

        return dict;
    }

    public static Dictionary<string, string> BuildSpindleContext(ToolpathProgram program, SpindleBlock spindle)
    {
        var dict = BuildHeaderContext(program);
        // 主轴转速
        if (spindle.Rpm.HasValue)
        {
            dict["SpindleRpm"] = spindle.Rpm.Value.ToString(CultureInfo.InvariantCulture);
        }

        // 主轴方向 -> M3/M4
        dict["SpindleMCode"] = spindle.Direction switch
        {
            SpindleDirection.Clw => "M3",
            SpindleDirection.Cclw => "M4",
            _ => string.Empty
        };

        return dict;
    }

    public static Dictionary<string, string> BuildFeedContext(ToolpathProgram program, FeedBlock feed, OutputState state)
    {
        var dict = BuildHeaderContext(program);
        // 当前进给
        dict["FeedRate"] = Format(feed.FeedRate);
        // 去重输出的 F 字段
        dict["FField"] = OutputLineProcessor.UpdateFeedField(feed.FeedRate, ref state.LastFeed);
        return dict;
    }

    public static Dictionary<string, string> BuildProcessPhaseContext(ToolpathProgram program, ProcessPhaseBlock phase)
    {
        var dict = BuildHeaderContext(program);
        dict["PhaseType"] = phase.PhaseType.ToString();
        dict["PhaseRawText"] = phase.RawText ?? string.Empty;
        dict["PhaseText"] = phase.PhaseType switch
        {
            ProcessPhaseType.JinDao => "进刀",
            ProcessPhaseType.QieXue => "切削",
            ProcessPhaseType.TuiDao => "退刀",
            _ => "工艺阶段"
        };
        return dict;
    }

    /// <summary>
    /// 孔循环开始上下文：仅提供参数，不主动修改 LastFeed（避免影响第一孔输出 F）。
    /// </summary>
    public static Dictionary<string, string> BuildCycleStartContext(ToolpathProgram program, HoleCycleStartBlock start, OutputState state)
    {
        var dict = BuildHeaderContext(program);

        dict["CycleFamily"] = start.CycleFamily;
        dict["CycleVariant"] = start.CycleVariant;

        // 参数展开：生成 Cycle_RAPTO / Cycle_FEDTO / Cycle_MMPM ...
        AddCycleParameters(dict, start.Parameters);

        dict["CycleActive"] = "1";
        dict["CycleInitialized"] = state.CycleInitialized ? "1" : "0";
        return dict;
    }

    /// <summary>
    /// 孔循环孔位上下文：
    /// - 负责 A/C 解算（五轴或 3+2 锁轴）
    /// - 负责 3+2 模式下坐标旋转
    /// - 第一孔时输出进给（FField）并将 LastZ 更新为 RAPTO（因为模板会先定位到安全高度）
    /// </summary>
    public static Dictionary<string, string> BuildCycleHoleContext(ToolpathProgram program, HoleCycleHoleBlock hole, OutputState state, AxisMode axisMode, bool isFirstHole)
    {
        var dict = BuildHeaderContext(program);

        var xOut = hole.X;
        var yOut = hole.Y;
        var zOut = hole.Z;

        // 循环分类
        dict["CycleFamily"] = state.CycleFamily;
        dict["CycleVariant"] = state.CycleVariant;

        // 循环参数（来自 state）
        AddCycleParameters(dict, state.CycleParameters);
        dict["CycleActive"] = state.CycleActive ? "1" : "0";
        dict["CycleInitialized"] = state.CycleInitialized ? "1" : "0";
        dict["IsFirstHole"] = isFirstHole ? "1" : "0";

        // 第一孔：通常需要输出一次 F（示例：F250）
        dict["FeedRate"] = state.CycleFeedRate.HasValue ? Format(state.CycleFeedRate.Value) : string.Empty;
        dict["FField"] = isFirstHole ? OutputLineProcessor.UpdateFeedField(state.CycleFeedRate, ref state.LastFeed) : string.Empty;

        // 刀轴向量（用于 3+2/五轴解算）
        if (hole.ToolAxisI.HasValue)
        {
            dict["I"] = Format(hole.ToolAxisI.Value);
        }
        if (hole.ToolAxisJ.HasValue)
        {
            dict["J"] = Format(hole.ToolAxisJ.Value);
        }
        if (hole.ToolAxisK.HasValue)
        {
            dict["K"] = Format(hole.ToolAxisK.Value);
        }

        // A/C 输出字段初始化
        dict["A"] = string.Empty;
        dict["C"] = string.Empty;
        dict["AField"] = string.Empty;
        dict["CField"] = string.Empty;

        state.AxisJustLocked = false;

        // 1) A/C 解算
        if (hole.ToolAxisI.HasValue && hole.ToolAxisJ.HasValue && hole.ToolAxisK.HasValue)
        {
            if (AcHeadKinematics.TrySolveAc(hole.ToolAxisI.Value, hole.ToolAxisJ.Value, hole.ToolAxisK.Value, out var aDeg, out var cDeg))
            {
                if (axisMode == AxisMode.FiveAxis)
                {
                    ResolveFiveAxisAc(ref aDeg, ref cDeg, state, isJinDaoMotion: true);

                    var aStr = Format(aDeg);
                    var cStr = Format(cDeg);
                    dict["A"] = aStr;
                    dict["C"] = cStr;
                    dict["AField"] = "A" + aStr;
                    dict["CField"] = "C" + cStr;
                }
                else if (axisMode == AxisMode.ThreePlusTwo)
                {
                    // 3+2：首次锁轴时输出一次 A/C
                    if (!state.AxisLocked)
                    {
                        if (!state.LastA.HasValue || !state.LastC.HasValue)
                        {
                            SelectInitialAcBranch(ref aDeg, ref cDeg);
                        }
                        dict["A"] = Format(aDeg);
                        dict["C"] = Format(cDeg);
                        dict["AField"] = "A" + Format(aDeg);
                        dict["CField"] = "C" + Format(cDeg);
                        state.LastA = aDeg;
                        state.LastC = cDeg;
                        state.AxisLocked = true;
                        state.AxisJustLocked = true;
                    }
                }
            }
        }

        // 2) 3+2 模式下进行 AC 逆旋转（孔位同样需要旋转到机床坐标）
        if (axisMode == AxisMode.ThreePlusTwo && state.AxisLocked)
        {
            var aAngle = state.LastA ?? 0.0;
            var cAngle = state.LastC ?? 0.0;
            RotateByAcInverse(ref xOut, ref yOut, ref zOut, aAngle, cAngle);
        }

        // 3) 输出最终 XY（孔位通常只用到 X/Y）
        dict["X"] = Format(xOut);
        dict["Y"] = Format(yOut);
        dict["HoleZ"] = Format(zOut); // 预留：某些循环可能需要孔底/起始Z

        // 去重字段（用于后续孔位只输出变化的轴）
        dict["XField"] = OutputLineProcessor.UpdateAxisField("X", xOut, ref state.LastX);
        dict["YField"] = OutputLineProcessor.UpdateAxisField("Y", yOut, ref state.LastY);

        // 第一孔初始化：模板通常会先定位到 Z=RAPTO
        if (isFirstHole)
        {
            state.LastZ = state.CycleRapTo;
        }
        dict["CycleZField"] = "Z" + Format(state.CycleRapTo);

        // 第一孔的“执行孔”行需要强制输出 X/Y（因为上一行已定位到同一 X/Y）
        dict["XFieldForce"] = "X" + Format(xOut);
        dict["YFieldForce"] = "Y" + Format(yOut);

        // 3+2 状态标记（供模板 IF 判断）
        dict["AxisLocked"] = state.AxisLocked ? "1" : "0";
        dict["AxisJustLocked"] = state.AxisJustLocked ? "1" : "0";

        return dict;
    }

    public static Dictionary<string, string> BuildCycleEndContext(ToolpathProgram program, HoleCycleEndBlock end, OutputState state)
    {
        var dict = BuildHeaderContext(program);
        dict["CycleFamily"] = state.CycleFamily;
        dict["CycleVariant"] = state.CycleVariant;
        AddCycleParameters(dict, state.CycleParameters);
        dict["CycleActive"] = state.CycleActive ? "1" : "0";
        dict["CycleInitialized"] = state.CycleInitialized ? "1" : "0";
        return dict;
    }

    /// <summary>
    /// 将循环参数字典展开到模板上下文：
    /// - 数值会格式化为 4 位小数
    /// - 生成变量名：Cycle_XXX（例如 Cycle_RAPTO, Cycle_FEDTO, Cycle_MMPM）
    /// </summary>
    private static void AddCycleParameters(Dictionary<string, string> dict, IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters == null)
        {
            return;
        }

        foreach (var kv in parameters)
        {
            var key = (kv.Key ?? string.Empty).Trim().ToUpperInvariant();
            if (key.Length == 0)
            {
                continue;
            }

            var valueText = (kv.Value ?? string.Empty).Trim();
            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                dict["Cycle_" + key] = Format(num);
            }
            else
            {
                dict["Cycle_" + key] = valueText;
            }
        }

        // 兼容常用字段（便于模板写起来更顺手）
        if (dict.TryGetValue("Cycle_RAPTO", out var rapto))
        {
            dict["CycleRapto"] = rapto;
        }
        if (dict.TryGetValue("Cycle_FEDTO", out var fedto))
        {
            dict["CycleFedto"] = fedto;
        }
        if (dict.TryGetValue("Cycle_MMPM", out var mmpm))
        {
            dict["CycleFeedRate"] = mmpm;
        }
    }

    /// <summary>
    /// 生成运动上下文：
    /// 1) A/C 解算（五轴或 3+2 锁轴）
    /// 2) 3+2 旋转后的 XYZ/IJ
    /// 3) 去重字段（XField/YField/...）
    /// </summary>
    public static Dictionary<string, string> BuildMotionContext(ToolpathProgram program, MotionBlock motion, OutputState state, AxisMode axisMode, bool enableFAdaptive = false)
    {
        var dict = BuildHeaderContext(program);

        var xOut = motion.X;
        var yOut = motion.Y;
        var zOut = motion.Z;
        var arcI = motion.ArcI;
        var arcJ = motion.ArcJ;

        // F 自适应变速：在 AC 解算前捕获上行点位
        var prevX = state.PrevPointX;
        var prevY = state.PrevPointY;
        var prevZ = state.PrevPointZ;
        var prevA = state.PrevPointA;
        var prevC = state.PrevPointC;

        // 进给值
        if (motion.FeedRate.HasValue)
        {
            dict["FeedRate"] = Format(motion.FeedRate.Value);
        }
        // 去重输出的 F 字段
        dict["FField"] = OutputLineProcessor.UpdateFeedField(motion.FeedRate, ref state.LastFeed);

        // 刀轴向量（若模板需要输出 I/J/K）
        if (motion.ToolAxisI.HasValue)
        {
            dict["I"] = Format(motion.ToolAxisI.Value);
        }
        if (motion.ToolAxisJ.HasValue)
        {
            dict["J"] = Format(motion.ToolAxisJ.Value);
        }
        if (motion.ToolAxisK.HasValue)
        {
            dict["K"] = Format(motion.ToolAxisK.Value);
        }

        // A/C 输出字段初始化
        dict["A"] = string.Empty;
        dict["C"] = string.Empty;
        dict["AField"] = string.Empty;
        dict["CField"] = string.Empty;

        state.AxisJustLocked = false;

        // 1) 先解算 A/C
        if (motion.ToolAxisI.HasValue && motion.ToolAxisJ.HasValue && motion.ToolAxisK.HasValue)
        {
            if (AcHeadKinematics.TrySolveAc(motion.ToolAxisI.Value, motion.ToolAxisJ.Value, motion.ToolAxisK.Value, out var aDeg, out var cDeg))
            {
                if (axisMode == AxisMode.FiveAxis)
                {
                    ResolveFiveAxisAc(ref aDeg, ref cDeg, state,
                        isJinDaoMotion: motion.PhaseType == ProcessPhaseType.JinDao);

                    var aStr = Format(aDeg);
                    var cStr = Format(cDeg);
                    dict["A"] = aStr;
                    dict["C"] = cStr;
                    dict["AField"] = "A" + aStr;
                    dict["CField"] = "C" + cStr;
                }
                else if (axisMode == AxisMode.ThreePlusTwo)
                {
                    // 3+2：只在首次锁轴时输出 A/C
                    if (!state.AxisLocked)
                    {
                        if (!state.LastA.HasValue || !state.LastC.HasValue)
                        {
                            SelectInitialAcBranch(ref aDeg, ref cDeg);
                        }
                        dict["A"] = Format(aDeg);
                        dict["C"] = Format(cDeg);
                        dict["AField"] = "A" + Format(aDeg);
                        dict["CField"] = "C" + Format(cDeg);
                        state.LastA = aDeg;
                        state.LastC = cDeg;
                        state.AxisLocked = true;
                        state.AxisJustLocked = true;
                    }
                }
            }
        }

        // 2) 3+2 模式下进行 AC 逆旋转
        if (axisMode == AxisMode.ThreePlusTwo && state.AxisLocked)
        {
            var aAngle = state.LastA ?? 0.0;
            var cAngle = state.LastC ?? 0.0;
            RotateByAcInverse(ref xOut, ref yOut, ref zOut, aAngle, cAngle);
            if (arcI.HasValue || arcJ.HasValue)
            {
                // 圆弧 I/J 作为位移向量进行同样旋转
                var ii = arcI ?? 0.0;
                var jj = arcJ ?? 0.0;
                var kk = 0.0;
                RotateByAcInverse(ref ii, ref jj, ref kk, aAngle, cAngle);
                arcI = ii;
                arcJ = jj;
            }
        }

        // 3) 输出最终 XYZ
        dict["X"] = Format(xOut);
        dict["Y"] = Format(yOut);
        dict["Z"] = Format(zOut);

        // 3) 去重字段
        dict["XField"] = OutputLineProcessor.UpdateAxisField("X", xOut, ref state.LastX);
        dict["YField"] = OutputLineProcessor.UpdateAxisField("Y", yOut, ref state.LastY);
        dict["ZField"] = OutputLineProcessor.UpdateAxisField("Z", zOut, ref state.LastZ);

        // 圆弧参数
        dict["ArcI"] = arcI.HasValue ? Format(arcI.Value) : string.Empty;
        dict["ArcJ"] = arcJ.HasValue ? Format(arcJ.Value) : string.Empty;

        // 3+2 状态标记
        dict["AxisLocked"] = state.AxisLocked ? "1" : "0";
        dict["AxisJustLocked"] = state.AxisJustLocked ? "1" : "0";

        // F 自适应变速：仅切削阶段直线
        if (enableFAdaptive && motion.Kind == MotionKind.Linear
            && motion.PhaseType == ProcessPhaseType.QieXue
            && prevX.HasValue && prevA.HasValue)
        {
            const double epsilon = 0.001;
            var dx = Math.Abs(xOut - prevX!.Value);
            var dy = Math.Abs(yOut - prevY!.Value);
            var dz = Math.Abs(zOut - prevZ!.Value);
            var da = Math.Abs(state.LastA!.Value - prevA!.Value);
            var dc = Math.Abs(Math.IEEERemainder(state.LastC!.Value - prevC!.Value, 360.0));

            var xyzSum = dx + dy + dz;
            var acSum = da + dc;
            var r = xyzSum / Math.Max(acSum, epsilon);

            if (state.PrevSegmentR.HasValue)
            {
                var rPrev = state.PrevSegmentR.Value;
                const double triggerRatio = 3.0;
                const double fScale = 0.15;

                if (!state.FReduced && rPrev / r > triggerRatio && motion.FeedRate.HasValue)
                {
                    // 进弯：r 骤降 → 降速
                    state.OriginalSegmentF = motion.FeedRate.Value;
                    var adjustedF = motion.FeedRate.Value * fScale;
                    dict["FeedRate"] = Format(adjustedF);
                    dict["FField"] = "F" + Format(adjustedF);
                    state.FReduced = true;
                    state.PrevSegmentR = null; // 跳过下一段比较，避免弯内连续触发
                }
                else if (state.FReduced && r / rPrev > triggerRatio && state.OriginalSegmentF.HasValue)
                {
                    // 出弯：r 骤升 → 恢复原 F
                    var restoredF = state.OriginalSegmentF.Value;
                    dict["FeedRate"] = Format(restoredF);
                    dict["FField"] = "F" + Format(restoredF);
                    state.OriginalSegmentF = null;
                    state.FReduced = false;
                    state.PrevSegmentR = null;
                }
                else
                {
                    state.PrevSegmentR = r;
                }
            }
            else
            {
                state.PrevSegmentR = r;
            }
        }

        // 保存本行点位供下一段差计算
        state.PrevPointX = xOut;
        state.PrevPointY = yOut;
        state.PrevPointZ = zOut;
        state.PrevPointA = state.LastA;
        state.PrevPointC = state.LastC;

        return dict;
    }

    /// <summary>
    /// 事件上下文：提供 EventType/EventSeq/EventMotionType。
    /// </summary>
    public static void AddEventContext(Dictionary<string, string> context, string eventType, int seq, string? motionType = null)
    {
        context["EventType"] = eventType;
        context["EventSeq"] = seq.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(motionType))
        {
            context["EventMotionType"] = motionType;
        }
    }

    /// <summary>
    /// 轴模式上下文：AxisMode 与 IsThreeAxis/IsThreePlusTwo/IsFiveAxis。
    /// </summary>
    public static void AddAxisModeContext(Dictionary<string, string> context, AxisMode axisMode)
    {
        context["AxisMode"] = axisMode.ToString();
        context["IsThreeAxis"] = axisMode == AxisMode.ThreeAxis ? "1" : "0";
        context["IsThreePlusTwo"] = axisMode == AxisMode.ThreePlusTwo ? "1" : "0";
        context["IsFiveAxis"] = axisMode == AxisMode.FiveAxis ? "1" : "0";
    }

    /// <summary>
    /// AC 逆旋转：先绕 C（Z）后绕 A（X）。
    /// 用于将刀轨从刀轴系转回机床坐标系。
    /// </summary>
    private static void RotateByAcInverse(ref double x, ref double y, ref double z, double aDeg, double cDeg)
    {
        var cRad = -cDeg * Math.PI / 180.0;
        var cosC = Math.Cos(cRad);
        var sinC = Math.Sin(cRad);
        var x1 = x * cosC - y * sinC;
        var y1 = x * sinC + y * cosC;
        var z1 = z;

        var aRad = -aDeg * Math.PI / 180.0;
        var cosA = Math.Cos(aRad);
        var sinA = Math.Sin(aRad);
        var y2 = y1 * cosA - z1 * sinA;
        var z2 = y1 * sinA + z1 * cosA;

        x = x1;
        y = y2;
        z = z2;
    }

    /// <summary>
    /// A/C 连续化：
    /// - 等效解 1：A,C
    /// - 等效解 2：-A, C+180
    /// 选择与上一行 C 最接近的解，避免大角度跳变。
    /// </summary>
    private static void MakeContinuousAc(ref double aDeg, ref double cDeg, double lastC)
    {
        var c1 = NormalizeCWithinRange(MakeContinuousC(cDeg, lastC), lastC);
        var c2 = NormalizeCWithinRange(MakeContinuousC(cDeg + 180.0, lastC), lastC);
        var delta1 = Math.Abs(c1 - lastC);
        var delta2 = Math.Abs(c2 - lastC);

        if (delta2 < delta1)
        {
            aDeg = -aDeg;
            cDeg = c2;
        }
        else
        {
            cDeg = c1;
        }

        // 归一化至 C 轴有效行程 [-360, 360]（delta 比较已用原始最近值完成，此处仅做边界归位）
        if (cDeg > 360.0) cDeg -= 360.0;
        else if (cDeg < -360.0) cDeg += 360.0;
    }

    private static double MakeContinuousC(double cDeg, double lastC)
    {
        var adjusted = cDeg;
        while (adjusted - lastC > 180.0)
        {
            adjusted -= 360.0;
        }
        while (adjusted - lastC < -180.0)
        {
            adjusted += 360.0;
        }
        return adjusted;
    }

    private static double NormalizeCWithinRange(double cDeg, double lastC)
    {
        var c1 = cDeg - 360.0;
        var c2 = cDeg;
        var c3 = cDeg + 360.0;

        var best = c2;
        var bestDelta = double.MaxValue;

        ChooseCandidate(ref best, ref bestDelta, c1, lastC);
        ChooseCandidate(ref best, ref bestDelta, c2, lastC);
        ChooseCandidate(ref best, ref bestDelta, c3, lastC);

        return best;
    }

    private static void ChooseCandidate(ref double best, ref double bestDelta, double candidate, double lastC)
    {
        if (candidate < -540.0 || candidate > 540.0)
            return;

        var delta = Math.Abs(candidate - lastC);
        if (delta < bestDelta)
        {
            bestDelta = delta;
            best = candidate;
        }
    }

    /// <summary>
    /// 完整周期归一：用于层间重置功能，C 可超出 [-360, 360] 边界找到真正最近的等价角。
    /// </summary>
    private static double NormalizeCPeriodic(double angle, double target)
    {
        var diff = angle - target;
        diff -= Math.Floor(diff / 360.0 + 0.5) * 360.0;
        return target + diff;
    }

    /// <summary>
    /// 五轴 AC 解算统一入口：（BuildMotionContext / BuildCycleHoleContext 共用）
    /// 处理 NeedsSaveLayerRef / NeedsQieXueInit / MakeContinuousAc 三分支，更新 aDeg/cDeg 和 state。
    /// </summary>
    private static void ResolveFiveAxisAc(ref double aDeg, ref double cDeg, OutputState state, bool isJinDaoMotion)
    {
        if (state.NeedsSaveLayerRef && isJinDaoMotion)
        {
            const double sameGroupTolerance = 1.0;
            var sameGroup = state.CurrentLayerRefA.HasValue;
            if (sameGroup)
            {
                var rawCNearRef = NormalizeCPeriodic(cDeg, state.CurrentLayerRefC!.Value);
                sameGroup = Math.Abs(aDeg - state.CurrentLayerRefA!.Value) < sameGroupTolerance
                         && Math.Abs(rawCNearRef - state.CurrentLayerRefC!.Value) < sameGroupTolerance;
            }

            if (sameGroup)
                SelectClosestToRef(ref aDeg, ref cDeg, state.CurrentLayerRefA!.Value, state.CurrentLayerRefC!.Value);
            else
                SelectInitialAcBranch(ref aDeg, ref cDeg);

            state.CurrentLayerRefA = aDeg;
            state.CurrentLayerRefC = cDeg;
            state.NeedsSaveLayerRef = false;
        }
        else if (state.NeedsQieXueInit)
        {
            SelectClosestToRef(ref aDeg, ref cDeg, state.CurrentLayerRefA!.Value, state.CurrentLayerRefC!.Value);
            state.NeedsQieXueInit = false;
        }
        else
        {
            if (state.LastC.HasValue)
                MakeContinuousAc(ref aDeg, ref cDeg, state.LastC.Value);
            else
                SelectInitialAcBranch(ref aDeg, ref cDeg);
        }

        state.LastA = aDeg;
        state.LastC = cDeg;
    }

    /// <summary>
    /// 层间参考选解：在 (A,C) 与 (-A,C+180) 中选择离参考 AC 最近且 A/C 正负号均一致的解。
    /// A 匹配优先于 C 匹配：当无解同时匹配时，优先保 A 正负号一致。
    /// C 考虑完整周期（C + n*360），选择与 refC 绝对值最接近的周期等价解。
    /// </summary>
    private static void SelectClosestToRef(ref double aDeg, ref double cDeg, double refA, double refC)
    {
        var a1 = aDeg;
        var a2 = -aDeg;

        // C 考虑所有周期等价解（±360*n），取离 refC 最近的
        var c1 = NormalizeCPeriodic(cDeg, refC);
        var c2 = NormalizeCPeriodic(cDeg + 180.0, refC);

        // A 同号：权重更高，优先保证
        var refASign = Math.Sign(refA);
        var a1Match = refASign == 0 || Math.Sign(a1) == refASign;
        var a2Match = refASign == 0 || Math.Sign(a2) == refASign;

        // C 同号（基于归一化后的 C 值）
        var refCSign = Math.Sign(refC);
        var c1Match = refCSign == 0 || Math.Sign(c1) == refCSign;
        var c2Match = refCSign == 0 || Math.Sign(c2) == refCSign;

        const double aPenalty = 2000.0;
        const double cPenalty = 1000.0;

        var penalty1 = (a1Match ? 0.0 : aPenalty) + (c1Match ? 0.0 : cPenalty);
        var penalty2 = (a2Match ? 0.0 : aPenalty) + (c2Match ? 0.0 : cPenalty);

        // deltaC 使用归一化后的值（已考虑所有周期等价解）
        var cost1 = penalty1 + Math.Abs(c1 - refC);
        var cost2 = penalty2 + Math.Abs(c2 - refC);

        if (cost2 < cost1)
        {
            aDeg = a2;
            cDeg = c2;
        }
        else
        {
            cDeg = c1;
        }
    }

    /// <summary>
    /// 首点镜像选解：在 (A,C) 与 (-A,C+180) 中选择到参考姿态 A0/C0 的最短路径。
    /// </summary>
    private static void SelectInitialAcBranch(ref double aDeg, ref double cDeg)
    {
        const double refA = 0.0;
        const double refC = 0.0;
        const double weightA = 2.0;
        const double weightC = 1.0;

        var c1 = NormalizeCWithinRange(cDeg, refC);
        var a1 = aDeg;
        var cost1 = weightA * Math.Abs(a1 - refA) + weightC * Math.Abs(c1 - refC);

        var c2 = NormalizeCWithinRange(cDeg + 180.0, refC);
        var a2 = -aDeg;
        var cost2 = weightA * Math.Abs(a2 - refA) + weightC * Math.Abs(c2 - refC);

        if (cost2 < cost1)
        {
            aDeg = a2;
            cDeg = c2;
        }
        else
        {
            cDeg = c1;
        }
    }

    private static string Format(double value)
    {
        if (Math.Abs(value) < 0.0000005)
        {
            value = 0.0;
        }

        return value.ToString("0.0000", CultureInfo.InvariantCulture);
    }
}
