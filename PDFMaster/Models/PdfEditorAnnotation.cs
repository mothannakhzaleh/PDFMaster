namespace PDFMaster.Models;



public sealed class PdfEditorAnnotation

{

    public Guid Id { get; init; } = Guid.NewGuid();

    public int PageIndex { get; set; }

    public PdfEditorTool Type { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string? Text { get; set; }

    public string? ColorHex { get; set; }

    public string? ImagePath { get; set; }

    public bool Filled { get; init; }

    public double FontSize { get; set; } = 14;

    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public double RotationDegrees { get; set; }



    public PdfEditorAnnotation Clone() => new()

    {

        Id = Id,

        PageIndex = PageIndex,

        Type = Type,

        X = X,

        Y = Y,

        Width = Width,

        Height = Height,

        Text = Text,

        ColorHex = ColorHex,

        ImagePath = ImagePath,

        Filled = Filled,

        FontSize = FontSize,

        FontFamily = FontFamily,

        Bold = Bold,

        Italic = Italic,

        RotationDegrees = RotationDegrees

    };

}

