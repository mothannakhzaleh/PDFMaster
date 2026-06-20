using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDFMaster.Models;

public sealed class PdfFormFieldItem : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private double _x;
    private double _y;
    private double _width;
    private double _height;

    public required string Name { get; init; }
    public required string FieldType { get; init; }
    public bool ReadOnly { get; init; }
    public int PageIndex { get; init; } = -1;

    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBounds)); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBounds)); }
    }

    public double Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBounds)); }
    }

    public double Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBounds)); }
    }

    public string ColorHex { get; set; } = "111827";
    public double FontSize { get; set; } = 12;
    public string FontFamily { get; set; } = "Segoe UI";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public int RotationDegrees { get; set; }
    public List<string> Options { get; set; } = [];

    public bool IsComboField => FieldType is "Combo" or "ComboBox";

    public bool HasBounds => PageIndex >= 0 && Width > 0 && Height > 0;

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
