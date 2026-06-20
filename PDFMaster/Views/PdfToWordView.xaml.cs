using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfToWordView : UserControl
{
    private readonly PdfToWordService _converter = new();
    private SettingsService? _settings;
    private LocalizationService _loc = new();
    private Action<string>? _setStatus;

    public PdfToWordView()
    {
        InitializeComponent();
    }

    public void Initialize(SettingsService settings, LocalizationService loc, Action<string> setStatus)
    {
        _settings = settings;
        _loc = loc;
        _setStatus = setStatus;
        OutputPathBox.Text = settings.Current.DefaultSavePath;
        ApplyLocalization(loc);
    }

    public void RefreshSettings(AppSettings settings) =>
        OutputPathBox.Text = settings.DefaultSavePath;

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        HintText.Text = loc.Get("PdfToWordHint");
        SourceLabel.Text = loc.Get("ToolsSource");
        PageRangeLabel.Text = loc.Get("ExtractRange");
        OutputLabel.Text = loc.Get("PdfToWordOutput");
        BrowseSourceButton.Content = loc.Get("Browse");
        BrowseOutputButton.Content = loc.Get("Browse");
        ConvertButton.Content = loc.Get("PdfToWordButton");
        PreserveLayoutCheck.Content = loc.Get("PdfToWordPreserveLayout");
        PageRangeBox.Tag = loc.Get("ExtractRangeExample");
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = _loc.Get("EditorOpenFilter"),
            Title = _loc.Get("ToolsSource")
        };
        if (dlg.ShowDialog() != true)
            return;

        SourcePathBox.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(OutputPathBox.Text) ||
            Directory.Exists(OutputPathBox.Text))
        {
            OutputPathBox.Text = Path.Combine(
                Path.GetDirectoryName(dlg.FileName) ?? _settings?.Current.DefaultSavePath ?? string.Empty,
                Path.GetFileNameWithoutExtension(dlg.FileName) + ".docx");
        }
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var suggested = string.IsNullOrWhiteSpace(SourcePathBox.Text)
            ? "converted.docx"
            : Path.GetFileNameWithoutExtension(SourcePathBox.Text) + ".docx";

        var dlg = new SaveFileDialog
        {
            Filter = _loc.Get("PdfToWordFilter"),
            FileName = suggested,
            InitialDirectory = _settings?.Current.DefaultSavePath
        };
        if (dlg.ShowDialog() == true)
            OutputPathBox.Text = dlg.FileName;
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourcePathBox.Text) || !File.Exists(SourcePathBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectFile"), _loc.Get("AppTitle"));
            return;
        }

        int from;
        int to;
        var rangeText = PageRangeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            from = 1;
            to = int.MaxValue;
        }
        else if (rangeText.EndsWith('-') && int.TryParse(rangeText.TrimEnd('-'), out var rangeStart))
        {
            from = rangeStart;
            to = int.MaxValue;
        }
        else if (!PdfSplitService.TryParsePageRange(rangeText, out from, out to))
        {
            MessageBox.Show(_loc.Get("ExtractInvalidRange"), _loc.Get("AppTitle"));
            return;
        }

        var output = OutputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            MessageBox.Show(_loc.Get("PdfToWordSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        if (!output.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            output += ".docx";

        var sourcePath = SourcePathBox.Text;
        var progressFormat = _loc.Get("PdfToWordProgress");

        ConvertButton.IsEnabled = false;
        ResultText.Text = string.Empty;
        _setStatus?.Invoke(_loc.Get("PdfToWordProcessing"));

        try
        {
            var progress = new Progress<int>(current =>
                _setStatus?.Invoke(string.Format(progressFormat, current)));
            var preserveLayout = PreserveLayoutCheck.IsChecked == true;
            var pageCount = await Task.Run(() =>
                _converter.Convert(sourcePath, output, from, to, preserveLayout, progress));

            var summary = string.Format(_loc.Get("PdfToWordDone"), pageCount, output);
            ResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            ResultText.Text = _loc.Get("PdfToWordFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }
}
