using System;
using System.Collections.Generic;
using System.IO;

namespace PostProcessor.Core.Templating;

/// <summary>
/// 模板定义：按 [SECTION] 分段读取并存储。
/// </summary>
public sealed class TemplateDefinition
{
    private readonly Dictionary<string, List<string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 从文件加载模板。
    /// </summary>
    public static TemplateDefinition LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        return Parse(lines);
    }

    /// <summary>
    /// 解析模板内容：遇到 [SECTION] 开始新分段。
    /// </summary>
    public static TemplateDefinition Parse(IEnumerable<string> lines)
    {
        var template = new TemplateDefinition();
        string? currentSection = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("[") && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = line[1..^1].Trim();
                if (!template._sections.ContainsKey(currentSection))
                {
                    template._sections[currentSection] = new List<string>();
                }
                continue;
            }

            if (currentSection == null)
            {
                continue;
            }

            template._sections[currentSection].Add(rawLine);
        }

        return template;
    }

    /// <summary>
    /// 获取指定区块的行集合。
    /// </summary>
    public IReadOnlyList<string> GetSection(string name)
    {
        return _sections.TryGetValue(name, out var lines) ? lines : Array.Empty<string>();
    }

    /// <summary>
    /// 判断是否存在指定区块。
    /// </summary>
    public bool HasSection(string name)
    {
        return _sections.ContainsKey(name);
    }
}
