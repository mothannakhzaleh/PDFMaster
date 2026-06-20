using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PDFMaster.Services;

public static class InputPrompt
{
    public static string? Show(Window owner, string message, string title, string defaultValue = "",
        string okText = "OK", string cancelText = "Cancel")
    {
        var flow = owner.FlowDirection;
        var window = new Window
        {
            Title = title,
            Width = 500,
            MinWidth = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, flow);

        var input = new TextBox
        {
            Text = defaultValue,
            MinHeight = 36,
            MaxHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = false
        };

        string? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(okText, (_, _) =>
        {
            result = input.Text;
            window.DialogResult = true;
        });

        var cancel = ThemeWindowHelper.CreateSecondaryButton(cancelText, isCancel: true,
            (_, _) => window.DialogResult = false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel }
        };
        ok.Margin = new Thickness(0, 0, 8, 0);

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var messageBlock = ThemeWindowHelper.CreateMessageText(message);
        Grid.SetRow(messageBlock, 0);
        Grid.SetRow(input, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(messageBlock);
        grid.Children.Add(input);
        grid.Children.Add(buttons);

        var border = new Border
        {
            Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = grid
        };

        window.Content = new Grid
        {
            Background = ThemeWindowHelper.GetBrush("BackgroundBrush"),
            Margin = new Thickness(16),
            Children = { border }
        };

        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? result : null;
    }
}
