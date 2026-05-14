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
}
