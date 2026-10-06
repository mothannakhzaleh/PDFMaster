using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfCompressService
{
    private readonly PdfGhostscriptService _ghostscript = new();

    public void Compress(string inputPath, string outputPath, PdfCompressLevel level)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("PDF file not found.", inputPath);

        if (ToolLocator.HasGhostscript && level is PdfCompressLevel.High or PdfCompressLevel.Maximum)
        {
            try
            {
                _ghostscript.Compress(inputPath, outputPath, level);
                return;
            }
            catch
            {
                // fall back to PDFsharp compression
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var bytes = File.ReadAllBytes(inputPath);
        using var stream = new MemoryStream(bytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        using var output = new PdfDocument();

        for (var i = 0; i < document.PageCount; i++)
            output.AddPage(document.Pages[i]);

        ApplyCompressionOptions(output, level);
        output.Save(outputPath);
    }

    public int CompressFolder(string inputFolder, string outputFolder, PdfCompressLevel level, IProgress<(int Done, int Total, string FileName)>? progress = null)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException("Input folder not found.");

        Directory.CreateDirectory(outputFolder);
        var files = Directory.GetFiles(inputFolder, "*.pdf", SearchOption.TopDirectoryOnly);
        var done = 0;

        foreach (var file in files)
        {
            var output = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file) + "_compressed.pdf");
            Compress(file, output, level);
            done++;
            progress?.Report((done, files.Length, Path.GetFileName(file)));
        }

        return done;
    }

    public static void ApplyCompressionOptions(PdfDocument document, PdfCompressLevel level)
    {
        document.Options.CompressContentStreams = level != PdfCompressLevel.Low;
        document.Options.NoCompression = level == PdfCompressLevel.Low;
        document.Options.FlateEncodeMode = level switch
        {
            PdfCompressLevel.Maximum => PdfFlateEncodeMode.BestCompression,
            PdfCompressLevel.High => PdfFlateEncodeMode.BestCompression,
            PdfCompressLevel.Medium => PdfFlateEncodeMode.Default,
            _ => PdfFlateEncodeMode.BestSpeed
        };
    }
}
