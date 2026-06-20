namespace PDFMaster.Services;

using PDFMaster.Models;

public static class PageBackgroundSampler
{
    public static string SampleFromRenderBytes(
        byte[] rawBytes,
        int renderW,
        int renderH,
        int left,
        int top,
        int right,
        int bottom,
        int margin = 16)
    {
        if (rawBytes.Length == 0 || renderW <= 0 || renderH <= 0)
            return "FFFFFF";

        left = Math.Clamp(left, 0, renderW - 1);
        right = Math.Clamp(right, left, renderW - 1);
        top = Math.Clamp(top, 0, renderH - 1);
        bottom = Math.Clamp(bottom, top, renderH - 1);

        var samples = new List<(byte r, byte g, byte b)>();
        var bandSamples = CollectVerticalBandSamples(rawBytes, renderW, renderH, left, top, right, bottom, margin);
        var sideSamples = CollectSideSamples(rawBytes, renderW, renderH, left, top, right, bottom, margin);
        samples.AddRange(bandSamples);
        samples.AddRange(bandSamples);
        samples.AddRange(sideSamples);

        if (bandSamples.Count < 4)
            samples.AddRange(CollectInteriorBackgroundSamples(rawBytes, renderW, renderH, left, top, right, bottom));

        if (samples.Count == 0)
            samples.AddRange(CollectSideSamples(rawBytes, renderW, renderH, left, top, right, bottom, margin * 2));

        if (samples.Count == 0)
            return "FFFFFF";

        var color = RepresentativeBackground(samples);
        if (IsLikelyInkOrLine(color))
        {
            var fallback = samples
                .Where(c => !IsLikelyInkOrLine(c))
                .OrderByDescending(Luminance)
                .FirstOrDefault();
            if (fallback != default)
                color = fallback;
        }

        return IsLikelyInkOrLine(color) ? "FFFFFF" : ToHex(color);
    }

    public static string SampleFromPdfRect(
        byte[] rawBytes,
        int renderW,
        int renderH,
        double pdfPageW,
        double pdfPageH,
        double pdfX,
        double pdfY,
        double pdfW,
        double pdfH)
    {
        if (pdfPageW <= 0 || pdfPageH <= 0)
            return "FFFFFF";

        var scaleX = renderW / pdfPageW;
        var scaleY = renderH / pdfPageH;
        var left = (int)Math.Floor(pdfX * scaleX);
        var top = (int)Math.Floor(pdfY * scaleY);
        var right = (int)Math.Ceiling((pdfX + pdfW) * scaleX);
        var bottom = (int)Math.Ceiling((pdfY + pdfH) * scaleY);
        return SampleFromRenderBytes(rawBytes, renderW, renderH, left, top, right, bottom);
    }

