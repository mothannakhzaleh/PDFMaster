using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PDFMaster.Services;

public sealed record SignaturePromptResult(string ImagePath);

public static class SignaturePrompt
{
    private static readonly (string Name, string Hex)[] SignatureColors =
    [
        ("Black", "111827"),
        ("Blue", "2563EB"),
        ("Red", "E11D48")
    ];

    public static SignaturePromptResult? Show(Window owner, LocalizationService loc)
    {
        var flow = owner.FlowDirection;
        var window = new Window
        {
            Title = loc.Get("EditorSignatureTitle"),
            Width = 540,
            MinWidth = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, flow);

        var drawInk = new InkCanvas
        {
            Height = 160,
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            EditingMode = InkCanvasEditingMode.Ink
        };
        drawInk.DefaultDrawingAttributes.Color = Colors.Black;
        drawInk.DefaultDrawingAttributes.Width = 2.5;
        drawInk.DefaultDrawingAttributes.Height = 2.5;

        var typeInput = new TextBox
        {
            Text = loc.Get("EditorSignatureDefaultName"),
            MinHeight = 36
        };

        var fontCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var family in EditorFontChoices.Families)
            fontCombo.Items.Add(family);
        fontCombo.SelectedItem = "Segoe Script";

        var sizeCombo = new ComboBox { MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var size in EditorFontChoices.Sizes)
            sizeCombo.Items.Add(size);
        sizeCombo.SelectedItem = 42;

        var boldToggle = new ToggleButton
        {
            Content = "B",
            FontWeight = FontWeights.Bold,
            Width = 36,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var italicToggle = new ToggleButton
        {
            Content = "I",
            FontStyle = FontStyles.Italic,
            Width = 36,
            Height = 32
        };
        if (Application.Current.TryFindResource("SecondaryToggleButton") is Style toggleStyle)
        {
            boldToggle.Style = toggleStyle;
            italicToggle.Style = toggleStyle;
        }

        var typePreview = new TextBlock
        {
            Text = typeInput.Text,
            FontFamily = new FontFamily("Segoe Script"),
            FontSize = 42,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        var typePreviewBorder = new Border
        {
            Height = 160,
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            Child = typePreview
        };

        var uploadPathBox = new TextBox { IsReadOnly = true, MinHeight = 36 };
        var uploadPreview = new Image
        {
            Height = 160,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var uploadPreviewBorder = new Border
        {
            Height = 160,
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = ThemeWindowHelper.GetBrush("SurfaceElevatedBrush"),
            Child = uploadPreview
        };

        var colorButtons = new List<RadioButton>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (name, hex) in SignatureColors)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(ParseHex(hex)),
                BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var radio = new RadioButton
            {
                GroupName = "SignatureColor",
                IsChecked = hex == "111827",
                Content = swatch,
                ToolTip = name,
                Tag = hex
            };
            colorButtons.Add(radio);
            colorPanel.Children.Add(radio);
        }

        var customColorBtn = ThemeWindowHelper.CreateSecondaryButton(loc.Get("EditorPickColor"), false, (_, _) =>
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "111827";
            var picked = ColorPickerDialog.Pick(window, ParseHex(hex), loc);
            if (picked is null)
                return;

            var newHex = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
            foreach (var radio in colorButtons)
                radio.IsChecked = string.Equals(radio.Tag as string, newHex, StringComparison.OrdinalIgnoreCase);
            UpdateTypePreview();
        });
        customColorBtn.Margin = new Thickness(0, 8, 0, 0);

        void UpdateTypePreview()
        {
            var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "111827";
            var family = fontCombo.SelectedItem as string ?? "Segoe Script";
            var size = sizeCombo.SelectedItem is int s ? s : 42;
            typePreview.Text = string.IsNullOrWhiteSpace(typeInput.Text)
                ? loc.Get("EditorSignatureDefaultName")
                : typeInput.Text.Trim();
            typePreview.Foreground = new SolidColorBrush(ParseHex(hex));
            typePreview.FontFamily = new FontFamily(family);
            typePreview.FontSize = size;
            typePreview.FontWeight = boldToggle.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
            typePreview.FontStyle = italicToggle.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
        }

        typeInput.TextChanged += (_, _) => UpdateTypePreview();
        fontCombo.SelectionChanged += (_, _) => UpdateTypePreview();
        sizeCombo.SelectionChanged += (_, _) => UpdateTypePreview();
        boldToggle.Click += (_, _) => UpdateTypePreview();
        italicToggle.Click += (_, _) => UpdateTypePreview();
        foreach (var radio in colorButtons)
            radio.Checked += (_, _) => UpdateTypePreview();
        UpdateTypePreview();

