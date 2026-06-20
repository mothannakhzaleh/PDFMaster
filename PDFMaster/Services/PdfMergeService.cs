using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfMergeService
{
    public void Merge(IReadOnlyList<string> orderedPaths, string outputPath, PdfCompressLevel level = PdfCompressLevel.Medium)
    {
        if (orderedPaths.Count == 0)
            throw new InvalidOperationException("No PDF files selected.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var output = new PdfDocument();
        foreach (var path in orderedPaths)
        {
            if (!File.Exists(path))
                continue;

            using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }

        if (output.PageCount == 0)
            throw new InvalidOperationException("Could not import any pages.");

        PdfCompressService.ApplyCompressionOptions(output, level);
        output.Save(outputPath);
    }
}
