using System.IO;
using System.Windows;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster;

public partial class SettingsWindow : Window
{
    private readonly LocalizationService _loc;
    public AppSettings? ResultSettings { get; private set; }

    public SettingsWindow(AppSettings current, LocalizationService loc)
    {
        InitializeComponent();
        _loc = loc;

        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Language);
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();

        LoadSettings(current);
        ApplyLabels();
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void LoadSettings(AppSettings settings)
    {
        SavePathBox.Text = settings.DefaultSavePath;
        ThemeCombo.SelectedIndex = settings.Theme == AppTheme.Light ? 1 : 0;
        LanguageCombo.SelectedValue = settings.Language;
        CompressOnSaveCheck.IsChecked = settings.CompressOnSave;
    }

    private void ApplyLabels()
    {
        Title = _loc.Get("Settings");
        HeaderText.Text = _loc.Get("Settings");
        SavePathLabel.Text = _loc.Get("SavePath");
        ThemeLabel.Text = _loc.Get("Theme");
        LanguageLabel.Text = _loc.Get("Language");
        CompressOnSaveCheck.Content = _loc.Get("EditorCompressOnSave");
        SaveButton.Content = _loc.Get("Save");
        CancelButton.Content = _loc.Get("Cancel");
        ResetButton.Content = _loc.Get("Reset");

        var selectedTheme = ThemeCombo.SelectedIndex;
        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(_loc.Get("ThemeDark"));
        ThemeCombo.Items.Add(_loc.Get("ThemeLight"));
        ThemeCombo.SelectedIndex = selectedTheme >= 0 ? selectedTheme : 0;
    }

    private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("SavePath") };
        if (dlg.ShowDialog() == true)
            SavePathBox.Text = dlg.FolderName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultSettings = new AppSettings
        {
            DefaultSavePath = SavePathBox.Text.Trim(),
            Theme = ThemeCombo.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark,
            Language = LanguageCombo.SelectedValue is AppLanguage lang ? lang : AppLanguage.English,
            CompressOnSave = CompressOnSaveCheck.IsChecked == true
        };
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(new AppSettings
        {
            DefaultSavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PDFMaster")
        });
    }

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);
}
