using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFMaster.Services;

public static class ThemeWindowHelper
{
    public static void Apply(Window window, FlowDirection flowDirection = FlowDirection.LeftToRight)
    {
        window.Background = GetBrush("BackgroundBrush");
        window.Foreground = GetBrush("TextPrimaryBrush");
        window.FlowDirection = flowDirection;
    }

    public static Brush GetBrush(string key) =>
        Application.Current.TryFindResource(key) as Brush
        ?? (key.Contains("Background") ? Brushes.White : Brushes.Black);

    public static Button CreatePrimaryButton(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 90,
            IsDefault = true
        };
        if (Application.Current.TryFindResource("PrimaryButton") is Style style)
            button.Style = style;
        button.Click += click;
        return button;
    }

    public static Button CreateSecondaryButton(string text, bool isCancel, RoutedEventHandler? click = null)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 90,
            IsCancel = isCancel
        };
        if (Application.Current.TryFindResource("SecondaryButton") is Style style)
            button.Style = style;
        if (click is not null)
            button.Click += click;
        return button;
    }

    public static TextBlock CreateMessageText(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        Foreground = GetBrush("TextPrimaryBrush"),
        Margin = new Thickness(0, 0, 0, 12)
    };
}
