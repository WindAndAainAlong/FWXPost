namespace PostProcessor.Core.IR;

public abstract record IRBlock
{
    public int Sequence { get; init; }
}

public enum MotionKind
{
    Rapid,
    Linear,
    Arc
}

public enum SpindleDirection
{
    Unknown,
    Clw,
    Cclw
}

public sealed record MotionBlock : IRBlock
{
    public MotionKind Kind { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double? ArcI { get; init; }
    public double? ArcJ { get; init; }
    public bool ArcClockwise { get; init; }
    public double? FeedRate { get; init; }
    public double? ToolAxisI { get; init; }
    public double? ToolAxisJ { get; init; }
    public double? ToolAxisK { get; init; }
}

public sealed record ToolChangeBlock : IRBlock
{
    /// <summary>
    /// 刀号（来自 TOOL/1 这类 CLS）。可能为空。
    /// </summary>
    public int? ToolNumber { get; init; }

    /// <summary>
    /// 刀具名（来自 TOOL PATH/xxx,TOOL,DR10 / R3）。可能为空。
    /// </summary>
    public string ToolName { get; init; } = string.Empty;
}

public sealed record SpindleBlock : IRBlock
{
    public int? Rpm { get; init; }
    public SpindleDirection Direction { get; init; }
}

public sealed record FeedBlock : IRBlock
{
    public double FeedRate { get; init; }
}

/// <summary>
/// 刀轨段开始：对应 NX CLS 的 TOOL PATH/...。
/// 用于在最终 NC 中分段输出 START_PATH 事件。
/// </summary>
public sealed record PathStartBlock : IRBlock
{
    public string PathName { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;
}

/// <summary>
/// 刀轨段结束：对应 NX CLS 的 END-OF-PATH。
/// 用于在最终 NC 中分段输出 END_PATH 事件。
/// </summary>
public sealed record PathEndBlock : IRBlock
{
}

public enum ProcessPhaseType
{
    Unknown,
    JinDao,
    QieXue,
    TuiDao,
    Zhuanyi
}

/// <summary>
/// 工艺阶段标记块：用于在 NC 中输出“进刀/切削/退刀”等语义提示。
/// </summary>
public sealed record ProcessPhaseBlock : IRBlock
{
    public ProcessPhaseType PhaseType { get; init; } = ProcessPhaseType.Unknown;
    public string RawText { get; init; } = string.Empty;
}

/// <summary>
/// 孔加工循环开始（对应 NX: CYCLE/DRILL ... / CYCLE/BORE ... / CYCLE/TAP ...）。
/// 说明：这是“循环定义”，真正的孔位由后续的 HoleCycleHoleBlock 提供。
/// </summary>
public sealed record HoleCycleStartBlock : IRBlock
{
    /// <summary>
    /// 循环主类：DRILL / BORE / TAP ...
    /// </summary>
    public string CycleFamily { get; init; } = string.Empty;

    /// <summary>
    /// 循环子类型：DEEP / BRKCHP / BACK ...（没有则为空字符串）。
    /// </summary>
    public string CycleVariant { get; init; } = string.Empty;

    /// <summary>
    /// 参数键值对（例如 RAPTO=3.0, FEDTO=-9.88, MMPM=250.0）。
    /// 这里不强行限制参数种类，便于后续扩展不同孔循环。
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 孔加工孔位（对应 CYCLE/* 生效期间的 GOTO/ 坐标）。
/// </summary>
public sealed record HoleCycleHoleBlock : IRBlock
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }

    public double? ToolAxisI { get; init; }
    public double? ToolAxisJ { get; init; }
    public double? ToolAxisK { get; init; }
}

/// <summary>
/// 孔加工循环结束（对应 NX: CYCLE/OFF）。
/// </summary>
public sealed record HoleCycleEndBlock : IRBlock
{
}
