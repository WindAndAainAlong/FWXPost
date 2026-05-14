using System.Collections.Generic;
using System.Globalization;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 行号处理：为每行增加 N 前缀序号。
/// </summary>
internal static class LineNumbering
{
    public static void Apply(List<string> lines, int start = 1, int step = 1)
    {
        if (lines == null || lines.Count == 0)
        {
            return;
        }

        var n = start;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            lines[i] = "N" + n.ToString(CultureInfo.InvariantCulture) + " " + line;
            n += step;
        }
    }
}
