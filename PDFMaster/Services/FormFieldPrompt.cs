using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PDFMaster.Services;

public sealed record TextFormFieldPromptResult(
    string Name,
    string DefaultValue,
    string ColorHex,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic,
    int RotationDegrees);

public sealed record ComboFormFieldPromptResult(
    string Name,
    IReadOnlyList<string> Options,
    string DefaultValue,
    string ColorHex,
    double FontSize,
    string FontFamily,
    bool Bold,
    bool Italic,
    int RotationDegrees);

public static class FormFieldPrompt
{
    private static readonly (string Name, string Hex)[] FieldColors =
    [
        ("Black", "111827"),
        ("Blue", "2563EB"),
        ("Red", "E11D48"),
        ("Green", "16A34A"),
        ("Orange", "F59E0B"),
        ("Purple", "7C3AED")
    ];

    private static readonly int[] RotationChoices = [0, 90, 180, 270];

    public static TextFormFieldPromptResult? ShowTextField(
        Window owner,
        LocalizationService loc,
        string defaultName = "TextFormField1",
        TextFormFieldPromptResult? existing = null,
        bool lockName = false)
    {
        var window = CreateWindow(owner,
            existing is null ? loc.Get("EditorAddTextField") : loc.Get("EditorEditFormField"),
            500);
        var nameBox = new TextBox
        {
            Text = existing?.Name ?? defaultName,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 0, 8),
            IsReadOnly = lockName
        };
        var valueBox = new TextBox { Text = existing?.DefaultValue ?? string.Empty, MinHeight = 32, Margin = new Thickness(0, 0, 0, 8) };

