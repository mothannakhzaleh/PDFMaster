using PDFMaster.Models;
using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PDFMaster.Services;

public sealed class PdfImageExtractionService
{
    public List<PdfImageElementItem> ExtractPageImages(string pdfPath, int pageIndex, double pdfPageHeight)
    {
        if (!File.Exists(pdfPath) || pageIndex < 0)
            return [];

        try
        {
            using var document = PdfDocument.Open(pdfPath);
            if (pageIndex >= document.NumberOfPages)
                return [];

            var page = document.GetPage(pageIndex + 1);
            var list = new List<PdfImageElementItem>();

            foreach (var image in page.GetImages())
            {
                try
                {
                    var bounds = image.BoundingBox;
                    if (bounds.Width < 4 || bounds.Height < 4)
                        continue;

                    var width = bounds.Width;
                    var height = bounds.Height;
                    var x = bounds.Left;
                    var top = pdfPageHeight - bounds.Top;
                    if (top < 0)
                        top = pdfPageHeight - bounds.Bottom - height;

                    var bytes = TryReadImageBytes(image);
                    if (bytes is null || bytes.Length == 0)
                        continue;

                    list.Add(new PdfImageElementItem
                    {
                        PageIndex = pageIndex,
                        X = x,
                        Y = top,
                        Width = width,
                        Height = height,
                        OriginalX = x,
                        OriginalY = top,
                        OriginalWidth = width,
                        OriginalHeight = height,
                        ImageBytes = bytes
                    });
                }
                catch
                {
                    // skip broken images
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static byte[]? TryReadImageBytes(IPdfImage image)
    {
        try
        {
            if (image.TryGetPng(out var png) && png.Length > 0)
                return png;

            if (image.TryGetBytesAsMemory(out var memory) && memory.Length > 0)
                return memory.ToArray();

            var raw = image.RawMemory;
            return raw.Length > 0 ? raw.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }
}
