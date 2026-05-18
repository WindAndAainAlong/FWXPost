using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PostProcessor.Core.IR;

namespace PostProcessor.Core.Parsing;

/// <summary>
/// CLS 解析器：将 NX CLS 文本转为内部 IR（ToolpathProgram）。
/// 重点支持：TOOL / SPINDL / FEDRAT / RAPID / GOTO / CIRCLE。
/// </summary>
public sealed class ClsParser
{
    public ToolpathProgram Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Input path is required.", nameof(path));
        }

        // 读取全部行并以文件名作为程序名
        var lines = File.ReadAllLines(path);
        var programName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        return ParseFromLines(programName, lines);
    }

    public ToolpathProgram ParseFromLines(string programName, IReadOnlyList<string> lines)
    {
        if (programName == null)
        {
            throw new ArgumentNullException(nameof(programName));
        }

        var blocks = new List<IRBlock>();
        var sequence = 1;

        // CLS 状态机：RAPID 只影响下一条 GOTO
        var pendingRapid = false;
        // 当前进给（FEDRAT）
        double? currentFeed = null;
        // 最近的刀轴向量（用于没有 IJK 的 GOTO 续用）
        (double I, double J, double K)? lastToolAxis = null;
        // 最近的点位（用于圆弧计算）
        (double X, double Y, double Z)? lastPoint = null;

        // CIRCLE/ + 下一条 GOTO 组成圆弧
        CircleState? circle = null;

        // 孔循环：CYCLE/* ... 到 CYCLE/OFF
        var holeCycleActive = false;
        // 工艺阶段去重：避免连续重复输出同一阶段
        ProcessPhaseType? lastPhase = null;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // 识别工艺阶段关键字（jindao/qiexue/tuidao 等），插入阶段块供模板渲染
            if (TryParseProcessPhase(line, out var phaseType))
            {
                if (lastPhase != phaseType)
                {
                    blocks.Add(new ProcessPhaseBlock
                    {
                        Sequence = sequence++,
                        PhaseType = phaseType,
                        RawText = line
                    });
                    lastPhase = phaseType;
                }
            }

            // TOOL PATH/... ：开始一个新的刀轨段（Operation），同时记录刀具名
            if (line.StartsWith("TOOL PATH/", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseToolPathHeader(line, out var pathName, out var toolName))
                {
                    blocks.Add(new PathStartBlock
                    {
                        Sequence = sequence++,
                        PathName = pathName,
                        ToolName = toolName
                    });

                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        // 这里直接生成 TOOL_CHANGE，由后处理阶段决定是否需要去重（同刀不重复 M6）
                        blocks.Add(new ToolChangeBlock
                        {
                            Sequence = sequence++,
                            ToolName = toolName,
                            ToolNumber = null
                        });
                    }

                    // 刀轨段切换：重置部分状态，避免跨段圆弧/循环串联
                    pendingRapid = false;
                    currentFeed = null;
                    lastToolAxis = null;
                    lastPoint = null;
                    circle = null;
                    holeCycleActive = false;
                }
                continue;
            }

            // END-OF-PATH：结束当前刀轨段
            if (line.StartsWith("END-OF-PATH", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new PathEndBlock { Sequence = sequence++ });
                pendingRapid = false;
                currentFeed = null;
                lastToolAxis = null;
                lastPoint = null;
                circle = null;
                holeCycleActive = false;
                continue;
            }

            // END 结束解析
            if (line.StartsWith("END", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // TOOL/1
            if (line.StartsWith("TOOL/", StringComparison.OrdinalIgnoreCase))
            {
                var toolText = line[5..].Trim();
                if (TryParseFirstNumber(toolText, out var toolNumber))
                {
                    blocks.Add(new ToolChangeBlock
                    {
                        Sequence = sequence++,
                        ToolNumber = (int)Math.Round(toolNumber),
                        ToolName = string.Empty
                    });
                }
                continue;
            }

            // FEDRAT/...
            if (line.StartsWith("FEDRAT/", StringComparison.OrdinalIgnoreCase))
            {
                var feedText = line[7..];
                if (TryParseFirstNumber(feedText, out var feedValue))
                {
                    currentFeed = feedValue;
                }
                continue;
            }

            // SPEED/ 或 SPINDL/
            if (line.StartsWith("SPEED/", StringComparison.OrdinalIgnoreCase) || line.StartsWith("SPINDL/", StringComparison.OrdinalIgnoreCase))
            {
                var speedText = line.Contains('/') ? line[(line.IndexOf('/') + 1)..] : line;
                if (TryParseFirstNumber(speedText, out var rpm))
                {
                    var dir = ParseSpindleDirection(speedText);
                    blocks.Add(new SpindleBlock
                    {
                        Sequence = sequence++,
                        Rpm = (int)Math.Round(rpm),
                        Direction = dir
                    });
                }
                continue;
            }

            // RAPID 只影响下一条 GOTO
            if (line.StartsWith("RAPID", StringComparison.OrdinalIgnoreCase))
            {
                pendingRapid = true;
                continue;
            }

            // CIRCLE/ 保存圆心与法向，等待下一条 GOTO 做圆弧
            if (line.StartsWith("CIRCLE/", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseCircle(line, out var parsedCircle))
                {
                    circle = parsedCircle;
                }
                continue;
            }

            // CYCLE/*（除 CYCLE/OFF 外）定义孔循环：孔位由后续 GOTO/ 提供。
            if (line.StartsWith("CYCLE/", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("CYCLE/OFF", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseCycleStart(line, out var family, out var variant, out var parameters))
                {
                    holeCycleActive = true;

                    blocks.Add(new HoleCycleStartBlock
                    {
                        Sequence = sequence++,
                        CycleFamily = family,
                        CycleVariant = variant,
                        Parameters = parameters
                    });
                }
                continue;
            }

            // CYCLE/OFF -> 结束当前循环
            if (line.StartsWith("CYCLE/OFF", StringComparison.OrdinalIgnoreCase))
            {
                if (holeCycleActive)
                {
                    blocks.Add(new HoleCycleEndBlock { Sequence = sequence++ });
                }
                holeCycleActive = false;
                continue;
            }

            // GOTO/ 解析坐标（含 IJK 刀轴）
            if (line.StartsWith("GOTO/", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseGoto(line, out var pos, out var axis))
                {
                    continue;
                }

                if (axis != null)
                {
                    lastToolAxis = axis;
                }

                // 孔循环期间：GOTO 作为孔位
                if (holeCycleActive)
                {
                    var hole = new HoleCycleHoleBlock
                    {
                        Sequence = sequence++,
                        X = pos.X,
                        Y = pos.Y,
                        Z = pos.Z,
                        ToolAxisI = lastToolAxis?.I,
                        ToolAxisJ = lastToolAxis?.J,
                        ToolAxisK = lastToolAxis?.K
                    };
                    blocks.Add(hole);

                    lastPoint = pos;
                    pendingRapid = false;
                    continue;
                }

                if (circle != null && lastPoint != null)
                {
                    // CIRCLE + GOTO => 圆弧
                    var arc = BuildArcBlock(sequence++, circle.Value, lastPoint.Value, pos, currentFeed, lastToolAxis);
                    blocks.Add(arc);
                    circle = null;
                }
                else
                {
                    var kind = pendingRapid ? MotionKind.Rapid : MotionKind.Linear;
                    var motion = new MotionBlock
                    {
                        Sequence = sequence++,
                        Kind = kind,
                        X = pos.X,
                        Y = pos.Y,
                        Z = pos.Z,
                        FeedRate = kind == MotionKind.Rapid ? null : currentFeed,
                        ToolAxisI = lastToolAxis?.I,
                        ToolAxisJ = lastToolAxis?.J,
                        ToolAxisK = lastToolAxis?.K
                    };
                    blocks.Add(motion);
                }

                lastPoint = pos;
                pendingRapid = false;
                continue;
            }
        }

        return new ToolpathProgram(programName, blocks);
    }

    private static bool TryParseProcessPhase(string line, out ProcessPhaseType phaseType)
    {
        phaseType = ProcessPhaseType.Unknown;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var text = line.ToLowerInvariant();

        // 英文拼音/常见中文关键词
        if (text.Contains("jindao", StringComparison.Ordinal) || text.Contains("进刀", StringComparison.Ordinal))
        {
            phaseType = ProcessPhaseType.JinDao;
            return true;
        }
        if (text.Contains("qiexue", StringComparison.Ordinal) || text.Contains("切削", StringComparison.Ordinal))
        {
            phaseType = ProcessPhaseType.QieXue;
            return true;
        }
        if (text.Contains("tuidao", StringComparison.Ordinal) || text.Contains("退刀", StringComparison.Ordinal))
        {
            phaseType = ProcessPhaseType.TuiDao;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 解析 TOOL PATH 头：
    /// 例：TOOL PATH/J_SHANG_1,TOOL,R3
    /// 返回 PathName=J_SHANG_1, ToolName=R3。
    /// </summary>
    private static bool TryParseToolPathHeader(string line, out string pathName, out string toolName)
    {
        pathName = string.Empty;
        toolName = string.Empty;

        var payload = line["TOOL PATH/".Length..].Trim();
        if (payload.Length == 0)
        {
            return false;
        }

        var parts = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1)
        {
            pathName = parts[0].Trim();
        }

        // 查找 ",TOOL,<name>"
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Trim().Equals("TOOL", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < parts.Length)
                {
                    toolName = parts[i + 1].Trim();
                }
                break;
            }
        }

        return pathName.Length > 0;
    }

    /// <summary>
    /// 解析 CYCLE/* 行：提取循环主类/子类，以及键值对参数。
    /// 支持形式示例：
    /// - CYCLE/DRILL,RAPTO,3.0,FEDTO,-10.0,MMPM,250
    /// - CYCLE/DRILL,DEEP,RAPTO,3.0,FEDTO,-10.0,MMPM,250
    /// - CYCLE/BORE,BACK,RAPTO,3.0,FEDTO,-10.0
    /// </summary>
    private static bool TryParseCycleStart(string line, out string family, out string variant, out Dictionary<string, string> parameters)
    {
        family = string.Empty;
        variant = string.Empty;
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var payload = line;
        var slashIndex = payload.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            payload = payload[(slashIndex + 1)..];
        }

        var rawTokens = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (rawTokens.Length == 0)
        {
            return false;
        }

        var tokens = new string[rawTokens.Length];
        for (var i = 0; i < rawTokens.Length; i++)
        {
            tokens[i] = rawTokens[i].Trim();
        }

        family = tokens[0].ToUpperInvariant();
        var idx = 1;

        // 子类型：如果第一个 token 不是“参数键”，则认为是 variant
        if (idx < tokens.Length && !IsLikelyCycleParamKey(tokens, idx))
        {
            variant = tokens[idx].ToUpperInvariant();
            idx++;
        }

        // 解析 key,value pairs
        while (idx < tokens.Length)
        {
            var key = tokens[idx].ToUpperInvariant();
            var value = string.Empty;
            if (idx + 1 < tokens.Length)
            {
                value = tokens[idx + 1];
            }

            // 如果 value 不存在，则仍然记录 key（便于排查/扩展）
            parameters[key] = value;
            idx += 2;
        }

        return family.Length > 0;
    }

    private static bool IsLikelyCycleParamKey(string[] tokens, int idx)
    {
        var token = tokens[idx].Trim();
        if (token.Length == 0)
        {
            return false;
        }

        // 常见参数键（后续可扩展）
        if (token.Equals("RAPTO", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("FEDTO", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("MMPM", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 如果下一个 token 是数值，则倾向认为这是参数键
        if (idx + 1 < tokens.Length &&
            double.TryParse(tokens[idx + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从字符串中提取第一个数值（用于 TOOL/FEDRAT/SPINDL）。
    /// </summary>
    private static bool TryParseFirstNumber(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(',');
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 解析主轴方向（CLW/CCLW）。
    /// </summary>
    private static SpindleDirection ParseSpindleDirection(string text)
    {
        if (text.IndexOf("CCLW", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SpindleDirection.Cclw;
        }

        if (text.IndexOf("CLW", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return SpindleDirection.Clw;
        }

        return SpindleDirection.Unknown;
    }

    /// <summary>
    /// 解析 GOTO：支持 X/Y/Z/I/J/K 前缀，也支持纯数值序列。
    /// </summary>
    private static bool TryParseGoto(string line, out (double X, double Y, double Z) pos, out (double I, double J, double K)? axis)
    {
        pos = default;
        axis = null;

        var payload = line[5..];
        var tokens = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);

        double? x = null;
        double? y = null;
        double? z = null;
        double? i = null;
        double? j = null;
        double? k = null;

        var hasAxisTokens = false;
        var numericTokens = new List<double>(tokens.Length);

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (TryParseAxisToken(token, out var axisName, out var value))
            {
                hasAxisTokens = true;
                switch (axisName)
                {
                    case 'X':
                        x = value;
                        break;
                    case 'Y':
                        y = value;
                        break;
                    case 'Z':
                        z = value;
                        break;
                    case 'I':
                        i = value;
                        break;
                    case 'J':
                        j = value;
                        break;
                    case 'K':
                        k = value;
                        break;
                }
            }
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                numericTokens.Add(number);
            }
        }

        // 纯数值序列：X,Y,Z,(I,J,K)
        if (!hasAxisTokens)
        {
            if (numericTokens.Count >= 3)
            {
                x = numericTokens[0];
                y = numericTokens[1];
                z = numericTokens[2];
            }
            if (numericTokens.Count >= 6)
            {
                i = numericTokens[3];
                j = numericTokens[4];
                k = numericTokens[5];
            }
        }

        if (!x.HasValue || !y.HasValue || !z.HasValue)
        {
            return false;
        }

        pos = (x.Value, y.Value, z.Value);
        if (i.HasValue && j.HasValue && k.HasValue)
        {
            axis = (i.Value, j.Value, k.Value);
        }

        return true;
    }

    /// <summary>
    /// 解析单个轴字符串（如 X10.0）。
    /// </summary>
    private static bool TryParseAxisToken(string token, out char axis, out double value)
    {
        axis = '\0';
        value = 0;

        if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
        {
            return false;
        }

        var ch = char.ToUpperInvariant(token[0]);
        if (ch is not ('X' or 'Y' or 'Z' or 'I' or 'J' or 'K'))
        {
            return false;
        }

        var numberText = token[1..].Trim();
        if (numberText.StartsWith("=", StringComparison.Ordinal))
        {
            numberText = numberText[1..].Trim();
        }

        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        axis = ch;
        return true;
    }

    /// <summary>
    /// 解析 CIRCLE：取前 6 个数值作为圆心与法向。
    /// </summary>
    private static bool TryParseCircle(string line, out CircleState circle)
    {
        circle = default;

        var payload = line[7..];
        var tokens = payload.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<double>();
        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (TryParseAxisToken(token, out _, out var val))
            {
                numbers.Add(val);
                continue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                numbers.Add(num);
            }
        }

        if (numbers.Count < 6)
        {
            return false;
        }

        circle = new CircleState(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
        return true;
    }

    /// <summary>
    /// 构建圆弧块：根据法向与起终点判断 CW/CCW，并输出 I/J（相对起点）。
    /// </summary>
    private static MotionBlock BuildArcBlock(
        int sequence,
        CircleState circle,
        (double X, double Y, double Z) start,
        (double X, double Y, double Z) end,
        double? feed,
        (double I, double J, double K)? axis)
    {
        var v1 = (X: start.X - circle.Cx, Y: start.Y - circle.Cy, Z: start.Z - circle.Cz);
        var v2 = (X: end.X - circle.Cx, Y: end.Y - circle.Cy, Z: end.Z - circle.Cz);
        var cross = (
            X: v1.Y * v2.Z - v1.Z * v2.Y,
            Y: v1.Z * v2.X - v1.X * v2.Z,
            Z: v1.X * v2.Y - v1.Y * v2.X
        );
        var dot = cross.X * circle.Nx + cross.Y * circle.Ny + cross.Z * circle.Nz;
        var ccw = dot >= 0.0;

        return new MotionBlock
        {
            Sequence = sequence,
            Kind = MotionKind.Arc,
            ArcClockwise = !ccw,
            X = end.X,
            Y = end.Y,
            Z = end.Z,
            ArcI = circle.Cx - start.X,
            ArcJ = circle.Cy - start.Y,
            FeedRate = feed,
            ToolAxisI = axis?.I,
            ToolAxisJ = axis?.J,
            ToolAxisK = axis?.K
        };
    }

    private readonly struct CircleState
    {
        public CircleState(double cx, double cy, double cz, double nx, double ny, double nz)
        {
            Cx = cx;
            Cy = cy;
            Cz = cz;
            Nx = nx;
            Ny = ny;
            Nz = nz;
        }

        public double Cx { get; }
        public double Cy { get; }
        public double Cz { get; }
        public double Nx { get; }
        public double Ny { get; }
        public double Nz { get; }
    }
}
