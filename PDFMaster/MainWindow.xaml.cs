using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings = new();
    private readonly ThemeService _theme = new();
    private readonly LocalizationService _loc = new();

    public MainWindow()
    {
        InitializeComponent();
        _settings.Load();

        BindLanguageCombo();
        LanguageCombo.SelectedValue = _settings.Current.Language;

        _theme.Apply(_settings.Current.Theme);
        _loc.SetLanguage(_settings.Current.Language);

        EditorPanel.Initialize(_settings, _loc, UpdateStatus);
        CompressPanel.Initialize(_settings, _loc, UpdateStatus);
        MergePanel.Initialize(_settings, _loc, UpdateStatus);
        ToolsPanel.Initialize(_settings, _loc, UpdateStatus);
        PdfToWordPanel.Initialize(_settings, _loc, UpdateStatus);

        ApplyLocalization();
    }

    private void BindLanguageCombo()
    {
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Language);
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();
    }

    private void ApplyLocalization()
    {
        Title = _loc.Get("AppTitle");
        TitleText.Text = _loc.Get("AppTitle");
        SubtitleText.Text = _loc.Get("AppSubtitle");
        SettingsButton.Content = _loc.Get("Settings");
        ThemeButton.Content = _loc.Get("Theme");
        AboutButton.Content = _loc.Get("About");
        EditorTab.Header = _loc.Get("TabEditor");
        CompressTab.Header = _loc.Get("TabCompress");
        MergeTab.Header = _loc.Get("TabMerge");
        ToolsTab.Header = _loc.Get("TabTools");
        PdfToWordTab.Header = _loc.Get("TabPdfToWord");
        StatusText.Text = _loc.Get("Ready");

        RootLayoutGrid.FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        EditorPanel.ApplyLocalization(_loc);
        CompressPanel.ApplyLocalization(_loc);
        MergePanel.ApplyLocalization(_loc);
        ToolsPanel.ApplyLocalization(_loc);
        PdfToWordPanel.ApplyLocalization(_loc);
    }

    private void UpdateStatus(string message) => StatusText.Text = message;

    private void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedValue is not AppLanguage language)
            return;

        _loc.SetLanguage(language);
        _settings.Current.Language = language;
        _settings.Save(_settings.Current);
        ApplyLocalization();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = _theme.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next);
        _settings.Current.Theme = next;
        _settings.Save(_settings.Current);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings.Current, _loc) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultSettings is null)
            return;

        _settings.Save(dlg.ResultSettings);
        _theme.Apply(dlg.ResultSettings.Theme);
        _loc.SetLanguage(dlg.ResultSettings.Language);
        LanguageCombo.SelectedValue = dlg.ResultSettings.Language;
        ApplyLocalization();

        EditorPanel.RefreshSettings(_settings.Current);
        CompressPanel.RefreshSettings(_settings.Current);
        MergePanel.RefreshSettings(_settings.Current);
        ToolsPanel.RefreshSettings(_settings.Current);
        PdfToWordPanel.RefreshSettings(_settings.Current);
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AboutWindow(_loc) { Owner = this };
        dlg.ShowDialog();
    }

    private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        StatusText.Text = _loc.Get("Ready");
    }

    private void CopyrightLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, ex.Message, _loc.Get("AppTitle"), _loc.Get("Ok"), MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);
}
