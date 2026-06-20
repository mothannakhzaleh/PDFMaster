using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using PdfSharp.Pdf.IO;
using PDFMaster.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PDFMaster.Services;

public sealed class PdfToWordService
{
    private const int ExtractionRenderWidth = 1200;
    private const int PointsToTwips = 20;
    private const int PointsToEmu = 12700;

    private readonly PdfTextExtractionService _textExtract = new();
    private readonly PdfImageExtractionService _imageExtract = new();

    public int Convert(string pdfPath, string outputPath, int fromPage = 1, int toPage = int.MaxValue,
        bool preserveLayout = true, IProgress<int>? progress = null)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF file not found.", pdfPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        var pageCount = GetPageCount(pdfPath);
        if (pageCount == 0)
            throw new InvalidOperationException("The PDF has no pages.");

        var start = Math.Clamp(fromPage, 1, pageCount);
        var end = Math.Clamp(toPage, start, pageCount);
        var (firstPageW, firstPageH) = GetPageSize(pdfPath, start - 1);

        var convertedPages = 0;
        uint drawingId = 1;

        for (var pageNumber = start; pageNumber <= end; pageNumber++)
        {
            progress?.Report(pageNumber - start + 1);

            if (convertedPages > 0)
                body.AppendChild(CreatePageBreakParagraph());

            var pageIndex = pageNumber - 1;
            var (pageW, pageH) = GetPageSize(pdfPath, pageIndex);
            var wroteContent = preserveLayout &&
                               TryWriteLayoutPage(body, mainPart, pdfPath, pageIndex, pageW, pageH, ref drawingId);

            if (!wroteContent)
                WritePlainTextPage(body, pdfPath, pageNumber);

            convertedPages++;
        }

        if (convertedPages == 0)
            throw new InvalidOperationException("No pages were converted.");

