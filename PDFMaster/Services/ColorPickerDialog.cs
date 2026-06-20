using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PDFMaster.Services;

public static class ColorPickerDialog
{
    public static Color? Pick(Window owner, Color initial, LocalizationService? loc = null)
    {
        var window = new Window
        {
            Title = loc?.Get("EditorPickColor") ?? "Pick color",
            Width = 360,
            MinWidth = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ThemeWindowHelper.Apply(window, owner.FlowDirection);

        RgbToHsv(initial, out var hue, out var saturation, out var value);

        var svCanvas = new Canvas
        {
            Width = 260,
            Height = 160,
            Margin = new Thickness(0, 0, 12, 0),
            Cursor = Cursors.Cross,
            Background = Brushes.Transparent
        };

        var hueLayer = new Rectangle { Width = 260, Height = 160, IsHitTestVisible = false };
        var valueLayer = new Rectangle { Width = 260, Height = 160, IsHitTestVisible = false };
        svCanvas.Children.Add(hueLayer);
        svCanvas.Children.Add(valueLayer);

        var svMarker = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        svCanvas.Children.Add(svMarker);

        var hueSlider = new Slider
        {
            Minimum = 0,
            Maximum = 359,
            Value = hue,
            Width = 260,
            Margin = new Thickness(0, 12, 12, 0)
        };

        var preview = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(initial)
        };

        var hexBox = CreateField(loc?.Get("EditorColorHex") ?? "Hex", ToHex(initial));
        var rBox = CreateField("R", initial.R.ToString(CultureInfo.InvariantCulture));
        var gBox = CreateField("G", initial.G.ToString(CultureInfo.InvariantCulture));
        var bBox = CreateField("B", initial.B.ToString(CultureInfo.InvariantCulture));

        Color? result = null;
        var suppress = false;

        void UpdateSvBackground()
        {
            var hueColor = HsvToRgb(hueSlider.Value, 1, 1);
            hueLayer.Fill = new LinearGradientBrush(Colors.White, hueColor, new Point(0, 0), new Point(1, 0));
            valueLayer.Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, new Point(0, 0), new Point(0, 1));
        }

        void UpdateSvMarker()
        {
            Canvas.SetLeft(svMarker, saturation * svCanvas.Width - 6);
            Canvas.SetTop(svMarker, (1 - value) * svCanvas.Height - 6);
        }

        void UpdateFromHsv()
        {
            suppress = true;
            var rgb = HsvToRgb(hueSlider.Value, saturation, value);
            preview.Background = new SolidColorBrush(rgb);
            hexBox.TextBox.Text = ToHex(rgb);
            rBox.TextBox.Text = rgb.R.ToString(CultureInfo.InvariantCulture);
            gBox.TextBox.Text = rgb.G.ToString(CultureInfo.InvariantCulture);
            bBox.TextBox.Text = rgb.B.ToString(CultureInfo.InvariantCulture);
            UpdateSvBackground();
            UpdateSvMarker();
            suppress = false;
        }

        void SetFromColor(Color color)
        {
            RgbToHsv(color, out var h, out saturation, out value);
            hueSlider.Value = h;
            UpdateFromHsv();
        }

        void UpdateFromRgbBoxes()
        {
            if (suppress)
                return;

            if (!byte.TryParse(rBox.TextBox.Text, out var r) ||
                !byte.TryParse(gBox.TextBox.Text, out var g) ||
                !byte.TryParse(bBox.TextBox.Text, out var b))
                return;

            SetFromColor(Color.FromRgb(r, g, b));
        }

        void UpdateFromHexBox()
        {
            if (suppress)
                return;

            var hex = hexBox.TextBox.Text.Trim().TrimStart('#');
            if (hex.Length != 6)
                return;

            try
            {
                SetFromColor(Color.FromRgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16)));
            }
            catch
            {
                // ignore invalid hex while typing
            }
        }

        svCanvas.PreviewMouseLeftButtonDown += (_, e) =>
        {
            svCanvas.CaptureMouse();
            e.Handled = true;
            UpdateSvFromPoint(e.GetPosition(svCanvas));
        };
        svCanvas.PreviewMouseMove += (_, e) =>
        {
            if (!svCanvas.IsMouseCaptured)
                return;

            UpdateSvFromPoint(e.GetPosition(svCanvas));
        };
        svCanvas.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (svCanvas.IsMouseCaptured)
                svCanvas.ReleaseMouseCapture();
        };

        void UpdateSvFromPoint(Point point)
        {
            saturation = Math.Clamp(point.X / svCanvas.Width, 0, 1);
            value = Math.Clamp(1 - point.Y / svCanvas.Height, 0, 1);
            UpdateFromHsv();
        }

        hueSlider.ValueChanged += (_, _) => UpdateFromHsv();
        hexBox.TextBox.TextChanged += (_, _) => UpdateFromHexBox();
        rBox.TextBox.TextChanged += (_, _) => UpdateFromRgbBoxes();
        gBox.TextBox.TextChanged += (_, _) => UpdateFromRgbBoxes();
        bBox.TextBox.TextChanged += (_, _) => UpdateFromRgbBoxes();

        UpdateFromHsv();

        var ok = ThemeWindowHelper.CreatePrimaryButton(loc?.Get("Ok") ?? "OK", (_, _) =>
        {
            if (preview.Background is SolidColorBrush brush)
                result = brush.Color;
            window.DialogResult = true;
        });
        var cancel = ThemeWindowHelper.CreateSecondaryButton(loc?.Get("Cancel") ?? "Cancel", isCancel: true,
            (_, _) => window.DialogResult = false);

        var rgbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        rgbRow.Children.Add(hexBox.Root);
        rgbRow.Children.Add(rBox.Root);
        rgbRow.Children.Add(gBox.Root);
        rgbRow.Children.Add(bBox.Root);

        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        topRow.Children.Add(svCanvas);
        topRow.Children.Add(preview);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(topRow);
        panel.Children.Add(hueSlider);
        panel.Children.Add(rgbRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel }
        };
        ok.Margin = new Thickness(0, 0, 8, 0);
        panel.Children.Add(buttons);

        window.Content = new Border
        {
            Background = ThemeWindowHelper.GetBrush("SurfaceBrush"),
            BorderBrush = ThemeWindowHelper.GetBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(16),
            Child = panel
        };

        return window.ShowDialog() == true ? result : null;
    }

    private static (StackPanel Root, TextBox TextBox) CreateField(string label, string value)
    {
        var box = new TextBox
        {
            Text = value,
            MinWidth = label is "Hex" or "HEX" ? 88 : 52,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var root = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        root.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ThemeWindowHelper.GetBrush("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(box);
        return (root, box);
    }

    private static string ToHex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        v = max;
        var delta = max - min;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            h = 0;
            return;
        }

        if (max == r)
            h = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            h = 60 * (((b - r) / delta) + 2);
        else
            h = 60 * (((r - g) / delta) + 4);

        if (h < 0)
            h += 360;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);

        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
