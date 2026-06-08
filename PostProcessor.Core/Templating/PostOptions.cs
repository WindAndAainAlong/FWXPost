namespace PostProcessor.Core.Templating;

/// <summary>
/// 后处理选项：用于控制 3+2 与五轴联动工况。
/// </summary>
public sealed class PostOptions
{
    /// <summary>
    /// true：3+2 做坐标旋转并输出 CYCLE800
    /// false：不旋转，按五轴联动输出
    /// </summary>
    public bool EnableThreePlusTwoRotation { get; set; } = false;

    /// <summary>
    /// true：启用层间 AC 重置。当各层 jindao 的 IJK 相近时，
    /// 第二层起 jindao 不从上一行 GOTO 连续 AC，而是选择离第一层 jindao 参考 AC 最近的等效解，
    /// 避免跨层 A 正负号翻转导致机床大幅旋转。
    /// </summary>
    public bool EnableLayerReset { get; set; } = false;

    /// <summary>
    /// 启用 F 自适应变速：切削阶段检测相邻三段运动模式切换（直走→转弯），
    /// 进入转弯时对 F 值按比例缩放（默认 ×0.15）。
    /// </summary>
    public bool EnableFAdaptive { get; set; } = false;
}
