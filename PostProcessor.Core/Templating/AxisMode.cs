namespace PostProcessor.Core.Templating;

/// <summary>
/// 轴模式枚举：用于模板判断与输出逻辑分支。
/// </summary>
public enum AxisMode
{
    /// <summary>三轴（无刀轴或 A/C 约等于 0）。</summary>
    ThreeAxis,
    /// <summary>3+2（刀轴固定不变，A/C 只输出一次）。</summary>
    ThreePlusTwo,
    /// <summary>五轴联动（刀轴变化，A/C 每行输出）。</summary>
    FiveAxis
}
