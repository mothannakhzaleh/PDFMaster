using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PDFMaster.Services;

public sealed record AddTextPromptResult(
    string Text,
    string ColorHex,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic,
    double RotationDegrees);

public static class AddTextPrompt
{
    private static readonly (string Name, string Hex)[] TextColors =
    [
        ("Black", "111827"),
        ("Blue", "2563EB"),
        ("Red", "E11D48"),
        ("Green", "16A34A"),
        ("Orange", "F59E0B"),
        ("Purple", "7C3AED")
    ];

    private static readonly double[] RotationChoices = [0, 45, 90, 135, 180, 270];

    public static AddTextPromptResult? Show(
        Window owner,
        LocalizationService loc,
        string defaultText = "",
        string defaultColorHex = "111827",
        double defaultFontSize = 14,
        string defaultFontFamily = "Segoe UI",
        bool defaultBold = false,
        bool defaultItalic = false,
        double defaultRotationDegrees = 0)
    {
        var window = new Window
        {
            Title = loc.Get("EditorToolText"),
            Width = 500,
            MinWidth = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, owner.FlowDirection);

        var input = new TextBox { Text = defaultText, MinHeight = 36 };

        var fontCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var family in EditorFontChoices.Families)
            fontCombo.Items.Add(family);
        fontCombo.SelectedItem = EditorFontChoices.Families.Contains(defaultFontFamily)
            ? defaultFontFamily
            : EditorFontChoices.Families[0];

        var sizeCombo = new ComboBox { MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var size in EditorFontChoices.Sizes)
            sizeCombo.Items.Add(size);
        sizeCombo.SelectedItem = EditorFontChoices.Sizes.Contains((int)defaultFontSize)
            ? (int)defaultFontSize
            : 14;

        var boldToggle = CreateStyleToggle("B", FontWeights.Bold, FontStyles.Normal, defaultBold);
        var italicToggle = CreateStyleToggle("I", FontWeights.Normal, FontStyles.Italic, defaultItalic);

        var rotationCombo = new ComboBox { MinWidth = 96 };
        foreach (var degrees in RotationChoices)
            rotationCombo.Items.Add(new ComboBoxItem { Content = $"{degrees:0}°", Tag = degrees });
        rotationCombo.SelectedItem = rotationCombo.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is double value && Math.Abs(value - defaultRotationDegrees) < 0.01)
            ?? rotationCombo.Items[0];

        var selectedColor = defaultColorHex.Trim().TrimStart('#').ToUpperInvariant();
        if (TextColors.All(c => !string.Equals(c.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)))
            selectedColor = TextColors[0].Hex;

        var colorButtons = new List<RadioButton>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (name, hex) in TextColors)
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
                GroupName = "AddTextColor",
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
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 56,
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush")
        };
        var previewText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(defaultText) ? loc.Get("EditorAddTextPreviewDefault") : defaultText,
            TextWrapping = TextWrapping.Wrap
        };
        previewBorder.Child = previewText;

        void UpdatePreview()
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var color = ParseHex(hex);
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int s ? s : 14;
            previewText.Foreground = new SolidColorBrush(color);
            previewText.FontFamily = new FontFamily(family);
            previewText.FontSize = size;
            previewText.FontWeight = boldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            previewText.FontStyle = italicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            previewText.Text = string.IsNullOrWhiteSpace(input.Text)
                ? loc.Get("EditorAddTextPreviewDefault")
                : input.Text.Trim();

            if (rotationCombo.SelectedItem is ComboBoxItem rotationItem &&
                rotationItem.Tag is double rotation &&
                Math.Abs(rotation) > 0.01)
            {
                previewText.RenderTransformOrigin = new Point(0.5, 0.5);
                previewText.RenderTransform = new RotateTransform(rotation);
            }
            else
            {
                previewText.RenderTransform = null;
            }
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
        rotationCombo.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var radio in colorButtons)
            radio.Checked += (_, _) => UpdatePreview();
        UpdatePreview();

        AddTextPromptResult? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(loc.Get("Ok"), (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return;

            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int fontSize ? fontSize : 14;
            var rotation = rotationCombo.SelectedItem is ComboBoxItem item && item.Tag is double degrees ? degrees : 0;
            result = new AddTextPromptResult(
                input.Text.Trim(),
                hex,
                size,
                family,
                boldToggle.IsChecked == true,
                italicToggle.IsChecked == true,
                rotation);
            window.DialogResult = true;
        });
        var cancel = ThemeWindowHelper.CreateSecondaryButton(loc.Get("Cancel"), true, (_, _) => window.DialogResult = false);

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        fontRow.Children.Add(fontCombo);
        fontRow.Children.Add(sizeCombo);
        fontRow.Children.Add(boldToggle);
        fontRow.Children.Add(italicToggle);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel }
        };
        ok.Margin = new Thickness(0, 0, 8, 0);

        var grid = new Grid { Margin = new Thickness(20) };
        var rows = new UIElement[]
        {
            ThemeWindowHelper.CreateMessageText(loc.Get("EditorAddTextPrompt")),
            input,
            Label(loc, "EditorStampFont"),
            fontRow,
            Label(loc, "EditorBlockColor"),
            colorPanel,
            customColorBtn,
            Label(loc, "EditorFormRotation"),
            rotationCombo,
            Label(loc, "EditorFormPreview"),
            previewBorder,
            buttons
        };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }

        window.Content = new Border
        {
            Background = ThemeWindowHelper.GetBrush("BackgroundBrush"),
            Margin = new Thickness(16),
            Child = new Border
            {
                Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
                BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = grid
            }
        };

        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? result : null;
    }

    private static ToggleButton CreateStyleToggle(string content, FontWeight weight, FontStyle style, bool isChecked)
    {
        var toggle = new ToggleButton
        {
            Content = content,
            FontWeight = weight,
            FontStyle = style,
            Width = 36,
            Height = 32,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 8, 0)
        };
        if (Application.Current.TryFindResource("SecondaryToggleButton") is Style toggleStyle)
            toggle.Style = toggleStyle;
        return toggle;
    }

    private static TextBlock Label(LocalizationService loc, string key) => new()
    {
        Text = loc.Get(key),
        Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return Colors.Black;

        return Color.FromRgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber),
            byte.Parse(hex[2..4], NumberStyles.HexNumber),
            byte.Parse(hex[4..6], NumberStyles.HexNumber));
    }
}
