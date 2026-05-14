using System;
using System.Collections.Generic;
using System.IO;
using PostProcessor.Core.IR;
using PostProcessor.Core.Parsing;
using PostProcessor.Core.Templating;

namespace PostProcessor.Core.Processing;

/// <summary>
/// 后处理统一入口：对外只暴露 Generate 接口。
/// </summary>
public sealed class PostProcessorEngine
{
    public PostProcessorResult Generate(PostProcessorRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequest(request);

        // 1) 解析 CLS
        var program = ParseCls(request.ClsPath);

        // 2) 加载模板
        var template = LoadTemplate(request.TemplatePath);

        // 3) 计算轴模式
        var (axisMode, post) = ResolveAxisMode(program, request.EnableThreePlusTwoRotation);

        // 4) 渲染模板
        var ncText = RenderTemplate(program, template, post, axisMode);

        // 5) 返回结果
        return new PostProcessorResult(ncText, axisMode);
    }

    private static void ValidateRequest(PostProcessorRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClsPath))
        {
            throw new ArgumentException("ClsPath is required.", nameof(request.ClsPath));
        }

        if (string.IsNullOrWhiteSpace(request.TemplatePath))
        {
            throw new ArgumentException("TemplatePath is required.", nameof(request.TemplatePath));
        }

        var clsPath = request.ClsPath.Trim();
        if (!File.Exists(clsPath) && !Directory.Exists(clsPath))
        {
            // 支持用 ; 或 | 传入多个文件路径
            var parts = SplitInputPaths(clsPath);
            if (parts.Count == 0)
            {
                throw new FileNotFoundException("CLS file/folder not found.", request.ClsPath);
            }

            foreach (var p in parts)
            {
                if (!File.Exists(p))
                {
                    throw new FileNotFoundException("CLS file not found.", p);
                }
            }
        }

        if (!File.Exists(request.TemplatePath))
        {
            throw new FileNotFoundException("Template file not found.", request.TemplatePath);
        }
    }

    private static ToolpathProgram ParseCls(string clsPath)
    {
        var parser = new ClsParser();

        // 支持多个文件路径：a.cls|b.cls 或 a.cls;b.cls
        var explicitFiles = SplitInputPaths(clsPath);
        if (explicitFiles.Count > 0)
        {
            return ParseMany(parser, explicitFiles);
        }

        // 允许输入为文件夹：合并文件夹内的多个 .cls（按文件名排序）
        if (Directory.Exists(clsPath))
        {
            var files = Directory.GetFiles(clsPath, "*.cls", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0)
            {
                throw new FileNotFoundException("No .cls files found in the folder.", clsPath);
            }

            return ParseMany(parser, files, programNameOverride: new DirectoryInfo(clsPath).Name);
        }

        return parser.Parse(clsPath);
    }

    private static ToolpathProgram ParseMany(ClsParser parser, IReadOnlyList<string> files, string? programNameOverride = null)
    {
        var mergedBlocks = new List<IRBlock>();
        var sequence = 1;

        var programName = programNameOverride;
        if (string.IsNullOrWhiteSpace(programName))
        {
            programName = Path.GetFileNameWithoutExtension(files[0]) ?? "MergedProgram";
        }

        foreach (var file in files)
        {
            var part = parser.Parse(file);

            var hasPathMarkers = false;
            foreach (var b in part.Blocks)
            {
                if (b is PathStartBlock)
                {
                    hasPathMarkers = true;
                    break;
                }
            }

            // 如果单个文件没有 TOOL PATH/.. 分段，则把该文件包装成一个 PATH，便于合并后仍能分段输出
            if (!hasPathMarkers)
            {
                mergedBlocks.Add(new PathStartBlock
                {
                    Sequence = sequence++,
                    PathName = Path.GetFileNameWithoutExtension(file) ?? string.Empty,
                    ToolName = string.Empty
                });
            }

            foreach (var block in part.Blocks)
            {
                // 重排 Sequence，保证合并后顺序连续
                mergedBlocks.Add(block with { Sequence = sequence++ });
            }

            if (!hasPathMarkers)
            {
                mergedBlocks.Add(new PathEndBlock { Sequence = sequence++ });
            }
        }

        return new ToolpathProgram(programName, mergedBlocks);
    }

    private static List<string> SplitInputPaths(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new List<string>();
        }

        // 允许用户用 ; 或 | 传入多个文件
        if (input.IndexOf('|') < 0 && input.IndexOf(';') < 0)
        {
            return new List<string>();
        }

        var parts = input.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0)
            {
                list.Add(t);
            }
        }
        return list;
    }

    private static TemplateDefinition LoadTemplate(string templatePath)
    {
        return TemplateDefinition.LoadFromFile(templatePath);
    }

    private static (AxisMode AxisMode, TemplatePostProcessor Post) ResolveAxisMode(ToolpathProgram program, bool enableRotation)
    {
        var options = new PostOptions { EnableThreePlusTwoRotation = enableRotation };
        var post = new TemplatePostProcessor(options);
        var axisMode = post.GetAxisMode(program);
        return (axisMode, post);
    }

    private static string RenderTemplate(ToolpathProgram program, TemplateDefinition template, TemplatePostProcessor post, AxisMode axisMode)
    {
        var lines = post.Generate(program, template, axisMode);
        return string.Join(Environment.NewLine, lines);
    }
}
