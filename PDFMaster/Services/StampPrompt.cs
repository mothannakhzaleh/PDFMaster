using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PDFMaster.Services;

public sealed record StampPromptResult(
    string Text,
    string ColorHex,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic);

public static class StampPrompt
{
    private static readonly (string Name, string Hex)[] StampColors =
    [
        ("Red", "E11D48"),
        ("Green", "16A34A"),
        ("Blue", "2563EB"),
        ("Orange", "F59E0B"),
        ("Purple", "7C3AED"),
        ("Black", "111827")
    ];

    public static StampPromptResult? Show(
        Window owner,
        LocalizationService loc,
        string defaultText = "APPROVED",
        string defaultColorHex = "E11D48",
        double defaultFontSize = 18,
        string defaultFontFamily = "Segoe UI",
        bool defaultBold = true,
        bool defaultItalic = false)
    {
        var flow = owner.FlowDirection;
        var window = new Window
        {
            Title = loc.Get("EditorToolStamp"),
            Width = 480,
            MinWidth = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, flow);

        var input = new TextBox { Text = defaultText, MinHeight = 36 };

        var fontCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var family in EditorFontChoices.Families)
            fontCombo.Items.Add(family);
        fontCombo.SelectedItem = EditorFontChoices.Families.Contains(defaultFontFamily)
            ? defaultFontFamily
            : EditorFontChoices.Families[0];

        var sizeCombo = new ComboBox { MinWidth = 72 };
        foreach (var size in EditorFontChoices.Sizes)
            sizeCombo.Items.Add(size);
        sizeCombo.SelectedItem = EditorFontChoices.Sizes.Contains((int)defaultFontSize)
            ? (int)defaultFontSize
            : 18;

        var boldToggle = new ToggleButton
        {
            Content = "B",
            FontWeight = FontWeights.Bold,
            Width = 36,
            Height = 32,
            IsChecked = defaultBold,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var italicToggle = new ToggleButton
        {
            Content = "I",
            FontStyle = FontStyles.Italic,
            Width = 36,
            Height = 32,
            IsChecked = defaultItalic
        };
        if (Application.Current.TryFindResource("SecondaryToggleButton") is Style toggleStyle)
        {
            boldToggle.Style = toggleStyle;
            italicToggle.Style = toggleStyle;
        }

        var selectedColor = defaultColorHex.Trim().TrimStart('#').ToUpperInvariant();
        if (StampColors.All(c => !string.Equals(c.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)))
            selectedColor = StampColors[0].Hex;

        var colorButtons = new List<RadioButton>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (name, hex) in StampColors)
        {
            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(ParseHex(hex)),
                BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 8)
            };
            var radio = new RadioButton
            {
                GroupName = "StampColor",
                IsChecked = string.Equals(hex, selectedColor, StringComparison.OrdinalIgnoreCase),
                Content = swatch,
                ToolTip = name,
                Tag = hex
            };
            colorButtons.Add(radio);
            colorPanel.Children.Add(radio);
        }

        var previewBorder = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 56
        };
        var previewText = new TextBlock
        {
            Text = defaultText,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        previewBorder.Child = previewText;

        void UpdatePreview()
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var color = ParseHex(hex);
            var family = fontCombo.SelectedItem as string ?? "Segoe UI";
            var size = sizeCombo.SelectedItem is int s ? s : 18;
            previewBorder.BorderBrush = new SolidColorBrush(color);
            previewBorder.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
            previewText.Foreground = new SolidColorBrush(color);
            previewText.Text = string.IsNullOrWhiteSpace(input.Text) ? "APPROVED" : input.Text.Trim();
            previewText.FontFamily = new FontFamily(family);
            previewText.FontSize = size;
            previewText.FontWeight = boldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            previewText.FontStyle = italicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
        }

        var customColorBtn = ThemeWindowHelper.CreateSecondaryButton(loc.Get("EditorPickColor"), false, (_, _) =>
        {
            var picked = ColorPickerDialog.Pick(window, ParseHex(selectedColor), loc);
            if (picked is null)
                return;

            selectedColor = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
            foreach (var radio in colorButtons)
                radio.IsChecked = false;
            UpdatePreview();
        });
        customColorBtn.Margin = new Thickness(0, 0, 0, 8);

        input.TextChanged += (_, _) => UpdatePreview();
        fontCombo.SelectionChanged += (_, _) => UpdatePreview();
        sizeCombo.SelectionChanged += (_, _) => UpdatePreview();
        boldToggle.Click += (_, _) => UpdatePreview();
        italicToggle.Click += (_, _) => UpdatePreview();
        foreach (var radio in colorButtons)
            radio.Checked += (_, _) => UpdatePreview();
        UpdatePreview();

        StampPromptResult? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(loc.Get("Ok"), (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return;

            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var family = fontCombo.SelectedItem as string ?? "Segoe UI";
            var size = sizeCombo.SelectedItem is int s ? s : 18;
            result = new StampPromptResult(
                input.Text.Trim(),
                hex,
                size,
                family,
                boldToggle.IsChecked == true,
                italicToggle.IsChecked == true);
            window.DialogResult = true;
        });

        var cancel = ThemeWindowHelper.CreateSecondaryButton(loc.Get("Cancel"), isCancel: true,
            (_, _) => window.DialogResult = false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel }
        };
        ok.Margin = new Thickness(0, 0, 8, 0);

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        fontRow.Children.Add(fontCombo);
        fontRow.Children.Add(sizeCombo);
        fontRow.Children.Add(boldToggle);
        fontRow.Children.Add(italicToggle);

        var grid = new Grid { Margin = new Thickness(20) };
        for (var i = 0; i < 9; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var message = ThemeWindowHelper.CreateMessageText(loc.Get("EditorStampPrompt"));
        var fontLabel = new TextBlock
        {
            Text = loc.Get("EditorStampFont"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var colorLabel = new TextBlock
        {
            Text = loc.Get("EditorStampColor"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var previewLabel = new TextBlock
        {
            Text = loc.Get("EditorStampPreview"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };

        Grid.SetRow(message, 0);
        Grid.SetRow(input, 1);
        Grid.SetRow(fontLabel, 2);
        Grid.SetRow(fontRow, 3);
        Grid.SetRow(colorLabel, 4);
        Grid.SetRow(colorPanel, 5);
        Grid.SetRow(customColorBtn, 6);
        Grid.SetRow(previewLabel, 7);
        Grid.SetRow(previewBorder, 8);
        Grid.SetRow(buttons, 9);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(buttons, 9);

        grid.Children.Add(message);
        grid.Children.Add(input);
        grid.Children.Add(fontLabel);
        grid.Children.Add(fontRow);
        grid.Children.Add(colorLabel);
        grid.Children.Add(colorPanel);
        grid.Children.Add(customColorBtn);
        grid.Children.Add(previewLabel);
        grid.Children.Add(previewBorder);
        grid.Children.Add(buttons);

        window.Content = new Grid
        {
            Background = ThemeWindowHelper.GetBrush("BackgroundBrush"),
            Margin = new Thickness(16),
            Children =
            {
                new Border
                {
                    Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
                    BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Child = grid
                }
            }
        };

        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? result : null;
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return Colors.Red;

        return Color.FromRgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}
