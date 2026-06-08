using System.Text.RegularExpressions;
using PostProcessor.Core.Processing;

var clsPath = args.Length > 0 ? args[0] : @"i:\工作\05项目\蜂窝芯\FwxPostProcessing\凹面测试.cls";
var templatePath = @"i:\工作\05项目\蜂窝芯\FwxPostProcessing\PostProcessor.Core\Templating\Templates\Siemens_AC_TRAORI.tpl";

Console.WriteLine($"CLS: {clsPath}");
Console.WriteLine($"TPL: {templatePath}");
Console.WriteLine();

// Test with EnableLayerReset = true
var engine = new PostProcessorEngine();
var request = new PostProcessorRequest
{
    ClsPath = clsPath,
    TemplatePath = templatePath,
    EnableThreePlusTwoRotation = false,
    EnableLayerReset = true
};

var result = engine.Generate(request);

// Extract AC lines for analysis
var lines = result.NcText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
var acLines = new List<string>();
foreach (var line in lines)
{
    var trimmed = line.Trim();
    if (trimmed.Contains("A") && trimmed.Contains("C"))
    {
        acLines.Add(trimmed);
    }
}

Console.WriteLine($"Total NC lines: {lines.Length}");
Console.WriteLine($"Lines with A/C: {acLines.Count}");
Console.WriteLine();
Console.WriteLine("=== First 30 A/C lines ===");
for (var i = 0; i < Math.Min(30, acLines.Count); i++)
{
    Console.WriteLine($"  [{i:D3}] {acLines[i]}");
}

// Show lines around phase transitions (jumps in C by > 90 degrees)
Console.WriteLine();
Console.WriteLine("=== Detecting large C jumps (> 90 deg between lines) ===");
double? lastC = null;
var lineNum = 0;
foreach (var line in acLines)
{
    var cMatch = System.Text.RegularExpressions.Regex.Match(line, @"C(-?[\d.]+)");
    if (cMatch.Success && double.TryParse(cMatch.Groups[1].Value, out var c))
    {
        if (lastC.HasValue && Math.Abs(c - lastC.Value) > 90)
        {
            Console.WriteLine($"  Jump at line [{lineNum:D3}]: C {lastC.Value:F2} -> {c:F2} (delta={c - lastC.Value:F2})");
            Console.WriteLine($"    {line}");
        }
        lastC = c;
    }
    lineNum++;
}

Console.WriteLine();
Console.WriteLine("Done.");