        var uploadBrowse = ThemeWindowHelper.CreateSecondaryButton(loc.Get("EditorSignatureBrowse"), false, (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                Filter = loc.Get("EditorImageFilter"),
                Title = loc.Get("EditorSignatureUploadTab")
            };
            if (dlg.ShowDialog() != true)
                return;

            uploadPathBox.Text = dlg.FileName;
            uploadPreview.Source = new BitmapImage(new Uri(dlg.FileName));
        });

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        fontRow.Children.Add(fontCombo);
        fontRow.Children.Add(sizeCombo);
        fontRow.Children.Add(boldToggle);
        fontRow.Children.Add(italicToggle);

        var drawPanel = new StackPanel();
        drawPanel.Children.Add(new TextBlock
        {
            Text = loc.Get("EditorSignatureDrawHint"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        drawPanel.Children.Add(drawInk);
        drawPanel.Children.Add(ThemeWindowHelper.CreateSecondaryButton(loc.Get("EditorSignatureClear"), false, (_, _) => drawInk.Strokes.Clear()));

        var typePanel = new StackPanel();
        typePanel.Children.Add(new TextBlock
        {
            Text = loc.Get("EditorSignatureTypeHint"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        typePanel.Children.Add(typeInput);
        typePanel.Children.Add(new TextBlock
        {
            Text = loc.Get("EditorStampFont"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        typePanel.Children.Add(fontRow);
        typePanel.Children.Add(colorPanel);
        typePanel.Children.Add(customColorBtn);
        typePanel.Children.Add(typePreviewBorder);

        var uploadPanel = new StackPanel();
        uploadPanel.Children.Add(new TextBlock
        {
            Text = loc.Get("EditorSignatureUploadHint"),
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        uploadPanel.Children.Add(uploadBrowse);
        uploadPanel.Children.Add(uploadPathBox);
        uploadPanel.Children.Add(uploadPreviewBorder);

        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 8) };
        tabs.Items.Add(new TabItem { Header = loc.Get("EditorSignatureDrawTab"), Content = drawPanel });
        tabs.Items.Add(new TabItem { Header = loc.Get("EditorSignatureTypeTab"), Content = typePanel });
        tabs.Items.Add(new TabItem { Header = loc.Get("EditorSignatureUploadTab"), Content = uploadPanel });

        SignaturePromptResult? result = null;
        var ok = ThemeWindowHelper.CreatePrimaryButton(loc.Get("Create"), (_, _) =>
        {
            byte[]? bytes = null;
            if (tabs.SelectedIndex == 0)
            {
                if (drawInk.Strokes.Count == 0)
                    return;
                bytes = RenderInkToPng(drawInk);
            }
            else if (tabs.SelectedIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(typeInput.Text))
                    return;

                var hex = colorButtons.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "111827";
                var family = fontCombo.SelectedItem as string ?? "Segoe Script";
                var size = sizeCombo.SelectedItem is int s ? s : 42;
                bytes = RenderTextToPng(
                    typeInput.Text.Trim(),
                    ParseHex(hex),
                    family,
                    size,
                    boldToggle.IsChecked == true,
                    italicToggle.IsChecked == true);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(uploadPathBox.Text) || !File.Exists(uploadPathBox.Text))
                    return;

                result = new SignaturePromptResult(uploadPathBox.Text);
                window.DialogResult = true;
                return;
            }

            if (bytes is null || bytes.Length == 0)
                return;

            var path = Path.Combine(Path.GetTempPath(), $"pdfmaster_sig_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(path, bytes);
            result = new SignaturePromptResult(path);
            window.DialogResult = true;
        });

        var cancel = ThemeWindowHelper.CreateSecondaryButton(loc.Get("Cancel"), isCancel: true,
            (_, _) => window.DialogResult = false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel }
        };
        ok.Margin = new Thickness(0, 0, 8, 0);

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(tabs, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(tabs);
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

        return window.ShowDialog() == true ? result : null;
    }

    private static byte[] RenderInkToPng(InkCanvas canvas)
    {
        var width = (int)Math.Max(320, canvas.ActualWidth > 0 ? canvas.ActualWidth : 420);
        var height = (int)Math.Max(120, canvas.ActualHeight > 0 ? canvas.ActualHeight : 160);
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var clone = new InkCanvas { Width = width, Height = height, Background = Brushes.Transparent };
        foreach (var stroke in canvas.Strokes)
            clone.Strokes.Add(stroke.Clone());
        clone.Measure(new Size(width, height));
        clone.Arrange(new Rect(0, 0, width, height));
        rtb.Render(clone);
        return EncodePng(rtb);
    }

    private static byte[] RenderTextToPng(string text, Color color, string fontFamily, double fontSize, bool bold, bool italic)
    {
        const int width = 420;
        const int height = 160;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            var weight = bold ? FontWeights.Bold : FontWeights.Normal;
            var style = italic ? FontStyles.Italic : FontStyles.Normal;
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(fontFamily), style, weight, FontStretches.Normal),
                fontSize,
                new SolidColorBrush(color),
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            var x = Math.Max(8, (width - formatted.Width) / 2);
            var y = Math.Max(8, (height - formatted.Height) / 2);
            dc.DrawText(formatted, new Point(x, y));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return EncodePng(rtb);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return Colors.Black;

        return Color.FromRgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }
}