    public static IReadOnlyList<TextCoverSegment> SampleCoverSegmentsFromPdfRect(
        byte[] rawBytes,
        int renderW,
        int renderH,
        double pdfPageW,
        double pdfPageH,
        double pdfX,
        double pdfY,
        double pdfW,
        double pdfH,
        int columnCount = 8,
        int rowCount = 4)
    {
        _ = columnCount;
        _ = rowCount;

        if (pdfPageW <= 0 || pdfPageH <= 0 || pdfW <= 0 || pdfH <= 0)
            return [new TextCoverSegment { X = pdfX, Y = pdfY, Width = pdfW, Height = pdfH, ColorHex = "FFFFFF" }];

        var unified = SampleFromPdfRect(rawBytes, renderW, renderH, pdfPageW, pdfPageH, pdfX, pdfY, pdfW, pdfH);
        var midX = pdfX + pdfW / 2;
        var midY = pdfY + pdfH / 2;

        var leftColor = SampleFromPdfRect(rawBytes, renderW, renderH, pdfPageW, pdfPageH, pdfX, pdfY, pdfW / 2, pdfH);
        var rightColor = SampleFromPdfRect(rawBytes, renderW, renderH, pdfPageW, pdfPageH, midX, pdfY, pdfW / 2, pdfH);
        var topColor = SampleFromPdfRect(rawBytes, renderW, renderH, pdfPageW, pdfPageH, pdfX, pdfY, pdfW, pdfH / 2);
        var bottomColor = SampleFromPdfRect(rawBytes, renderW, renderH, pdfPageW, pdfPageH, pdfX, midY, pdfW, pdfH / 2);

        var horizontalDiff = ColorDifference(leftColor, rightColor);
        var verticalDiff = ColorDifference(topColor, bottomColor);
        const double splitThreshold = 22;

        if (horizontalDiff >= splitThreshold && horizontalDiff >= verticalDiff)
        {
            return
            [
                new TextCoverSegment { X = pdfX, Y = pdfY, Width = pdfW / 2, Height = pdfH, ColorHex = leftColor },
                new TextCoverSegment { X = midX, Y = pdfY, Width = pdfW / 2, Height = pdfH, ColorHex = rightColor }
            ];
        }

        if (verticalDiff >= splitThreshold)
        {
            return
            [
                new TextCoverSegment { X = pdfX, Y = pdfY, Width = pdfW, Height = pdfH / 2, ColorHex = topColor },
                new TextCoverSegment { X = pdfX, Y = midY, Width = pdfW, Height = pdfH / 2, ColorHex = bottomColor }
            ];
        }

        return [new TextCoverSegment { X = pdfX, Y = pdfY, Width = pdfW, Height = pdfH, ColorHex = unified }];
    }

    private static double ColorDifference(string hexA, string hexB)
    {
        var a = ParseHexColor(hexA);
        var b = ParseHexColor(hexB);
        var lumDiff = Math.Abs(Luminance(a) - Luminance(b));
        var rgbDiff = Math.Sqrt(
            Math.Pow(a.r - b.r, 2) +
            Math.Pow(a.g - b.g, 2) +
            Math.Pow(a.b - b.b, 2));
        return lumDiff + rgbDiff * 0.35;
    }

    private static (byte r, byte g, byte b) ParseHexColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return (255, 255, 255);

