using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDFMaster.Models;

public sealed class PdfMergeItem : INotifyPropertyChanged
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public int PageCount { get; init; }
    public int Order { get; set; }

    public string DisplayText => $"{Order}. {FileName} ({PageCount})";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(Order));
        OnPropertyChanged(nameof(DisplayText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
