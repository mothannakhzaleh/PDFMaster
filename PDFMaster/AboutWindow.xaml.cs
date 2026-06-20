using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using PDFMaster.Services;

namespace PDFMaster;

public partial class AboutWindow : Window
{
    public AboutWindow(LocalizationService loc)
    {
        InitializeComponent();

        Title = loc.Get("About");
        TitleText.Text = loc.Get("AppTitle");
        VersionText.Text = $"{loc.Get("AboutVersion")} {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.0"}";
        DescriptionText.Text = loc.Get("AboutDescription");
        FeaturesText.Text = loc.Get("AboutFeatures");
        AuthorPrefixRun.Text = loc.Get("AboutAuthorPrefix");
        CloseButton.Content = loc.Get("AboutClose");
        FlowDirection = loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
