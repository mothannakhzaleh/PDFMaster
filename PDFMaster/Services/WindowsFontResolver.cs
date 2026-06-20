using System.IO;
using PdfSharp.Fonts;

namespace PDFMaster.Services;

public sealed class WindowsFontResolver : IFontResolver
{
    private static readonly Dictionary<string, string> FontFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SegoeUI"] = "segoeui.ttf",
        ["SegoeUI-Bold"] = "segoeuib.ttf",
        ["SegoeUI-Italic"] = "segoeuii.ttf",
        ["SegoeUI-BoldItalic"] = "segoeuiz.ttf",
        ["Arial"] = "arial.ttf",
        ["Arial-Bold"] = "arialbd.ttf",
        ["Arial-Italic"] = "ariali.ttf",
        ["Arial-BoldItalic"] = "arialbi.ttf",
        ["TimesNewRoman"] = "times.ttf",
        ["TimesNewRoman-Bold"] = "timesbd.ttf",
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var key = familyName.ToLowerInvariant() switch
        {
            "segoe ui" or "segoeui" => BuildFace("SegoeUI", isBold, isItalic),
            "arial" => BuildFace("Arial", isBold, isItalic),
            "times new roman" or "times" => BuildFace("TimesNewRoman", isBold, isItalic),
            _ => BuildFace("SegoeUI", isBold, isItalic)
        };

        return FontFiles.ContainsKey(key) ? new FontResolverInfo(key) : new FontResolverInfo("SegoeUI");
    }

    public byte[]? GetFont(string faceName)
    {
        if (!FontFiles.TryGetValue(faceName, out var fileName))
            fileName = FontFiles["SegoeUI"];

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static string BuildFace(string family, bool bold, bool italic) => (bold, italic) switch
    {
        (true, true) => $"{family}-BoldItalic",
        (true, false) => $"{family}-Bold",
        (false, true) => $"{family}-Italic",
        _ => family
    };
}
