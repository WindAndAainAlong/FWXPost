using PostProcessor.Core.Templating;

namespace PostProcessor.Core.Processing;

/// <summary>
/// 后处理结果：包含 NC 文本与轴模式。
/// </summary>
public sealed class PostProcessorResult
{
    public PostProcessorResult(string ncText, AxisMode axisMode)
    {
        NcText = ncText;
        AxisMode = axisMode;
    }

    /// <summary>
    /// 生成后的 NC 文本。
    /// </summary>
    public string NcText { get; }

    /// <summary>
    /// 本次生成的轴模式。
    /// </summary>
    public AxisMode AxisMode { get; }
}
