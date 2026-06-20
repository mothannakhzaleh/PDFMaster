using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfToolsView : UserControl
{
    private readonly PdfSplitService _split = new();
    private readonly PdfDocumentService _document = new();
    private SettingsService? _settings;
    private LocalizationService _loc = new();
    private Action<string>? _setStatus;

    private readonly List<string> _imagesToPdfPaths = [];

    public PdfToolsView()
    {
        InitializeComponent();
        SplitModeCombo.SelectionChanged += (_, _) => UpdateSplitParamHint();
    }

    public void Initialize(SettingsService settings, LocalizationService loc, Action<string> setStatus)
    {
        _settings = settings;
        _loc = loc;
        _setStatus = setStatus;
        SplitOutputBox.Text = settings.Current.DefaultSavePath;
        ExtractOutputBox.Text = settings.Current.DefaultSavePath;
        WatermarkOutputBox.Text = settings.Current.DefaultSavePath;
        ImagesToPdfOutputBox.Text = settings.Current.DefaultSavePath;
        ApplyLocalization(loc);
    }

    public void RefreshSettings(AppSettings settings)
    {
        SplitOutputBox.Text = settings.DefaultSavePath;
        ExtractOutputBox.Text = settings.DefaultSavePath;
        WatermarkOutputBox.Text = settings.DefaultSavePath;
        ImagesToPdfOutputBox.Text = settings.DefaultSavePath;
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        SplitHint.Text = loc.Get("SplitHint");
        SplitSourceLabel.Text = loc.Get("ToolsSource");
        SplitModeLabel.Text = loc.Get("SplitMode");
        SplitParamLabel.Text = loc.Get("SplitParam");
        SplitOutputLabel.Text = loc.Get("CompressOutput");
        SplitBrowseButton.Content = loc.Get("Browse");
        SplitOutputBrowseButton.Content = loc.Get("Browse");
        SplitButton.Content = loc.Get("SplitButton");

        ExtractHint.Text = loc.Get("ExtractHint");
        ExtractSourceLabel.Text = loc.Get("ToolsSource");
        ExtractRangeLabel.Text = loc.Get("ExtractRange");
        ExtractOutputLabel.Text = loc.Get("ExtractOutputName");
        ExtractBrowseButton.Content = loc.Get("Browse");
        ExtractOutputBrowseButton.Content = loc.Get("Browse");
        ExtractButton.Content = loc.Get("ExtractButton");

        WatermarkHint.Text = loc.Get("WatermarkHint");
        WatermarkSourceLabel.Text = loc.Get("ToolsSource");
        WatermarkTextLabel.Text = loc.Get("WatermarkText");
        WatermarkDiagonalCheck.Content = loc.Get("WatermarkDiagonal");
        WatermarkOutputLabel.Text = loc.Get("ExtractOutputName");
        WatermarkBrowseButton.Content = loc.Get("Browse");
        WatermarkOutputBrowseButton.Content = loc.Get("Browse");
        WatermarkButton.Content = loc.Get("WatermarkButton");

        ImagesToPdfHint.Text = loc.Get("ImagesToPdfHint");
        ImagesToPdfSourceLabel.Text = loc.Get("ImagesToPdfSource");
        ImagesToPdfBrowseButton.Content = loc.Get("ImagesToPdfBrowse");
        ImagesToPdfOutputLabel.Text = loc.Get("ImagesToPdfOutput");
        ImagesToPdfOutputBrowseButton.Content = loc.Get("Browse");
        ImagesToPdfButton.Content = loc.Get("ImagesToPdfButton");

        var modeIndex = SplitModeCombo.SelectedIndex;
        SplitModeCombo.Items.Clear();
        SplitModeCombo.Items.Add(loc.Get("SplitModeEveryPage"));
        SplitModeCombo.Items.Add(loc.Get("SplitModeEveryN"));
        SplitModeCombo.Items.Add(loc.Get("SplitModeAtPages"));
        SplitModeCombo.SelectedIndex = modeIndex >= 0 ? modeIndex : 0;
        UpdateSplitParamHint();
    }

    private void UpdateSplitParamHint()
    {
        SplitParamBox.Tag = SplitModeCombo.SelectedIndex switch
        {
            0 => _loc.Get("SplitParamEveryPage"),
            1 => _loc.Get("SplitParamEveryN"),
            _ => _loc.Get("SplitParamAtPages")
        };
    }

    private void SplitBrowseButton_Click(object sender, RoutedEventArgs e) =>
        PickPdf(SplitSourceBox);

    private void SplitOutputBrowseButton_Click(object sender, RoutedEventArgs e) =>
        PickFolder(SplitOutputBox);

    private void ExtractBrowseButton_Click(object sender, RoutedEventArgs e) =>
        PickPdf(ExtractSourceBox);

    private void ExtractOutputBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = _loc.Get("EditorSaveFilter"),
            FileName = "extracted.pdf",
            InitialDirectory = _settings?.Current.DefaultSavePath
        };
        if (dlg.ShowDialog() == true)
            ExtractOutputBox.Text = dlg.FileName;
    }

    private void WatermarkBrowseButton_Click(object sender, RoutedEventArgs e) =>
        PickPdf(WatermarkSourceBox);

    private void WatermarkOutputBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = _loc.Get("EditorSaveFilter"),
            FileName = "watermarked.pdf",
            InitialDirectory = _settings?.Current.DefaultSavePath
        };
        if (dlg.ShowDialog() == true)
            WatermarkOutputBox.Text = dlg.FileName;
    }

    private void PickPdf(TextBox target)
    {
        var dlg = new OpenFileDialog { Filter = _loc.Get("EditorOpenFilter") };
        if (dlg.ShowDialog() == true)
            target.Text = dlg.FileName;
    }

    private void PickFolder(TextBox target)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true)
            target.Text = dlg.FolderName;
    }

    private async void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SplitSourceBox.Text) || !File.Exists(SplitSourceBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectFile"), _loc.Get("AppTitle"));
            return;
        }

        if (string.IsNullOrWhiteSpace(SplitOutputBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        var prefix = Path.GetFileNameWithoutExtension(SplitSourceBox.Text);
        SplitButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("SplitProcessing"));

        try
        {
            IReadOnlyList<string> outputs = SplitModeCombo.SelectedIndex switch
            {
                0 => await Task.Run(() => _split.SplitEveryPage(SplitSourceBox.Text, SplitOutputBox.Text, prefix)),
                1 when int.TryParse(SplitParamBox.Text.Trim(), out var n) && n > 0 =>
                    await Task.Run(() => _split.SplitEveryNPages(SplitSourceBox.Text, SplitOutputBox.Text, prefix, n)),
                2 => await Task.Run(() => _split.SplitAtPages(
                    SplitSourceBox.Text,
                    SplitOutputBox.Text,
                    prefix,
                    SplitParamBox.Text.Split(',', ';', ' ')
                        .Select(s => int.TryParse(s.Trim(), out var p) ? p : 0)
                        .Where(p => p > 0)
                        .ToList())),
                _ => throw new InvalidOperationException(_loc.Get("SplitInvalidParam"))
            };

            var summary = string.Format(_loc.Get("SplitDone"), outputs.Count);
            SplitResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            SplitResultText.Text = _loc.Get("SplitFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SplitButton.IsEnabled = true;
        }
    }

    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExtractSourceBox.Text) || !File.Exists(ExtractSourceBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectFile"), _loc.Get("AppTitle"));
            return;
        }

        if (!PdfSplitService.TryParsePageRange(ExtractRangeBox.Text, out var from, out var to))
        {
            MessageBox.Show(_loc.Get("ExtractInvalidRange"), _loc.Get("AppTitle"));
            return;
        }

        var output = ExtractOutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            MessageBox.Show(_loc.Get("ExtractSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        ExtractButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("ExtractProcessing"));

        try
        {
            await Task.Run(() => _split.ExtractPageRange(
                ExtractSourceBox.Text,
                output,
                from,
                to,
                _settings?.Current.DefaultCompressLevel ?? PdfCompressLevel.Medium));

            var summary = string.Format(_loc.Get("ExtractDone"), output);
            ExtractResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            ExtractResultText.Text = _loc.Get("ExtractFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    private void ImagesToPdfBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = _loc.Get("EditorImageFilter"),
            Multiselect = true,
            Title = _loc.Get("ImagesToPdfBrowse")
        };
        if (dlg.ShowDialog() != true)
            return;

        _imagesToPdfPaths.Clear();
        _imagesToPdfPaths.AddRange(dlg.FileNames.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        ImagesToPdfSourceBox.Text = string.Join(Environment.NewLine, _imagesToPdfPaths.Select(Path.GetFileName));
    }

    private void ImagesToPdfOutputBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = _loc.Get("EditorSaveFilter"),
            FileName = "images.pdf",
            InitialDirectory = _settings?.Current.DefaultSavePath
        };
        if (dlg.ShowDialog() == true)
            ImagesToPdfOutputBox.Text = dlg.FileName;
    }

    private async void ImagesToPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_imagesToPdfPaths.Count == 0)
        {
            MessageBox.Show(_loc.Get("ImagesToPdfSelectImages"), _loc.Get("AppTitle"));
            return;
        }

        var output = ImagesToPdfOutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            MessageBox.Show(_loc.Get("ExtractSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        if (!output.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            output += ".pdf";

        ImagesToPdfButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("ImagesToPdfProcessing"));

        try
        {
            await Task.Run(() => PdfDocumentService.CreatePdfFromImages(_imagesToPdfPaths, output));
            var summary = string.Format(_loc.Get("ImagesToPdfDone"), _imagesToPdfPaths.Count, output);
            ImagesToPdfResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            ImagesToPdfResultText.Text = _loc.Get("ImagesToPdfFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImagesToPdfButton.IsEnabled = true;
        }
    }

    private async void WatermarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WatermarkSourceBox.Text) || !File.Exists(WatermarkSourceBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectFile"), _loc.Get("AppTitle"));
            return;
        }

        if (string.IsNullOrWhiteSpace(WatermarkTextBox.Text))
        {
            MessageBox.Show(_loc.Get("WatermarkEnterText"), _loc.Get("AppTitle"));
            return;
        }

        var output = WatermarkOutputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            MessageBox.Show(_loc.Get("ExtractSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        WatermarkButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("WatermarkProcessing"));

        try
        {
            await Task.Run(() =>
            {
                _document.Close();
                _document.Open(WatermarkSourceBox.Text);
                _document.AddTextWatermark(WatermarkTextBox.Text, 48, 0.35, WatermarkDiagonalCheck.IsChecked == true);
                _document.Save(output, false, PdfCompressLevel.Medium);
                _document.Close();
            });

            var summary = string.Format(_loc.Get("WatermarkDone"), output);
            WatermarkResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            WatermarkResultText.Text = _loc.Get("WatermarkFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            WatermarkButton.IsEnabled = true;
        }
    }
}
