using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfMergeView : UserControl
{
    private readonly PdfMergeService _merge = new();
    private readonly PdfRenderService _render = new();
    private readonly ObservableCollection<PdfMergeItem> _items = [];
    private SettingsService? _settings;
    private LocalizationService _loc = new();
    private Action<string>? _setStatus;

    public PdfMergeView()
    {
        InitializeComponent();
        FilesList.ItemsSource = _items;
    }

    public void Initialize(SettingsService settings, LocalizationService loc, Action<string> setStatus)
    {
        _settings = settings;
        _loc = loc;
        _setStatus = setStatus;
        OutputFolderBox.Text = settings.Current.DefaultSavePath;
        ApplyLocalization(loc);
    }

    public void RefreshSettings(AppSettings settings) => OutputFolderBox.Text = settings.DefaultSavePath;

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        HintText.Text = loc.Get("MergeHint");
        AddFilesButton.Content = loc.Get("MergeAddFiles");
        RemoveButton.Content = loc.Get("MergeRemove");
        MoveUpButton.Content = loc.Get("MergeMoveUp");
        MoveDownButton.Content = loc.Get("MergeMoveDown");
        ClearButton.Content = loc.Get("MergeClear");
        OutputNameLabel.Text = loc.Get("MergeOutputName");
        OutputFolderLabel.Text = loc.Get("MergeOutputFolder");
        BrowseOutputButton.Content = loc.Get("Browse");
        MergeButton.Content = loc.Get("MergeButton");
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = _loc.Get("EditorOpenFilter"),
            Title = _loc.Get("MergeAddFiles"),
            Multiselect = true
        };
        if (dlg.ShowDialog() != true)
            return;

        foreach (var path in dlg.FileNames)
            AddFile(path);

        RefreshOrder();
    }

    private void AddFile(string path)
    {
        if (_items.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var pages = _render.GetPageCount(path);
        _items.Add(new PdfMergeItem
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            PageCount = pages,
            Order = _items.Count + 1
        });
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not PdfMergeItem item)
            return;

        _items.Remove(item);
        RefreshOrder();
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var index = FilesList.SelectedIndex;
        if (index < 0)
            return;

        var target = index + delta;
        if (target < 0 || target >= _items.Count)
            return;

        _items.Move(index, target);
        RefreshOrder();
        FilesList.SelectedIndex = target;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
        ResultText.Text = string.Empty;
    }

    private void RefreshOrder()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].Order = i + 1;
            _items[i].NotifyDisplayChanged();
        }
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("MergeOutputFolder") };
        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private void FilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show(_loc.Get("MergeNeedTwo"), _loc.Get("AppTitle"));
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolderBox.Text) || !Directory.Exists(OutputFolderBox.Text))
        {
            MessageBox.Show(_loc.Get("MergeSelectOutput"), _loc.Get("AppTitle"));
            return;
        }

        var fileName = FormatHelpers.SanitizeFileName(OutputNameBox.Text.Trim());
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            fileName += ".pdf";

        var output = Path.Combine(OutputFolderBox.Text, fileName);
        var paths = _items.OrderBy(i => i.Order).Select(i => i.FilePath).ToList();
        var level = _settings?.Current.DefaultCompressLevel ?? PdfCompressLevel.Medium;

        MergeButton.IsEnabled = false;
        _setStatus?.Invoke(_loc.Get("MergeProcessing"));
        ResultText.Text = _loc.Get("MergeProcessing");

        try
        {
            await Task.Run(() => _merge.Merge(paths, output, level));
            var summary = string.Format(_loc.Get("MergeDone"), paths.Count, fileName);
            ResultText.Text = summary;
            _setStatus?.Invoke(summary);
        }
        catch (Exception ex)
        {
            ResultText.Text = _loc.Get("MergeFailed");
            MessageBox.Show(ex.Message, _loc.Get("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            _setStatus?.Invoke(_loc.Get("MergeFailed"));
        }
        finally
        {
            MergeButton.IsEnabled = true;
        }
    }
}
