using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfTextExtractionService
{
    public List<PdfTextBlockItem> ExtractPageBlocks(string pdfPath, int pageIndex, double pdfPageWidth, double pdfPageHeight, int renderWidth)
    {
        if (!File.Exists(pdfPath) || pageIndex < 0)
            return [];

        try
        {
            using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(renderWidth, renderWidth * 2));
            if (pageIndex >= docReader.GetPageCount())
                return [];

            using var pageReader = docReader.GetPageReader(pageIndex);
            var rawBytes = pageReader.GetImage();
            var characters = pageReader.GetCharacters()
                .Where(c => !char.IsControl(c.Char))
                .ToList();

            if (characters.Count == 0)
                return [];

            var renderW = pageReader.GetPageWidth();
            var renderH = pageReader.GetPageHeight();
            if (renderW <= 0 || renderH <= 0)
                return [];

            rawBytes = PdfRenderService.CompositeOnWhite(rawBytes, renderW, renderH);

            var scaleX = pdfPageWidth / renderW;
            var scaleY = pdfPageHeight / renderH;
            var lines = GroupCharactersIntoLines(characters);
            return MergeLinesIntoBlocks(lines, pageIndex, scaleX, scaleY, rawBytes, renderW, renderH);
        }
        catch
        {
            return [];
        }
    }

    private static List<List<Character>> GroupCharactersIntoLines(IReadOnlyList<Character> characters)
    {
        var ordered = characters
            .OrderBy(c => (c.Box.Top + c.Box.Bottom) / 2.0)
            .ThenBy(c => c.Box.Left)
            .ToList();

        var lines = new List<List<Character>>();
        foreach (var character in ordered)
        {
            var centerY = (character.Box.Top + character.Box.Bottom) / 2.0;
            var tolerance = Math.Max(4, character.FontSize * 0.45);
            var line = lines.LastOrDefault(existing =>
            {
                var lineCenter = existing.Average(c => (c.Box.Top + c.Box.Bottom) / 2.0);
                return Math.Abs(lineCenter - centerY) <= tolerance;
            });

            if (line is null)
            {
                line = [];
                lines.Add(line);
            }

            line.Add(character);
        }

        return lines;
    }

    private static List<List<Character>> SplitLineIntoSegments(List<Character> line)
    {
        var ordered = line.OrderBy(c => c.Box.Left).ToList();
        if (ordered.Count == 0)
            return [];

        var segments = new List<List<Character>>();
        var current = new List<Character> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].Box.Left - ordered[i - 1].Box.Right;
            var threshold = Math.Max(ordered[i].FontSize * 4.5, 56);
            if (gap > threshold)
            {
                segments.Add(current);
                current = [];
            }

            current.Add(ordered[i]);
        }

        if (current.Count > 0)
            segments.Add(current);

        return segments;
    }

    private static List<PdfTextBlockItem> MergeLinesIntoBlocks(
        IEnumerable<List<Character>> lines,
        int pageIndex,
        double scaleX,
        double scaleY,
        byte[] rawBytes,
        int renderW,
        int renderH)
    {
        var blocks = new List<PdfTextBlockItem>();
        PdfTextBlockItem? current = null;

        foreach (var line in lines.OrderBy(l => l.Min(c => c.Box.Top)))
        {
            foreach (var segment in SplitLineIntoSegments(line))
            {
                var text = BuildLineText(segment);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var left = segment.Min(c => c.Box.Left);
                var top = segment.Min(c => c.Box.Top);
                var right = segment.Max(c => c.Box.Right);
                var bottom = segment.Max(c => c.Box.Bottom);
                var fontSize = segment.Average(c => c.FontSize) * scaleY;

                if (current is not null && ShouldMergeBlocks(current, text, left, top, right, bottom, fontSize, scaleX, scaleY))
                {
                    current = MergeBlocks(current, text, left, top, right, bottom, fontSize, scaleX, scaleY, rawBytes, renderW, renderH);
                    continue;
                }

                if (current is not null)
                    blocks.Add(current);

                current = CreateBlock(pageIndex, segment, text, left, top, right, bottom, fontSize, scaleX, scaleY, rawBytes, renderW, renderH);
            }
        }

        if (current is not null)
            blocks.Add(current);

        return ConsolidateParagraphBlocks(
            blocks.Where(b => b.Width > 1 && b.Height > 1 && b.Text.Length > 0).ToList(),
            scaleX,
            scaleY,
            rawBytes,
            renderW,
            renderH);
    }

    private static List<PdfTextBlockItem> ConsolidateParagraphBlocks(
        List<PdfTextBlockItem> blocks,
        double scaleX,
        double scaleY,
        byte[] rawBytes,
        int renderW,
        int renderH)
    {
        if (blocks.Count <= 1)
            return blocks;

        var ordered = blocks.OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
        var merged = new List<PdfTextBlockItem>();
        PdfTextBlockItem? current = ordered[0];

        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            var nextLeft = next.X / scaleX;
            var nextTop = next.Y / scaleY;
            var nextRight = (next.X + next.Width) / scaleX;
            var nextBottom = (next.Y + next.Height) / scaleY;

            if (current is not null &&
                ShouldMergeBlocks(current, next.Text, nextLeft, nextTop, nextRight, nextBottom, next.FontSize, scaleX, scaleY))
            {
                current = MergeBlocks(current, next.Text, nextLeft, nextTop, nextRight, nextBottom, next.FontSize, scaleX, scaleY, rawBytes, renderW, renderH);
                continue;
            }

            if (current is not null)
                merged.Add(current);
            current = next;
        }

        if (current is not null)
            merged.Add(current);
        return merged;
    }

    private static string BuildLineText(IEnumerable<Character> line)
    {
        var ordered = line.OrderBy(c => c.Box.Left).ToList();
        if (ordered.Count == 0)
            return string.Empty;

        if (ordered.Count == 1)
            return ordered[0].Char.ToString();

        var gaps = new List<double>();
        for (var i = 1; i < ordered.Count; i++)
            gaps.Add(Math.Max(0, ordered[i].Box.Left - ordered[i - 1].Box.Right));

        var medianGap = gaps.OrderBy(g => g).ElementAt(gaps.Count / 2);
        var avgCharWidth = ordered.Average(c => c.Box.Right - c.Box.Left);
        var fontSize = ordered[0].FontSize;
        var spaceThreshold = Math.Max(Math.Max(medianGap * 1.5, avgCharWidth * 0.38), fontSize * 0.2);

        var text = ordered[0].Char.ToString();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (gaps[i - 1] > spaceThreshold)
                text += ' ';
            text += ordered[i].Char;
        }

        return text.Trim();
    }

    private static bool DetectBold(IReadOnlyList<Character> segment, string text, double fontSize)
    {
        if (segment.Count == 0)
            return false;

        var trimmed = text.Trim();
        if (trimmed.Length >= 4 &&
            trimmed.ToUpperInvariant() == trimmed &&
            fontSize >= 16)
            return true;

        var avgWidth = segment.Average(c => Math.Max(1, c.Box.Right - c.Box.Left));
        var avgHeight = segment.Average(c => Math.Max(1, c.Box.Bottom - c.Box.Top));
        return avgWidth / avgHeight > 0.52 || avgWidth / Math.Max(1, fontSize) > 0.46;
    }

    private static bool DetectItalic(IReadOnlyList<Character> segment) =>
        segment.Any(c => Math.Abs(c.Angle) > 0.1);

    private static bool ShouldMergeBlocks(
        PdfTextBlockItem block,
        string lineText,
        double left,
        double top,
        double right,
        double bottom,
        double fontSize,
        double scaleX,
        double scaleY)
    {
        var blockBottom = (block.Y + block.Height) / scaleY;
        var gap = top - blockBottom;
        if (gap > Math.Max(8, block.FontSize / scaleY * 3.0))
            return false;

        var blockLeft = block.X / scaleX;
        var blockRight = (block.X + block.Width) / scaleX;
        var overlap = Math.Min(blockRight, right) - Math.Max(blockLeft, left);
        var minWidth = Math.Min(blockRight - blockLeft, right - left);
        if (minWidth > 0 && overlap <= minWidth * 0.05)
            return false;

        var sizeRatio = Math.Max(block.FontSize, fontSize) / Math.Max(1, Math.Min(block.FontSize, fontSize));
        if (sizeRatio > 1.35)
            return false;

        if (IsSectionHeader(block.Text) || IsSectionHeader(lineText))
            return false;

        if (Math.Abs(blockLeft - left) > Math.Max(36, block.FontSize / scaleY * 2.2))
            return false;

        return true;
    }

    private static bool IsSectionHeader(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 48)
            return false;

        if (!trimmed.Contains(' ') && trimmed.Length <= 24)
            return trimmed.ToUpperInvariant() == trimmed;

        var letters = trimmed.Count(char.IsLetter);
        return letters > 0 && trimmed.Length <= 32 && trimmed.ToUpperInvariant() == trimmed;
    }

    private static PdfTextBlockItem MergeBlocks(
        PdfTextBlockItem block,
        string lineText,
        double left,
        double top,
        double right,
        double bottom,
        double fontSize,
        double scaleX,
        double scaleY,
        byte[] rawBytes,
        int renderW,
        int renderH)
    {
        var mergedLeft = Math.Min(block.X / scaleX, left);
        var mergedTop = Math.Min(block.Y / scaleY, top);
        var mergedRight = Math.Max((block.X + block.Width) / scaleX, right);
        var mergedBottom = Math.Max((block.Y + block.Height) / scaleY, bottom);

        var originalLeftRender = Math.Min(block.OriginalX / scaleX, left);
        var originalTopRender = Math.Min(block.OriginalY / scaleY, top);
        var originalRightRender = Math.Max((block.OriginalX + block.OriginalWidth) / scaleX, right);
        var originalBottomRender = Math.Max((block.OriginalY + block.OriginalHeight) / scaleY, bottom);

        var previousBottom = (block.Y + block.Height) / scaleY;
        var lineGap = top - previousBottom;
        var joiner = lineGap > block.FontSize / scaleY * 1.4 ? " " : " ";

        var background = PageBackgroundSampler.SampleFromRenderBytes(
            rawBytes,
            renderW,
            renderH,
            (int)originalLeftRender,
            (int)originalTopRender,
            (int)originalRightRender,
            (int)originalBottomRender);
        var pdfPageW = renderW * scaleX;
        var pdfPageH = renderH * scaleY;
        var mergedWidth = (originalRightRender - originalLeftRender) * scaleX;
        var mergedHeight = (originalBottomRender - originalTopRender) * scaleY;
        var coverSegments = PageBackgroundSampler.SampleCoverSegmentsFromPdfRect(
            rawBytes, renderW, renderH, pdfPageW, pdfPageH,
            originalLeftRender * scaleX, originalTopRender * scaleY, mergedWidth, mergedHeight).ToList();

        return new PdfTextBlockItem
        {
            Id = block.Id,
            PageIndex = block.PageIndex,
            Text = $"{block.Text}{joiner}{lineText}",
            X = mergedLeft * scaleX,
            Y = mergedTop * scaleY,
            Width = (mergedRight - mergedLeft) * scaleX,
            Height = (mergedBottom - mergedTop) * scaleY,
            OriginalX = originalLeftRender * scaleX,
            OriginalY = originalTopRender * scaleY,
            OriginalWidth = mergedWidth,
            OriginalHeight = mergedHeight,
            FontSize = Math.Max(block.FontSize, fontSize),
            OriginalFontSize = block.OriginalFontSize,
            ColorHex = block.ColorHex,
            OriginalColorHex = block.OriginalColorHex,
            BackgroundColorHex = background,
            CoverSegments = coverSegments,
            Bold = block.Bold,
            Italic = block.Italic,
            OriginalBold = block.OriginalBold,
            OriginalItalic = block.OriginalItalic,
            ReplacementText = block.ReplacementText,
            IsDeleted = block.IsDeleted
        };
    }

    private static PdfTextBlockItem CreateBlock(
        int pageIndex,
        IReadOnlyList<Character> segment,
        string text,
        double left,
        double top,
        double right,
        double bottom,
        double fontSize,
        double scaleX,
        double scaleY,
        byte[] rawBytes,
        int renderW,
        int renderH)
    {
        var width = Math.Max(4, (right - left) * scaleX);
        var height = Math.Max(4, (bottom - top) * scaleY);
        var bold = DetectBold(segment, text, fontSize / scaleY);
        var italic = DetectItalic(segment);
        var background = PageBackgroundSampler.SampleFromRenderBytes(
            rawBytes, renderW, renderH, (int)left, (int)top, (int)right, (int)bottom);
        var pdfPageW = renderW * scaleX;
        var pdfPageH = renderH * scaleY;
        var coverSegments = PageBackgroundSampler.SampleCoverSegmentsFromPdfRect(
            rawBytes, renderW, renderH, pdfPageW, pdfPageH,
            left * scaleX, top * scaleY, width, height).ToList();
        return new PdfTextBlockItem
        {
            PageIndex = pageIndex,
            Text = text,
            X = left * scaleX,
            Y = top * scaleY,
            Width = width,
            Height = height,
            OriginalX = left * scaleX,
            OriginalY = top * scaleY,
            OriginalWidth = width,
            OriginalHeight = height,
            FontSize = Math.Max(8, fontSize),
            OriginalFontSize = Math.Max(8, fontSize),
            ColorHex = "111827",
            OriginalColorHex = "111827",
            BackgroundColorHex = background,
            CoverSegments = coverSegments,
            Bold = bold,
            Italic = italic,
            OriginalBold = bold,
            OriginalItalic = italic
        };
    }
}
