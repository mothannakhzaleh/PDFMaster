namespace PDFMaster.Models;

public sealed class PdfTextBlockItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int PageIndex { get; set; }
    public string Text { get; init; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double OriginalX { get; init; }
    public double OriginalY { get; init; }
    public double OriginalWidth { get; init; }
    public double OriginalHeight { get; init; }
    public double FontSize { get; set; } = 12;
    public string ColorHex { get; set; } = "111827";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool OriginalBold { get; init; }
    public bool OriginalItalic { get; init; }
    public string OriginalColorHex { get; init; } = "111827";
    public string BackgroundColorHex { get; set; } = "FFFFFF";
    public List<TextCoverSegment> CoverSegments { get; set; } = [];
    public double OriginalFontSize { get; init; } = 12;
    public string? ReplacementText { get; set; }
    public bool IsDeleted { get; set; }
    public double RotationDegrees { get; set; }

    public bool IsMoved => Math.Abs(X - OriginalX) > 0.5 || Math.Abs(Y - OriginalY) > 0.5;

    public bool IsResized =>
        Math.Abs(Width - OriginalWidth) > 0.5 || Math.Abs(Height - OriginalHeight) > 0.5;

    public bool IsRotated => Math.Abs(RotationDegrees) > 0.5;

    public bool IsModified =>
        IsDeleted ||
        IsMoved ||
        IsResized ||
        IsRotated ||
        (ReplacementText is not null && !string.Equals(ReplacementText, Text, StringComparison.Ordinal)) ||
        Math.Abs(FontSize - OriginalFontSize) > 0.1 ||
        !string.Equals(ColorHex, OriginalColorHex, StringComparison.OrdinalIgnoreCase) ||
        Bold != OriginalBold ||
        Italic != OriginalItalic;

    public string CurrentText => IsDeleted ? string.Empty : ReplacementText ?? Text;

    public PdfTextBlockItem Clone() => new()
    {
        Id = Id,
        PageIndex = PageIndex,
        Text = Text,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        OriginalX = OriginalX,
        OriginalY = OriginalY,
        OriginalWidth = OriginalWidth,
        OriginalHeight = OriginalHeight,
        FontSize = FontSize,
        ColorHex = ColorHex,
        Bold = Bold,
        Italic = Italic,
        OriginalBold = OriginalBold,
        OriginalItalic = OriginalItalic,
        OriginalColorHex = OriginalColorHex,
            BackgroundColorHex = BackgroundColorHex,
            CoverSegments = CoverSegments.Select(s => new TextCoverSegment
            {
                X = s.X,
                Y = s.Y,
                Width = s.Width,
                Height = s.Height,
                ColorHex = s.ColorHex
            }).ToList(),
            OriginalFontSize = OriginalFontSize,
        ReplacementText = ReplacementText,
        IsDeleted = IsDeleted,
        RotationDegrees = RotationDegrees
    };
}
