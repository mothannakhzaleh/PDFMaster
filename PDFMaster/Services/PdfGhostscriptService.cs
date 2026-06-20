using System.Diagnostics;
using System.IO;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfGhostscriptService
{
    public void Compress(string inputPath, string outputPath, PdfCompressLevel level)
    {
        if (!ToolLocator.HasGhostscript)
            throw new InvalidOperationException("Ghostscript not found. Run setup-tools.bat.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var settings = level switch
        {
            PdfCompressLevel.Low => "/printer",
            PdfCompressLevel.Medium => "/ebook",
            PdfCompressLevel.High => "/ebook",
            PdfCompressLevel.Maximum => "/screen",
            _ => "/ebook"
        };

        var args = $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.4 -dPDFSETTINGS={settings} " +
                   $"-dNOPAUSE -dQUIET -dBATCH -sOutputFile=\"{outputPath}\" \"{inputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ToolLocator.GhostscriptExecutable!,
            Arguments = args,
            WorkingDirectory = ToolLocator.GhostscriptWorkingDirectory ?? Path.GetDirectoryName(ToolLocator.GhostscriptExecutable!)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start Ghostscript.");

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Ghostscript compression failed."
                : error.Trim());
    }
}
