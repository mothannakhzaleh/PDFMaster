using System.IO;

namespace PDFMaster.Services;

public static class ToolLocator
{
    public static string BundledToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");
    public static string BundledGhostscriptDir => Path.Combine(BundledToolsDir, "gs", "bin");

    private static string? _ghostscriptPath;
    private static string? _ghostscriptWorkingDir;

    public static string? GhostscriptExecutable => _ghostscriptPath;
    public static string? GhostscriptWorkingDirectory => _ghostscriptWorkingDir;
    public static bool HasGhostscript => _ghostscriptPath is not null;

    public static bool ConfigureBundled()
    {
        _ghostscriptPath = ResolveGhostscript(out _ghostscriptWorkingDir);
        return _ghostscriptPath is not null;
    }

    private static string? ResolveGhostscript(out string? workingDirectory)
    {
        workingDirectory = null;

        var bundledCandidates = new[]
        {
            Path.Combine(BundledGhostscriptDir, "gswin64c.exe"),
            Path.Combine(BundledToolsDir, "gswin64c.exe")
        };

        foreach (var candidate in bundledCandidates)
        {
            if (File.Exists(candidate))
                return SetWorkingDir(candidate, out workingDirectory);
        }

        var bundledGsRoot = Path.Combine(BundledToolsDir, "gs");
        if (Directory.Exists(bundledGsRoot))
        {
            var found = Directory.EnumerateFiles(bundledGsRoot, "gswin64c.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (found is not null)
                return SetWorkingDir(found, out workingDirectory);
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        try
        {
            var gsRoot = Path.Combine(programFiles, "gs");
            if (Directory.Exists(gsRoot))
            {
                foreach (var dir in Directory.GetDirectories(gsRoot).OrderDescending())
                {
                    var candidate = Path.Combine(dir, "bin", "gswin64c.exe");
                    if (File.Exists(candidate))
                        return SetWorkingDir(candidate, out workingDirectory);
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string SetWorkingDir(string executablePath, out string? workingDirectory)
    {
        workingDirectory = Path.GetDirectoryName(executablePath);
        return Path.GetFullPath(executablePath);
    }
}
