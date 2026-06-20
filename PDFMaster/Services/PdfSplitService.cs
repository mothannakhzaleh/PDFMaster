using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfSplitService
{
    public IReadOnlyList<string> SplitEveryPage(string inputPath, string outputFolder, string namePrefix)
    {
        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var outputs = new List<string>();
        Directory.CreateDirectory(outputFolder);

        for (var i = 0; i < source.PageCount; i++)
        {
            using var pageDoc = new PdfDocument();
            pageDoc.AddPage(source.Pages[i]);
            var fileName = $"{namePrefix}_page_{i + 1:D3}.pdf";
            var path = Path.Combine(outputFolder, fileName);
            pageDoc.Save(path);
            outputs.Add(path);
        }

        return outputs;
    }

    public IReadOnlyList<string> SplitEveryNPages(string inputPath, string outputFolder, string namePrefix, int pagesPerFile)
    {
        if (pagesPerFile < 1)
            throw new ArgumentOutOfRangeException(nameof(pagesPerFile));

        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var outputs = new List<string>();
        Directory.CreateDirectory(outputFolder);

        var part = 1;
        for (var start = 0; start < source.PageCount; start += pagesPerFile)
        {
            using var partDoc = new PdfDocument();
            var end = Math.Min(start + pagesPerFile, source.PageCount);
            for (var i = start; i < end; i++)
                partDoc.AddPage(source.Pages[i]);

            var path = Path.Combine(outputFolder, $"{namePrefix}_part_{part:D3}.pdf");
            partDoc.Save(path);
            outputs.Add(path);
            part++;
        }

        return outputs;
    }

    public IReadOnlyList<string> SplitAtPages(string inputPath, string outputFolder, string namePrefix, IReadOnlyList<int> splitAfterPages)
    {
        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var breakpoints = splitAfterPages
            .Where(p => p > 0 && p < source.PageCount)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var ranges = new List<(int Start, int End)>();
        var rangeStart = 0;
        foreach (var point in breakpoints)
        {
            ranges.Add((rangeStart, point - 1));
            rangeStart = point;
        }

        ranges.Add((rangeStart, source.PageCount - 1));

        var outputs = new List<string>();
        Directory.CreateDirectory(outputFolder);

        for (var part = 0; part < ranges.Count; part++)
        {
            var (start, end) = ranges[part];
            if (end < start)
                continue;

            using var partDoc = new PdfDocument();
            for (var i = start; i <= end; i++)
                partDoc.AddPage(source.Pages[i]);

            var path = Path.Combine(outputFolder, $"{namePrefix}_part_{part + 1:D3}.pdf");
            partDoc.Save(path);
            outputs.Add(path);
        }

        return outputs;
    }

    public string ExtractPageRange(string inputPath, string outputPath, int fromPage, int toPage, PdfCompressLevel level = PdfCompressLevel.Medium)
    {
        using var source = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var start = Math.Clamp(fromPage, 1, source.PageCount) - 1;
        var end = Math.Clamp(toPage, 1, source.PageCount) - 1;
        if (start > end)
            (start, end) = (end, start);

        using var output = new PdfDocument();
        for (var i = start; i <= end; i++)
            output.AddPage(source.Pages[i]);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        PdfCompressService.ApplyCompressionOptions(output, level);
        output.Save(outputPath);
        return outputPath;
    }

    public static bool TryParsePageRange(string text, out int from, out int to)
    {
        from = to = 0;
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.Contains('-'))
        {
            var parts = text.Split('-', 2);
            return int.TryParse(parts[0].Trim(), out from) && int.TryParse(parts[1].Trim(), out to);
        }

        if (int.TryParse(text, out from))
        {
            to = from;
            return true;
        }

        return false;
    }
}