        var fontCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var family in EditorFontChoices.Families)
            fontCombo.Items.Add(family);
        fontCombo.SelectedItem = EditorFontChoices.Families.Contains(existing?.FontFamily ?? string.Empty)
            ? existing!.FontFamily
            : EditorFontChoices.Families[0];

        var sizeCombo = new ComboBox { MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var size in EditorFontChoices.Sizes)
            sizeCombo.Items.Add(size);
        var defaultSize = (int)(existing?.FontSize ?? 12);
        sizeCombo.SelectedItem = EditorFontChoices.Sizes.Contains(defaultSize)
            ? defaultSize
            : 12;

        var boldToggle = CreateStyleToggle("B", FontWeights.Bold, FontStyles.Normal, existing?.Bold == true);
        var italicToggle = CreateStyleToggle("I", FontWeights.Normal, FontStyles.Italic, existing?.Italic == true);

        var rotationCombo = new ComboBox { MinWidth = 96 };
        foreach (var degrees in RotationChoices)
            rotationCombo.Items.Add(new ComboBoxItem { Content = $"{degrees}°", Tag = degrees });
        rotationCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RotationChoices, existing?.RotationDegrees ?? 0));

        var selectedColor = existing?.ColorHex ?? FieldColors[0].Hex;
        var colorButtons = new List<RadioButton>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (name, hex) in FieldColors)
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
                GroupName = "FormFieldColor",
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
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 52,
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush")
        };
        var previewText = new TextBlock
        {
            Text = loc.Get("EditorFormDefaultValue"),
            TextWrapping = TextWrapping.Wrap
        };
        previewBorder.Child = previewText;

        void UpdatePreview()
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var color = ParseHex(hex);
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int s ? s : 12;
            previewText.Foreground = new SolidColorBrush(color);
            previewText.FontFamily = new FontFamily(family);
            previewText.FontSize = size;
            previewText.FontWeight = boldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            previewText.FontStyle = italicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            previewText.Text = string.IsNullOrWhiteSpace(valueBox.Text)
                ? loc.Get("EditorFormDefaultValue")
                : valueBox.Text;
            if (rotationCombo.SelectedItem is ComboBoxItem rotationItem &&
                rotationItem.Tag is int rotation &&
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

        valueBox.TextChanged += (_, _) => UpdatePreview();
        fontCombo.SelectionChanged += (_, _) => UpdatePreview();
        sizeCombo.SelectionChanged += (_, _) => UpdatePreview();
        boldToggle.Click += (_, _) => UpdatePreview();
        italicToggle.Click += (_, _) => UpdatePreview();
        rotationCombo.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var radio in colorButtons)
            radio.Checked += (_, _) => UpdatePreview();
        UpdatePreview();

        TextFormFieldPromptResult? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(loc.Get("Ok"), (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
                return;

            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int fontSize ? fontSize : 12;
            var rotation = rotationCombo.SelectedItem is ComboBoxItem item && item.Tag is int degrees ? degrees : 0;
            result = new TextFormFieldPromptResult(
                nameBox.Text.Trim(),
                valueBox.Text,
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

        var grid = BuildGrid(
            ThemeWindowHelper.CreateMessageText(loc.Get("EditorTextFieldPrompt")),
            Label(loc, "EditorFormFieldName"),
            nameBox,
            Label(loc, "EditorFormDefaultValue"),
            valueBox,
            Label(loc, "EditorFormFontFamily"),
            fontRow,
            Label(loc, "EditorBlockColor"),
            colorPanel,
            customColorBtn,
            Label(loc, "EditorFormRotation"),
            rotationCombo,
            Label(loc, "EditorFormPreview"),
            previewBorder,
            Buttons(ok, cancel));
        window.Content = Wrap(grid);
        return window.ShowDialog() == true ? result : null;
    }

    public static ComboFormFieldPromptResult? ShowComboField(
        Window owner,
        LocalizationService loc,
        string defaultName = "ComboFormField1",
        ComboFormFieldPromptResult? existing = null,
        bool lockName = false)
    {
        var window = CreateWindow(owner,
            existing is null ? loc.Get("EditorAddComboField") : loc.Get("EditorEditFormField"),
            500);
        var nameBox = new TextBox
        {
            Text = existing?.Name ?? defaultName,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 0, 8),
            IsReadOnly = lockName
        };
        var optionsBox = new TextBox
        {
            Text = existing?.Options.Count > 0
                ? string.Join(Environment.NewLine, existing.Options)
                : "Option 1\nOption 2\nOption 3",
            MinHeight = 96,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var valueBox = new TextBox
        {
            Text = existing?.DefaultValue ?? "Option 1",
            MinHeight = 32,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var fontCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var family in EditorFontChoices.Families)
            fontCombo.Items.Add(family);
        var defaultFamily = existing?.FontFamily ?? string.Empty;
        fontCombo.SelectedItem = EditorFontChoices.Families.Contains(defaultFamily)
            ? defaultFamily
            : EditorFontChoices.Families[0];

        var sizeCombo = new ComboBox { MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var size in EditorFontChoices.Sizes)
            sizeCombo.Items.Add(size);
        var defaultSize = (int)(existing?.FontSize ?? 12);
        sizeCombo.SelectedItem = EditorFontChoices.Sizes.Contains(defaultSize)
            ? defaultSize
            : 12;

        var boldToggle = CreateStyleToggle("B", FontWeights.Bold, FontStyles.Normal, existing?.Bold == true);
        var italicToggle = CreateStyleToggle("I", FontWeights.Normal, FontStyles.Italic, existing?.Italic == true);

        var rotationCombo = new ComboBox { MinWidth = 96 };
        foreach (var degrees in RotationChoices)
            rotationCombo.Items.Add(new ComboBoxItem { Content = $"{degrees}°", Tag = degrees });
        rotationCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RotationChoices, existing?.RotationDegrees ?? 0));

        var selectedColor = existing?.ColorHex ?? FieldColors[0].Hex;
        var colorButtons = new List<RadioButton>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (name, hex) in FieldColors)
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
                GroupName = "ComboFieldColor",
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
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 52,
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush")
        };
        var previewText = new TextBlock
        {
            Text = valueBox.Text,
            TextWrapping = TextWrapping.Wrap
        };
        previewBorder.Child = previewText;

        void UpdatePreview()
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var color = ParseHex(hex);
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int s ? s : 12;
            previewText.Foreground = new SolidColorBrush(color);
            previewText.FontFamily = new FontFamily(family);
            previewText.FontSize = size;
            previewText.FontWeight = boldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            previewText.FontStyle = italicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
            previewText.Text = string.IsNullOrWhiteSpace(valueBox.Text)
                ? loc.Get("EditorFormDefaultValue")
                : valueBox.Text;
            if (rotationCombo.SelectedItem is ComboBoxItem rotationItem &&
                rotationItem.Tag is int rotation &&
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

        valueBox.TextChanged += (_, _) => UpdatePreview();
        fontCombo.SelectionChanged += (_, _) => UpdatePreview();
        sizeCombo.SelectionChanged += (_, _) => UpdatePreview();
        boldToggle.Click += (_, _) => UpdatePreview();
        italicToggle.Click += (_, _) => UpdatePreview();
        rotationCombo.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var radio in colorButtons)
            radio.Checked += (_, _) => UpdatePreview();
        UpdatePreview();

        ComboFormFieldPromptResult? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(loc.Get("Ok"), (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
                return;

            var options = optionsBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .ToList();
            if (options.Count == 0)
                return;

            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? selectedColor;
            var family = fontCombo.SelectedItem as string ?? EditorFontChoices.Families[0];
            var size = sizeCombo.SelectedItem is int fontSize ? fontSize : 12;
            var rotation = rotationCombo.SelectedItem is ComboBoxItem item && item.Tag is int degrees ? degrees : 0;
            var defaultValue = string.IsNullOrWhiteSpace(valueBox.Text) ? options[0] : valueBox.Text.Trim();
            result = new ComboFormFieldPromptResult(
                nameBox.Text.Trim(),
                options,
                defaultValue,
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

        var grid = BuildGrid(
            ThemeWindowHelper.CreateMessageText(loc.Get("EditorComboFieldPrompt")),
            Label(loc, "EditorFormFieldName"),
            nameBox,
            Label(loc, "EditorComboOptions"),
            optionsBox,
            Label(loc, "EditorFormDefaultValue"),
            valueBox,
            Label(loc, "EditorFormFontFamily"),
            fontRow,
            Label(loc, "EditorBlockColor"),
            colorPanel,
            customColorBtn,
            Label(loc, "EditorFormRotation"),
            rotationCombo,
            Label(loc, "EditorFormPreview"),
            previewBorder,
            Buttons(ok, cancel));
        window.Content = Wrap(grid);
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

    private static Window CreateWindow(Window owner, string title, double width = 420)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            MinWidth = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, owner.FlowDirection);
        return window;
    }

    private static TextBlock Label(LocalizationService loc, string key) => new()
    {
        Text = loc.Get(key),
        Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static Grid BuildGrid(params UIElement[] children)
    {
        var grid = new Grid { Margin = new Thickness(20) };
        for (var i = 0; i < children.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(children[i], i);
            grid.Children.Add(children[i]);
        }

        return grid;
    }

    private static StackPanel Buttons(Button ok, Button cancel)
    {
        ok.Margin = new Thickness(0, 16, 8, 0);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel }
        };
    }

    private static Border Wrap(UIElement child) => new()
    {
        Background = ThemeWindowHelper.GetBrush("BackgroundBrush"),
        Margin = new Thickness(16),
        Child = new Border
        {
            Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = child
        }
    };

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
            return Color.FromRgb(
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber));
        return Colors.Black;
    }
}
