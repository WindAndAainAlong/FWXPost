using System;
using System.Globalization;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 去重输出辅助：相邻相同坐标/进给时输出空字符串。
/// </summary>
internal static class OutputLineProcessor
{
    public static string UpdateAxisField(string axis, double value, ref double? lastValue)
    {
        if (lastValue.HasValue && NearlyEqual(lastValue.Value, value))
        {
            return string.Empty;
        }

        lastValue = value;
        return axis + Format(value);
    }

    public static string UpdateFeedField(double? feedRate, ref double? lastFeed)
    {
        if (!feedRate.HasValue)
        {
            return string.Empty;
        }

        if (lastFeed.HasValue && NearlyEqual(lastFeed.Value, feedRate.Value))
        {
            return string.Empty;
        }

        lastFeed = feedRate.Value;
        return "F" + Format(feedRate.Value);
    }

    public static bool NearlyEqual(double a, double b)
    {
        return Math.Abs(a - b) <= 1e-6;
    }

    private static string Format(double value)
        => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
