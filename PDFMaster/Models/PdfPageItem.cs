using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace PDFMaster.Models;

public sealed class PdfPageItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private ImageSource? _thumbnail;

    public int PageNumber { get; init; }
    public int SourceIndex { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
