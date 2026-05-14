using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PostProcessor.Core.Processing;

namespace PostProcessor.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Set this to false to disable 3+2 rotation and force 5-axis output for 3+2 toolpaths.
    private const bool EnableThreePlusTwoRotation = false;

    public MainWindow()
    {
        InitializeComponent();
        TemplatePathTextBox.Text = ResolveDefaultTemplatePath() ?? string.Empty;
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CLS Files (*.cls)|*.cls|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            // 支持选择多个 CLS：
            // - 单个：直接填路径
            // - 多个：用 | 连接（Core 会按顺序合并）
            InputPathTextBox.Text = dialog.FileNames.Length <= 1
                ? dialog.FileName
                : string.Join("|", dialog.FileNames);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "NC Files (*.nc;*.mpf)|*.nc;*.mpf|All Files (*.*)|*.*",
            AddExtension = true,
            FileName = "output.nc"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Template Files (*.tpl)|*.tpl|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TemplatePathTextBox.Text = dialog.FileName;
        }
    }

    private void PreviewNc_Click(object sender, RoutedEventArgs e)
    {
        var inputPath = InputPathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            MessageBox.Show(this, "Please select an input CLS file first.", "NC Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(inputPath))
        {
            MessageBox.Show(this, "The input CLS file does not exist.", "NC Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var templatePath = TemplatePathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            MessageBox.Show(this, "Please select a template file first.", "NC Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!File.Exists(templatePath))
        {
            MessageBox.Show(this, "The template file does not exist.", "NC Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var engine = new PostProcessorEngine();
            var request = new PostProcessorRequest
            {
                ClsPath = inputPath,
                TemplatePath = templatePath,
                EnableThreePlusTwoRotation = EnableThreePlusTwoRotation
            };

            var result = engine.Generate(request);
            NcPreviewTextBox.Text = result.NcText;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Preview failed: {ex.Message}", "NC Preview", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveNc_Click(object sender, RoutedEventArgs e)
    {
        var outputPath = OutputPathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            MessageBox.Show(this, "Please select an output file first.", "NC Save", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ncText = NcPreviewTextBox.Text;
        if (string.IsNullOrWhiteSpace(ncText))
        {
            MessageBox.Show(this, "NC preview is empty. Please run Preview first.", "NC Save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, ncText);
            MessageBox.Show(this, "NC saved successfully.", "NC Save", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed: {ex.Message}", "NC Save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? ResolveDefaultTemplatePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Templates", "Siemens_AC_TRAORI.tpl");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var probe = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "PostProcessor.Core", "Templating", "Templates", "Siemens_AC_TRAORI.tpl"));
        return File.Exists(probe) ? probe : null;
    }
}
