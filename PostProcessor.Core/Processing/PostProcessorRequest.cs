using PostProcessor.Core.Templating;

namespace PostProcessor.Core.Processing;

/// <summary>
/// 后处理请求参数：用于调用统一入口生成 NC。
/// </summary>
public sealed class PostProcessorRequest
{
    /// <summary>
    /// CLS 文件路径。
    /// </summary>
    public string ClsPath { get; set; } = string.Empty;

    /// <summary>
    /// 模板文件路径（.tpl）。
    /// </summary>
    public string TemplatePath { get; set; } = string.Empty;

    /// <summary>
    /// 3+2 是否做坐标旋转（false 时按五轴联动输出）。
    /// </summary>
    public bool EnableThreePlusTwoRotation { get; set; } = false;

    /// <summary>
    /// 启用层间 AC 重置：第二层起 jindao 不从上一行连续，而是选择离第一层参考最近的解。
    /// </summary>
    public bool EnableLayerReset { get; set; } = false;

    /// <summary>
    /// 启用 F 自适应变速：切削阶段直走→转弯时按比例缩放 F 值。
    /// </summary>
    public bool EnableFAdaptive { get; set; } = false;
}
