using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfCompressView : UserControl
{
    private readonly PdfCompressService _compress = new();
    private SettingsService? _settings;
    private LocalizationService _loc = new();
    private Action<string>? _setStatus;

    public PdfCompressView()
    {
        InitializeComponent();
        LevelCombo.SelectedIndex = 1;
        ModeCombo.SelectedIndex = 0;
    }

    public void Initialize(SettingsService settings, LocalizationService loc, Action<string> setStatus)
    {
        _settings = settings;
        _loc = loc;
        _setStatus = setStatus;
        OutputPathBox.Text = settings.Current.DefaultSavePath;
        BatchFolderBox.Text = settings.Current.DefaultSavePath;
        ApplyLocalization(loc);
        UpdateGhostscriptHint();
    }

    public void RefreshSettings(AppSettings settings)
    {
        OutputPathBox.Text = settings.DefaultSavePath;
        BatchFolderBox.Text = settings.DefaultSavePath;
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        HintText.Text = loc.Get("CompressHint");
        ModeLabel.Text = loc.Get("CompressMode");
        SourceLabel.Text = loc.Get("CompressSource");
        BatchFolderLabel.Text = loc.Get("CompressBatchFolder");
        OutputLabel.Text = loc.Get("CompressOutput");
        LevelLabel.Text = loc.Get("CompressLevel");
        BrowseSourceButton.Content = loc.Get("Browse");
        BrowseBatchButton.Content = loc.Get("Browse");
        BrowseOutputButton.Content = loc.Get("Browse");
        CompressButton.Content = loc.Get("CompressButton");

        var modeIndex = ModeCombo.SelectedIndex;
        ModeCombo.Items.Clear();
        ModeCombo.Items.Add(loc.Get("CompressModeSingle"));
        ModeCombo.Items.Add(loc.Get("CompressModeBatch"));
        ModeCombo.SelectedIndex = modeIndex >= 0 ? modeIndex : 0;

        var selected = LevelCombo.SelectedIndex;
        LevelCombo.Items.Clear();
        LevelCombo.Items.Add(loc.Get("CompressLevelLow"));
        LevelCombo.Items.Add(loc.Get("CompressLevelMedium"));
        LevelCombo.Items.Add(loc.Get("CompressLevelHigh"));
        LevelCombo.Items.Add(loc.Get("CompressLevelMaximum"));
        LevelCombo.SelectedIndex = selected >= 0 ? selected : 1;

        UpdateModeVisibility();
        UpdateGhostscriptHint();
    }

    private void UpdateGhostscriptHint()
    {
        GhostscriptHint.Text = ToolLocator.HasGhostscript
            ? _loc.Get("CompressGhostscriptReady")
            : _loc.Get("CompressGhostscriptMissing");
    }

    private bool IsBatchMode => ModeCombo.SelectedIndex == 1;

    private void UpdateModeVisibility()
    {
        var batch = IsBatchMode;
        SourceLabel.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        SourcePathBox.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        BrowseSourceButton.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        BatchFolderLabel.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;
        BatchFolderRow.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;
        CompressButton.Content = batch ? _loc.Get("CompressBatchButton") : _loc.Get("CompressButton");
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        UpdateModeVisibility();
    }

    private PdfCompressLevel SelectedLevel => LevelCombo.SelectedIndex switch
    {
        0 => PdfCompressLevel.Low,
        2 => PdfCompressLevel.High,
        3 => PdfCompressLevel.Maximum,
        _ => PdfCompressLevel.Medium
    };

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = _loc.Get("EditorOpenFilter"),
            Title = _loc.Get("CompressSource")
        };
        if (dlg.ShowDialog() == true)
            SourcePathBox.Text = dlg.FileName;
    }

    private void BrowseBatchButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("CompressBatchFolder") };
        if (dlg.ShowDialog() == true)
            BatchFolderBox.Text = dlg.FolderName;
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("CompressOutput") };
        if (dlg.ShowDialog() == true)
            OutputPathBox.Text = dlg.FolderName;
    }

    private async void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputPathBox.Text) || !Directory.Exists(OutputPathBox.Text))
        {
            MessageBox.Show(_loc.Get("CompressSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        CompressButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("CompressProcessing"));
        ResultText.Text = _loc.Get("CompressProcessing");

        try
        {
            if (IsBatchMode)
            {
                if (string.IsNullOrWhiteSpace(BatchFolderBox.Text) || !Directory.Exists(BatchFolderBox.Text))
                {
                    MessageBox.Show(_loc.Get("CompressSelectBatchFolder"), _loc.Get("AppTitle"));
                    return;
                }

                var count = await Task.Run(() =>
                    _compress.CompressFolder(BatchFolderBox.Text, OutputPathBox.Text, SelectedLevel));

                var summary = string.Format(_loc.Get("CompressBatchDone"), count);
                ResultText.Text = summary;
                _setStatus?.Invoke(summary);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(SourcePathBox.Text) || !File.Exists(SourcePathBox.Text))
                {
                    MessageBox.Show(_loc.Get("CompressSelectFile"), _loc.Get("AppTitle"));
                    return;
                }

                var input = SourcePathBox.Text;
                var outputName = Path.GetFileNameWithoutExtension(input) + "_compressed.pdf";
                var output = Path.Combine(OutputPathBox.Text, outputName);
                var before = new FileInfo(input).Length;

                await Task.Run(() => _compress.Compress(input, output, SelectedLevel));
                var after = new FileInfo(output).Length;
                var summary = string.Format(
                    _loc.Get("CompressDone"),
                    outputName,
                    FormatHelpers.FormatBytes(before),
                    FormatHelpers.FormatBytes(after));
                ResultText.Text = summary;
                _setStatus?.Invoke(summary);
            }
        }
        catch (Exception ex)
        {
            ResultText.Text = _loc.Get("CompressFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            _setStatus?.Invoke(_loc.Get("CompressFailed"));
        }
        finally
        {
            CompressButton.IsEnabled = true;
        }
    }
}
