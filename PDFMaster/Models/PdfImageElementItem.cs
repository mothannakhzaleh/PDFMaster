namespace PDFMaster.Models;

public sealed class PdfImageElementItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int PageIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double OriginalX { get; init; }
    public double OriginalY { get; init; }
    public double OriginalWidth { get; init; }
    public double OriginalHeight { get; init; }
    public byte[]? ImageBytes { get; init; }
    public string BackgroundColorHex { get; set; } = "FFFFFF";
    public bool IsDeleted { get; set; }
    public double RotationDegrees { get; set; }

    public bool IsMoved =>
        Math.Abs(X - OriginalX) > 0.5 || Math.Abs(Y - OriginalY) > 0.5 ||
        Math.Abs(Width - OriginalWidth) > 0.5 || Math.Abs(Height - OriginalHeight) > 0.5;

    public bool IsRotated => Math.Abs(RotationDegrees) > 0.5;

    public bool IsModified => IsDeleted || IsMoved || IsRotated;

    public PdfImageElementItem Clone() => new()
    {
        Id = Id,
        PageIndex = PageIndex,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        OriginalX = OriginalX,
        OriginalY = OriginalY,
        OriginalWidth = OriginalWidth,
        OriginalHeight = OriginalHeight,
        ImageBytes = ImageBytes,
        BackgroundColorHex = BackgroundColorHex,
        IsDeleted = IsDeleted,
        RotationDegrees = RotationDegrees
    };
}