        return (
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    /// <summary>
    /// Sample strips directly above and below the text box where header/body background shows through.
    /// </summary>
    private static List<(byte r, byte g, byte b)> CollectVerticalBandSamples(
        byte[] rawBytes,
        int renderW,
        int renderH,
        int left,
        int top,
        int right,
        int bottom,
        int margin)
    {
        var samples = new List<(byte r, byte g, byte b)>();
        var band = Math.Max(margin, Math.Min(24, (bottom - top) / 3));
        var xStep = Math.Max(2, (right - left) / 8);

        for (var x = left; x <= right; x += xStep)
        {
            for (var y = top - band; y < top; y++)
                AddSample(rawBytes, renderW, renderH, x, y, samples);
            for (var y = bottom + 1; y <= bottom + band; y++)
                AddSample(rawBytes, renderW, renderH, x, y, samples);
        }

        return samples;
    }

    private static List<(byte r, byte g, byte b)> CollectInteriorBackgroundSamples(
        byte[] rawBytes,
        int renderW,
        int renderH,
        int left,
        int top,
        int right,
        int bottom)
    {
        var samples = new List<(byte r, byte g, byte b)>();
        var width = right - left;
        var height = bottom - top;
        if (width < 4 || height < 4)
            return samples;

        var inset = Math.Max(2, Math.Min(8, Math.Min(width, height) / 8));
        var xStep = Math.Max(2, width / 14);
        var yStep = Math.Max(2, height / 5);

        for (var y = top + inset; y <= bottom - inset; y += yStep)
        {
            for (var x = left + inset; x <= right - inset; x += xStep)
                AddSample(rawBytes, renderW, renderH, x, y, samples);
        }

        return samples;
    }

    private static List<(byte r, byte g, byte b)> CollectSideSamples(
        byte[] rawBytes,
        int renderW,
        int renderH,
        int left,
        int top,
        int right,
        int bottom,
        int margin)
    {
        var samples = new List<(byte r, byte g, byte b)>();
        var height = Math.Max(1, bottom - top);
        var yPositions = new[]
        {
            top + height / 4,
            top + height / 2,
            top + (height * 3) / 4
        };

        foreach (var y in yPositions)
        {
            AddSample(rawBytes, renderW, renderH, left - margin, y, samples);
            AddSample(rawBytes, renderW, renderH, left - margin / 2, y, samples);
            AddSample(rawBytes, renderW, renderH, left + Math.Max(2, (right - left) / 8), y, samples);
        }

        return samples;
    }

    private static void AddSample(byte[] rawBytes, int renderW, int renderH, int x, int y, List<(byte r, byte g, byte b)> samples)
    {
        if (x < 0 || y < 0 || x >= renderW || y >= renderH)
            return;

        var index = (y * renderW + x) * 4;
        if (index + 2 >= rawBytes.Length)
            return;

        var color = ReadCompositePixel(rawBytes, index);
        if (IsLikelyInkOrLine(color))
            return;

        samples.Add(color);
    }

    private static (byte r, byte g, byte b) ReadCompositePixel(byte[] rawBytes, int index)
    {
        var b = rawBytes[index];
        var g = rawBytes[index + 1];
        var r = rawBytes[index + 2];
        var alpha = rawBytes[index + 3] / 255.0;
        if (alpha >= 0.999)
            return (r, g, b);

        return (
            (byte)(r * alpha + 255 * (1 - alpha)),
            (byte)(g * alpha + 255 * (1 - alpha)),
            (byte)(b * alpha + 255 * (1 - alpha)));
    }

    private static (byte r, byte g, byte b) RepresentativeBackground(IReadOnlyList<(byte r, byte g, byte b)> samples)
    {
        var usable = samples.Where(c => !IsLikelyInkOrLine(c)).ToList();
        if (usable.Count == 0)
            usable = samples.ToList();

        var neutral = usable.Where(IsNeutralBackground).ToList();
        if (neutral.Count >= Math.Max(3, usable.Count / 3))
            usable = neutral;

        var nonWhite = usable.Where(c => Luminance(c) < 248).ToList();
        if (nonWhite.Count >= Math.Max(3, usable.Count / 3))
            usable = nonWhite;

        var sorted = usable.OrderBy(Luminance).ToList();
        var median = sorted[sorted.Count / 2];
        var close = usable
            .Where(c => Math.Abs(Luminance(c) - Luminance(median)) <= 28)
            .ToList();

        var pool = close.Count >= Math.Max(1, usable.Count / 4) ? close : usable;
        return (
            (byte)Math.Clamp(pool.Average(c => c.r), 0, 255),
            (byte)Math.Clamp(pool.Average(c => c.g), 0, 255),
            (byte)Math.Clamp(pool.Average(c => c.b), 0, 255));
    }

    private static bool IsNeutralBackground((byte r, byte g, byte b) color)
    {
        var spread = Math.Max(color.r, Math.Max(color.g, color.b)) -
                     Math.Min(color.r, Math.Min(color.g, color.b));
        return spread <= 24 && Luminance(color) is >= 120 and <= 245;
    }

    private static bool IsLikelyInkOrLine((byte r, byte g, byte b) color)
    {
        if (Luminance(color) < 80)
            return true;

        if (color.b > color.r + 18 && color.b >= color.g && Luminance(color) < 170)
            return true;

        return false;
    }

    private static double Luminance((byte r, byte g, byte b) color) =>
        color.r * 0.299 + color.g * 0.587 + color.b * 0.114;

    private static string ToHex((byte r, byte g, byte b) color) =>
        $"{color.r:X2}{color.g:X2}{color.b:X2}";
}
