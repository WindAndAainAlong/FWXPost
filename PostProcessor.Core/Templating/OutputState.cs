namespace PostProcessor.Core.Templating;

/// <summary>
/// 输出状态缓存：用于去重输出与 3+2 锁轴状态。
/// </summary>
internal sealed class OutputState
{
    // 上一行坐标（用于去重）
    public double? LastX;
    public double? LastY;
    public double? LastZ;

    // 上一行 A/C（用于五轴或 3+2 锁轴）
    public double? LastA;
    public double? LastC;

    // 上一行进给
    public double? LastFeed;

    // 3+2 是否已锁轴
    public bool AxisLocked;

    // 本行是否刚锁轴（用于插入 CYCLE800）
    public bool AxisJustLocked;

    // 上一次输出的刀具（用于多段刀轨合并时去掉重复换刀）
    public string LastToolKey = string.Empty;

    // 当前刀轨段名称（用于 START_PATH/END_PATH 事件）
    public string CurrentPathName = string.Empty;

    // --- 孔循环（CYCLE/* -> Siemens MCALL + CYCLE8x/...）---
    // 是否处于孔循环定义生效期间
    public bool CycleActive;

    // 循环类别（DRILL/BORE/TAP）与子类型（DEEP/BRKCHP/BACK/...）
    public string CycleFamily = string.Empty;
    public string CycleVariant = string.Empty;

    // 解析得到的参数键值对（RAPTO/FEDTO/MMPM/...）
    public IReadOnlyDictionary<string, string> CycleParameters = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

    // 常用参数的数值缓存（便于快速生成默认输出）
    public double CycleRapTo;
    public double CycleFedTo;
    public double? CycleFeedRate;

    // 当前循环是否已输出”第一孔”的初始化（定位到 RAPTO + MCALL CYCLE...）
    public bool CycleInitialized;

    // ── 层间 AC 重置（EnableLayerReset 开启时有效）──
    public int LayerIndex;               // 当前工序层序号（0=未进入任何层）
    public bool NeedsSaveLayerRef;       // 下一个带 IJK 的 jindao 运动需保存为层参考 AC
    public double? CurrentLayerRefA;     // 当前层（或上一层）jindao 的参考 A，用于相邻层 IJK 相似度比较
    public double? CurrentLayerRefC;     // 当前层（或上一层）jindao 的参考 C
    public bool NeedsQieXueInit;         // 当前层 qiexue 需要从层参考初始化 AC

    // ── F 自适应变速（EnableFAdaptive 开启时有效）──
    public double? PrevSegmentR;          // 上一段的 r 值（Σ|ΔXYZ| / Σ|ΔAC|）
    public double? PrevPointX;            // 上一行 X（用于段差计算）
    public double? PrevPointY;            // 上一行 Y
    public double? PrevPointZ;            // 上一行 Z
    public double? PrevPointA;            // 上一行 A（解算前）
    public double? PrevPointC;            // 上一行 C（解算前）
    public double? OriginalSegmentF;      // 转弯降速前的原始 F，用于出弯恢复
    public bool FReduced;                 // 当前是否已降速
}
