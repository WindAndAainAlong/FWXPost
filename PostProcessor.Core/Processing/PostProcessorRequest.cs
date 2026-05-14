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
}