        body.AppendChild(CreateSectionProperties(firstPageW, firstPageH));
        mainPart.Document.Save();
        return convertedPages;
    }

    private bool TryWriteLayoutPage(Body body, MainDocumentPart mainPart, string pdfPath, int pageIndex,
        double pageW, double pageH, ref uint drawingId)
    {
        var blocks = _textExtract.ExtractPageBlocks(pdfPath, pageIndex, pageW, pageH, ExtractionRenderWidth);
        var images = _imageExtract.ExtractPageImages(pdfPath, pageIndex, pageH);
        if (blocks.Count == 0 && images.Count == 0)
            return false;

        var elements = new List<LayoutElement>();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
                continue;

            elements.Add(new LayoutElement(block.Y, block.X, block.Height, block, null));
        }

        foreach (var image in images)
            elements.Add(new LayoutElement(image.Y, image.X, image.Height, null, image));

        if (elements.Count == 0)
            return false;

        var previousBottom = 0.0;
        foreach (var element in elements.OrderBy(e => e.Y).ThenBy(e => e.X))
        {
            var spacingBefore = Math.Max(0, element.Y - previousBottom);
            if (element.Block is not null)
                body.AppendChild(CreateTextParagraph(element.Block, spacingBefore));
            else if (element.Image is not null)
                body.AppendChild(CreateImageParagraph(mainPart, element.Image, spacingBefore, ref drawingId));

            previousBottom = element.Y + element.Height;
        }

        return true;
    }

    private sealed record LayoutElement(
        double Y,
        double X,
        double Height,
        PdfTextBlockItem? Block,
        PdfImageElementItem? Image);

    private void WritePlainTextPage(Body body, string pdfPath, int pageNumber)
    {
        using var pdf = PdfDocument.Open(pdfPath);
        var page = pdf.GetPage(pageNumber);
        var lines = ExtractPlainLines(page);
        if (lines.Count == 0)
        {
            body.AppendChild(CreateParagraph(string.Empty));
            return;
        }

        foreach (var line in lines)
            body.AppendChild(CreateParagraph(line));
    }

    private static List<string> ExtractPlainLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();

        if (words.Count == 0)
            return [];

        var grouped = GroupWordsIntoLines(words);
        return grouped
            .Select(line => string.Join(" ", line.Select(w => w.Text)).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static List<List<Word>> GroupWordsIntoLines(IReadOnlyList<Word> words)
    {
        var ordered = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<List<Word>>();
        foreach (var word in ordered)
        {
            var centerY = (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2.0;
            var tolerance = Math.Max(2, word.BoundingBox.Height * 0.55);
            var line = lines.LastOrDefault(existing =>
            {
                var lineCenter = existing.Average(w => (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0);
                return Math.Abs(lineCenter - centerY) <= tolerance;
            });

            if (line is null)
            {
                line = [];
                lines.Add(line);
            }

            line.Add(word);
        }

        foreach (var line in lines)
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));

        return lines;
    }

    private static Paragraph CreateTextParagraph(PdfTextBlockItem block, double spacingBeforePt)
    {
        var paragraph = new Paragraph();
        var paragraphProps = new ParagraphProperties();

        if (spacingBeforePt > 0.5)
        {
            paragraphProps.Append(new SpacingBetweenLines
            {
                Before = ToTwips(spacingBeforePt),
                After = "0"
            });
        }

        if (block.X > 0.5)
        {
            paragraphProps.Indentation = new Indentation
            {
                Left = ToTwips(block.X)
            };
        }

        if (paragraphProps.HasChildren)
            paragraph.Append(paragraphProps);

        var run = new Run();
        var runProps = new RunProperties
        {
            RunFonts = new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
            FontSize = new FontSize { Val = Math.Max(16, (int)Math.Round(block.FontSize * 2)).ToString(CultureInfo.InvariantCulture) }
        };

        if (block.Bold)
            runProps.Bold = new Bold();
        if (block.Italic)
            runProps.Italic = new Italic();
        if (!string.IsNullOrWhiteSpace(block.ColorHex))
            runProps.Color = new Color { Val = block.ColorHex };

        run.Append(runProps);
        run.Append(new Text(SanitizeXmlText(block.Text)) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.Append(run);
        return paragraph;
    }

    private static Paragraph CreateImageParagraph(MainDocumentPart mainPart, PdfImageElementItem image,
        double spacingBeforePt, ref uint drawingId)
    {
        var paragraph = new Paragraph();
        var paragraphProps = new ParagraphProperties();

        if (spacingBeforePt > 0.5)
        {
            paragraphProps.Append(new SpacingBetweenLines
            {
                Before = ToTwips(spacingBeforePt),
                After = "0"
            });
        }

        if (image.X > 0.5)
        {
            paragraphProps.Indentation = new Indentation
            {
                Left = ToTwips(image.X)
            };
        }

        if (paragraphProps.HasChildren)
            paragraph.Append(paragraphProps);

        if (image.ImageBytes is null || image.ImageBytes.Length == 0)
            return new Paragraph();

        var imagePartType = IsPng(image.ImageBytes) ? ImagePartType.Png : ImagePartType.Jpeg;
        var imagePart = mainPart.AddImagePart(imagePartType);
        using (var stream = new MemoryStream(image.ImageBytes))
            imagePart.FeedData(stream);

        var relationshipId = mainPart.GetIdOfPart(imagePart);
        var widthEmu = (long)Math.Max(1, image.Width * PointsToEmu);
        var heightEmu = (long)Math.Max(1, image.Height * PointsToEmu);
        drawingId++;

        var run = new Run();
        run.Append(new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent
                {
                    LeftEdge = 0L,
                    TopEdge = 0L,
                    RightEdge = 0L,
                    BottomEdge = 0L
                },
                new DW.DocProperties { Id = drawingId, Name = $"Picture {drawingId}" },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties
                                {
                                    Id = drawingId,
                                    Name = $"Image {drawingId}"
                                },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip
                                {
                                    Embed = relationshipId,
                                    CompressionState = A.BlipCompressionValues.Print
                                },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0, Y = 0 },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                {
                                    Preset = A.ShapeTypeValues.Rectangle
                                }))
                    )
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
                    }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            }));

        paragraph.Append(run);
        return paragraph;
    }

    private static SectionProperties CreateSectionProperties(double pageWidthPt, double pageHeightPt) =>
        new(
            new DocumentFormat.OpenXml.Wordprocessing.PageSize
            {
                Width = (UInt32Value)(uint)Math.Round(pageWidthPt * PointsToTwips),
                Height = (UInt32Value)(uint)Math.Round(pageHeightPt * PointsToTwips)
            },
            new PageMargin
            {
                Top = 0,
                Right = 0,
                Bottom = 0,
                Left = 0,
                Header = 0U,
                Footer = 0U,
                Gutter = 0U
            });

    private static Paragraph CreateParagraph(string text) =>
        new(new Run(new Text(SanitizeXmlText(text)) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph CreatePageBreakParagraph() =>
        new(new Run(new Break { Type = BreakValues.Page }));

    private static string ToTwips(double points) =>
        Math.Max(0, (int)Math.Round(points * PointsToTwips)).ToString(CultureInfo.InvariantCulture);

    private static int GetPageCount(string pdfPath)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static (double Width, double Height) GetPageSize(string pdfPath, int pageIndex)
    {
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
            return (612, 792);

        var page = doc.Pages[pageIndex];
        return (page.Width.Point, page.Height.Point);
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 &&
        bytes[0] == 0x89 &&
        bytes[1] == 0x50 &&
        bytes[2] == 0x4E &&
        bytes[3] == 0x47;

    private static string SanitizeXmlText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return new string(text.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= 0x20).ToArray());
    }
}
