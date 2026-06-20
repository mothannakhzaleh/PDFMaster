using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFMaster.Services;

public static class ThemedMessageBox
{
    public static void Show(Window owner, string message, string title,
        string okText = "OK", MessageBoxImage icon = MessageBoxImage.None)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            MinHeight = 140,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize
        };
        ThemeWindowHelper.Apply(window, owner.FlowDirection);

        var iconText = icon switch
        {
            MessageBoxImage.Error => "✕",
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Information => "i",
            _ => string.Empty
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        if (!string.IsNullOrEmpty(iconText))
        {
            header.Children.Add(new TextBlock
            {
                Text = iconText,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = icon == MessageBoxImage.Error
                    ? ThemeWindowHelper.GetBrush("ErrorBrush")
                    : ThemeWindowHelper.GetBrush("AccentBrush"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
        }

        header.Children.Add(ThemeWindowHelper.CreateMessageText(message));

        var ok = ThemeWindowHelper.CreatePrimaryButton(okText, (_, _) => window.DialogResult = true);
        ok.HorizontalAlignment = HorizontalAlignment.Right;

        var border = new Border
        {
            Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Child = new StackPanel { Children = { header, ok } }
        };

        window.Content = new Grid
        {
            Background = ThemeWindowHelper.GetBrush("BackgroundBrush"),
            Margin = new Thickness(16),
            Children = { border }
        };

        window.ShowDialog();
    }
}
