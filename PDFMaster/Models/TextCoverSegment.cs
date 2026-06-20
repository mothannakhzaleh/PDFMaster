namespace PDFMaster.Models;

public sealed class TextCoverSegment
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string ColorHex { get; init; } = "FFFFFF";
}
