using System.Globalization;
using System.IO;
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using PDFMaster.Models;

namespace PDFMaster.Services;

public sealed class PdfDocumentService
{
    private readonly PdfCompressService _compress = new();
    private readonly Stack<string> _undoSnapshots = new();
    private readonly Stack<string> _redoSnapshots = new();
    private readonly string _snapshotDir;

    public PdfDocumentService()
    {
        _snapshotDir = Path.Combine(Path.GetTempPath(), "PDFMaster", "snapshots");
        Directory.CreateDirectory(_snapshotDir);
    }

    public string? WorkingCopyPath { get; private set; }
    public string? OriginalPath { get; private set; }
    public bool CanUndo => _undoSnapshots.Count > 0;
    public bool CanRedo => _redoSnapshots.Count > 0;

    public int PageCount
    {
        get
        {
            if (WorkingCopyPath is null || !File.Exists(WorkingCopyPath))
                return 0;

            using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
    }

    public void Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("PDF file not found.", path);

        ClearHistory();
        OriginalPath = path;
        WorkingCopyPath = CreateTempCopy(path);
    }

    public void CreateNew(int pageCount = 1)
    {
        pageCount = Math.Max(1, pageCount);
        Close();

        var path = Path.Combine(_snapshotDir, $"{Guid.NewGuid():N}.pdf");
        using (var doc = new PdfDocument())
        {
            for (var i = 0; i < pageCount; i++)
                doc.AddPage();

            doc.Save(path);
        }

        WorkingCopyPath = path;
        OriginalPath = null;
    }

    public static void CreatePdfFromImages(IReadOnlyList<string> imagePaths, string outputPath)
    {
        if (imagePaths.Count == 0)
            throw new InvalidOperationException("At least one image is required.");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var doc = new PdfDocument();
        var added = 0;
        foreach (var imagePath in imagePaths)
        {
            if (!File.Exists(imagePath))
                continue;

            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            using var image = XImage.FromFile(imagePath);
            var margin = 36.0;
            var maxW = page.Width.Point - margin * 2;
            var maxH = page.Height.Point - margin * 2;
            var scale = Math.Min(maxW / Math.Max(1, image.PixelWidth), maxH / Math.Max(1, image.PixelHeight));
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            var x = (page.Width.Point - width) / 2;
            var y = (page.Height.Point - height) / 2;
            gfx.DrawImage(image, x, y, width, height);
            added++;
        }

        if (added == 0)
            throw new InvalidOperationException("No valid image files were found.");

        doc.Save(outputPath);
    }

    public void Close()
    {
        ClearHistory();
        DeleteIfExists(WorkingCopyPath);
        WorkingCopyPath = null;
        OriginalPath = null;
    }

    public (double Width, double Height) GetPageSize(int pageIndex)
    {
        if (WorkingCopyPath is null)
            return (612, 792);

        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Import);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
            return (612, 792);

        var page = doc.Pages[pageIndex];
        return (page.Width.Point, page.Height.Point);
    }

    public void Undo()
    {
        if (!CanUndo || WorkingCopyPath is null)
            return;

        _redoSnapshots.Push(CreateTempCopy(WorkingCopyPath));
        var previous = _undoSnapshots.Pop();
        File.Copy(previous, WorkingCopyPath, true);
        DeleteIfExists(previous);
    }

    public void Redo()
    {
        if (!CanRedo || WorkingCopyPath is null)
            return;

        _undoSnapshots.Push(CreateTempCopy(WorkingCopyPath));
        var next = _redoSnapshots.Pop();
        File.Copy(next, WorkingCopyPath, true);
        DeleteIfExists(next);
    }

    public void RotatePage(int pageIndex, int degrees) =>
        Modify(pageIndex, page =>
        {
            page.Rotate = (page.Rotate + degrees + 360) % 360;
        });

    public void DeletePage(int pageIndex) =>
        ModifyAll(doc =>
        {
            if (pageIndex >= 0 && pageIndex < doc.PageCount)
                doc.Pages.RemoveAt(pageIndex);
        });

    public int InsertBlankPageAfter(int pageIndex)
    {
        var insertIndex = 0;
        ModifyAll(doc =>
        {
            if (doc.PageCount == 0)
            {
                doc.AddPage();
                insertIndex = 0;
                return;
            }

            var referenceIndex = Math.Clamp(pageIndex, 0, doc.PageCount - 1);
            var reference = doc.Pages[referenceIndex];
            insertIndex = referenceIndex + 1;

            if (insertIndex >= doc.PageCount)
            {
                var addedPage = doc.AddPage();
                addedPage.Width = reference.Width;
                addedPage.Height = reference.Height;
                addedPage.Orientation = reference.Orientation;
                insertIndex = doc.PageCount - 1;
                return;
            }

            var newPage = doc.InsertPage(insertIndex);
            newPage.Width = reference.Width;
            newPage.Height = reference.Height;
            newPage.Orientation = reference.Orientation;
        });

        return insertIndex;
    }

    public void MovePage(int pageIndex, int delta) =>
        MovePageTo(pageIndex, pageIndex + delta);

    public void MovePageTo(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
            return;

        ModifyAll(doc =>
        {
            if (fromIndex < 0 || fromIndex >= doc.PageCount || toIndex < 0 || toIndex >= doc.PageCount)
                return;

            var page = doc.Pages[fromIndex];
            doc.Pages.RemoveAt(fromIndex);
            doc.Pages.Insert(toIndex, page);
        });
    }

    public void AddText(int pageIndex, string text, double x, double y, double fontSize, string colorHex) =>
        Modify(pageIndex, page =>
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var font = new XFont("Segoe UI", fontSize, XFontStyleEx.Regular);
            gfx.DrawString(text, font, new XSolidBrush(ParseColor(colorHex)), new XPoint(x, y));
        });

    public void AddImage(int pageIndex, string imagePath, double x, double y, double width, double height)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image file not found.", imagePath);

        Modify(pageIndex, page =>
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            using var image = XImage.FromFile(imagePath);
            gfx.DrawImage(image, x, y, width, height);
        });
    }

    public void AddRectangle(int pageIndex, double x, double y, double width, double height, string colorHex, bool filled) =>
        Modify(pageIndex, page =>
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var color = ParseColor(colorHex);
            var rect = new XRect(x, y, width, height);
            if (filled)
                gfx.DrawRectangle(new XSolidBrush(color), rect);
            else
                gfx.DrawRectangle(new XPen(color, 1.5), rect);
        });

    public void AddHighlight(int pageIndex, double x, double y, double width, double height) =>
        Modify(pageIndex, page =>
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(96, 255, 235, 59)), new XRect(x, y, width, height));
        });

    public void AddWhiteout(int pageIndex, double x, double y, double width, double height) =>
        Modify(pageIndex, page =>
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.DrawRectangle(XBrushes.White, new XRect(x, y, width, height));
        });

    public void AddStamp(int pageIndex, string text, double x, double y, double width, double height, double fontSize, string colorHex) =>
        Modify(pageIndex, page => DrawStamp(page, text, x, y, width, height, fontSize, colorHex));

    public void AddTextWatermark(string text, double fontSize, double opacity, bool diagonal)
    {
        ModifyAll(doc =>
        {
            for (var i = 0; i < doc.PageCount; i++)
            {
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var alpha = (int)Math.Clamp(opacity * 255, 20, 220);
                var brush = new XSolidBrush(XColor.FromArgb(alpha, 148, 163, 184));
                var font = new XFont("Segoe UI", fontSize, XFontStyleEx.Bold);

                if (diagonal)
                {
                    gfx.Save();
                    gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
                    gfx.RotateTransform(-35);
                    gfx.DrawString(text, font, brush, new XPoint(-page.Width.Point / 3, 0));
                    gfx.Restore();
                }
                else
                {
                    gfx.DrawString(text, font, brush, new XPoint(page.Width.Point / 2 - 80, page.Height.Point / 2));
                }
            }
        });
    }

    public void AddImageWatermark(string imagePath, double opacity)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image file not found.", imagePath);

        ModifyAll(doc =>
        {
            for (var i = 0; i < doc.PageCount; i++)
            {
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                using var image = XImage.FromFile(imagePath);
                var width = page.Width.Point * 0.35;
                var height = width * image.PixelHeight / Math.Max(1, image.PixelWidth);
                var x = (page.Width.Point - width) / 2;
                var y = (page.Height.Point - height) / 2;
                gfx.DrawImage(image, x, y, width, height);
            }
        });
    }

    public List<PdfFormFieldItem> GetFormFields()
    {
        if (WorkingCopyPath is null)
            return [];

        try
        {
            using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Import);
            var list = new List<PdfFormFieldItem>();
            CollectFieldsFromWidgets(doc, list);

            if (list.Count == 0 && doc.AcroForm?.Fields is not null)
            {
                try
                {
                    CollectFields(doc, doc.AcroForm.Fields, string.Empty, list);
                }
                catch
                {
                    // ignore broken acroform trees
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public void FlushAnnotations(IReadOnlyList<PdfEditorAnnotation> annotations)
    {
        if (WorkingCopyPath is null || annotations.Count == 0)
            return;

        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Modify);
        foreach (var annotation in annotations.OrderBy(a => a.PageIndex))
        {
            if (annotation.PageIndex < 0 || annotation.PageIndex >= doc.PageCount)
                continue;

            ApplyAnnotation(doc.Pages[annotation.PageIndex], annotation);
        }

        doc.Save(WorkingCopyPath);
    }

    public void ApplyTextReplacements(IReadOnlyList<PdfTextBlockItem> blocks)
    {
        if (WorkingCopyPath is null)
            return;

        var modified = blocks.Where(b => b.IsModified).ToList();
        if (modified.Count == 0)
            return;

        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Modify);
        foreach (var block in modified)
        {
            if (block.PageIndex < 0 || block.PageIndex >= doc.PageCount)
                continue;

            var page = doc.Pages[block.PageIndex];
            var padding = Math.Max(1.5, block.FontSize * 0.08);
            var (coverX, coverY, coverW, coverH) = TextBlockBoundsHelper.GetCoverBounds(block);
            var segments = block.CoverSegments.Count > 0
                ? block.CoverSegments
                :
                [
                    new TextCoverSegment
                    {
                        X = coverX,
                        Y = coverY,
                        Width = coverW,
                        Height = coverH,
                        ColorHex = block.BackgroundColorHex
                    }
                ];

            foreach (var segment in segments)
            {
                DrawWhiteout(page,
                    segment.X - padding,
                    segment.Y - padding,
                    segment.Width + padding * 2,
                    segment.Height + padding * 2,
                    segment.ColorHex);
            }

            if (!block.IsDeleted && !string.IsNullOrWhiteSpace(block.CurrentText))
            {
                DrawStyledText(page, block.CurrentText, block.X, block.Y + block.Height - block.FontSize * 0.85,
                    block.FontSize, block.ColorHex, block.Bold, block.Italic, block.Width, block.Height,
                    block.RotationDegrees);
            }
        }

        doc.Save(WorkingCopyPath);
    }

    public void ApplyImageChanges(IReadOnlyList<PdfImageElementItem> images)
    {
        if (WorkingCopyPath is null)
            return;

        var modified = images.Where(i => i.IsModified).ToList();
        if (modified.Count == 0)
            return;

        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Modify);
        foreach (var image in modified)
        {
            if (image.PageIndex < 0 || image.PageIndex >= doc.PageCount)
                continue;

            var page = doc.Pages[image.PageIndex];
            var padding = 2.0;
            DrawWhiteout(page,
                image.OriginalX - padding,
                image.OriginalY - padding,
                image.OriginalWidth + padding * 2,
                image.OriginalHeight + padding * 2,
                image.BackgroundColorHex);

            if (image.IsDeleted || image.ImageBytes is null || image.ImageBytes.Length == 0)
                continue;

            DrawImageBytes(page, image.ImageBytes, image.X, image.Y, image.Width, image.Height, image.RotationDegrees);
        }

        doc.Save(WorkingCopyPath);
    }

    public void SetFormFieldValue(string fieldName, string value)
    {
        try
        {
            ModifyAll(doc =>
            {
                if (TrySetWidgetValue(doc, fieldName, value))
                    return;

                var acroField = FindField(doc.AcroForm, fieldName);
                if (acroField is null || acroField.ReadOnly)
                    throw new InvalidOperationException($"Form field '{fieldName}' was not found.");

                acroField.Value = new PdfString(value);
                if (acroField is PdfTextField textField)
                    textField.Text = value;
                else if (acroField is PdfComboBoxField comboField)
                    comboField.Value = new PdfString(value);
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("AcroForm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This PDF has no editable form fields.", ex);
        }
    }

    public void RemoveFormField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));

        ModifyAll(doc =>
        {
            var removedFromPage = RemoveWidgetAnnotations(doc, fieldName);
            var removedFromForm = RemoveFromAcroFormFields(doc, fieldName);
            if (!removedFromPage && !removedFromForm)
                throw new InvalidOperationException($"Form field '{fieldName}' was not found.");
        });
    }

    public void UpdateTextFormField(string fieldName, TextFormFieldPromptResult settings)
    {
        ModifyAll(doc =>
        {
            var widget = FindWidgetAnnotation(doc, fieldName);
            if (widget is null)
                throw new InvalidOperationException($"Form field '{fieldName}' was not found.");

            widget.Elements.SetString("/DA", BuildTextFieldDefaultAppearance(
                settings.FontFamily, settings.FontSize, settings.ColorHex, settings.Bold, settings.Italic));
            widget.Elements.SetString("/V", settings.DefaultValue);
            ApplyWidgetRotation(widget, settings.RotationDegrees);
        });
    }

    public void UpdateComboFormField(string fieldName, ComboFormFieldPromptResult settings)
    {
        ModifyAll(doc =>
        {
            var widget = FindWidgetAnnotation(doc, fieldName);
            if (widget is null)
                throw new InvalidOperationException($"Form field '{fieldName}' was not found.");

            var optionArray = new PdfArray(doc);
            foreach (var option in settings.Options)
                optionArray.Elements.Add(new PdfString(option));
            widget.Elements.SetObject("/Opt", optionArray);
            widget.Elements.SetString("/V", settings.DefaultValue);
            widget.Elements.SetName("/FT", "/Ch");
            widget.Elements.SetInteger("/Ff", (int)PdfAcroFieldFlags.Combo);
            widget.Elements.SetString("/DA", BuildTextFieldDefaultAppearance(
                settings.FontFamily, settings.FontSize, settings.ColorHex, settings.Bold, settings.Italic));
            ApplyWidgetRotation(widget, settings.RotationDegrees);
        });
    }

    public void UpdateFormFieldBounds(string fieldName, int pageIndex, double x, double y, double width, double height)
    {
        ModifyAll(doc =>
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
                throw new InvalidOperationException("Invalid page index.");

            var widget = FindWidgetAnnotation(doc, fieldName);
            if (widget is null)
                throw new InvalidOperationException($"Form field '{fieldName}' was not found.");

            var pageHeight = doc.Pages[pageIndex].Height.Point;
            widget.Elements.SetObject("/Rect", CreateRectArray(doc, x, y, width, height, pageHeight));
        });
    }

    public void UpdateFormFieldRotation(string fieldName, int rotationDegrees)
    {
        ModifyAll(doc =>
        {
            var widget = FindWidgetAnnotation(doc, fieldName);
            if (widget is null)
                throw new InvalidOperationException($"Form field '{fieldName}' was not found.");

            ApplyWidgetRotation(widget, rotationDegrees);
        });
    }

    private static void ApplyWidgetRotation(PdfDictionary widget, int rotationDegrees)
    {
        var rotation = NormalizeFormFieldRotation(rotationDegrees);
        if (rotation != 0)
            widget.Elements.SetInteger("/Rotate", rotation);
        else if (widget.Elements.ContainsKey("/Rotate"))
            widget.Elements.Remove("/Rotate");
    }

    private static PdfDictionary? FindWidgetAnnotation(PdfDocument doc, string fieldName)
    {
        for (var pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
        {
            foreach (var widget in EnumeratePageWidgetAnnotations(doc.Pages[pageIndex]))
            {
                if (string.Equals(widget.Elements.GetString("/T"), fieldName, StringComparison.Ordinal))
                    return widget;
            }
        }

        return null;
    }

    public void AddTextFormField(int pageIndex, string name, double x, double y, double width, double height,
        string defaultValue = "", string colorHex = "111827", double fontSize = 12, string fontFamily = "Segoe UI",
        bool bold = false, bool italic = false, int rotationDegrees = 0)
    {
        ModifyAll(doc =>
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount)
                return;

            EnsureAcroFormDictionary(doc);
            var page = doc.Pages[pageIndex];
            var pageHeight = page.Height.Point;
            var widget = CreateTextFieldWidget(doc, name, x, y, width, height, defaultValue, colorHex, fontSize,
                fontFamily, bold, italic, rotationDegrees, pageHeight);
            RegisterFormField(doc, page, WrapAcroField<PdfTextField>(widget));
        });
    }

    public void AddComboFormField(int pageIndex, string name, double x, double y, double width, double height,
        IReadOnlyList<string> options, string defaultValue = "", string colorHex = "111827", double fontSize = 12,
        string fontFamily = "Segoe UI", bool bold = false, bool italic = false, int rotationDegrees = 0)
    {
        ModifyAll(doc =>
        {
            if (pageIndex < 0 || pageIndex >= doc.PageCount || options.Count == 0)
                return;

            EnsureAcroFormDictionary(doc);
            var page = doc.Pages[pageIndex];
            var pageHeight = page.Height.Point;
            var widget = CreateComboFieldWidget(doc, name, x, y, width, height, options, defaultValue, pageHeight,
                colorHex, fontSize, fontFamily, bold, italic, rotationDegrees);
            var comboField = ConfigureComboAcroField(widget, options, defaultValue);
            RegisterFormField(doc, page, comboField);
        });
    }

    private static void EnsureAcroFormDictionary(PdfDocument doc)
    {
        if (!doc.Internals.Catalog.Elements.ContainsKey("/AcroForm"))
        {
            var form = new PdfDictionary(doc);
            form.Elements.SetBoolean("/NeedAppearances", true);
            form.Elements.SetObject("/Fields", new PdfArray(doc));
            doc.Internals.Catalog.Elements.SetObject("/AcroForm", form);
            return;
        }

        if (doc.Internals.Catalog.Elements.GetObject("/AcroForm") is PdfDictionary existing)
        {
            existing.Elements.SetBoolean("/NeedAppearances", true);
            if (existing.Elements.GetObject("/Fields") is not PdfArray)
                existing.Elements.SetObject("/Fields", new PdfArray(doc));
        }
    }

    private static TField WrapAcroField<TField>(PdfDictionary widget) where TField : PdfAcroField =>
        (TField)Activator.CreateInstance(
            typeof(TField),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [widget],
            null)!;

    private static void RegisterFormField(PdfDocument doc, PdfPage page, PdfAcroField field)
    {
        doc.Internals.AddObject(field);

        if (doc.Internals.Catalog.Elements.GetObject("/AcroForm") is PdfDictionary form &&
            form.Elements.GetObject("/Fields") is PdfArray fields &&
            field.Reference is not null)
        {
            fields.Elements.Add(field.Reference);
        }

        if (field.Reference is not null)
        {
            if (page.Elements.TryGetValue("/Annots", out var item) && item is PdfArray annots)
                annots.Elements.Add(field.Reference);
            else
            {
                var array = new PdfArray(page.Owner);
                array.Elements.Add(field.Reference);
                page.Elements.SetObject("/Annots", array);
            }
        }
    }

    private static bool RemoveWidgetAnnotations(PdfDocument doc, string fieldName)
    {
        var removed = false;

        for (var pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
        {
            var page = doc.Pages[pageIndex];
            if (!page.Elements.TryGetValue("/Annots", out var item) || item is not PdfArray annots)
                continue;

            for (var i = annots.Elements.Count - 1; i >= 0; i--)
            {
                var dictionary = ResolveDictionary(annots.Elements[i]);
                if (dictionary is null)
                    continue;

                if (dictionary.Elements.GetName("/Subtype") != "/Widget")
                    continue;

                if (!string.Equals(dictionary.Elements.GetString("/T"), fieldName, StringComparison.Ordinal))
                    continue;

                annots.Elements.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private static bool RemoveFromAcroFormFields(PdfDocument doc, string fieldName)
    {
        if (doc.Internals.Catalog.Elements.GetObject("/AcroForm") is not PdfDictionary form)
            return false;

        if (form.Elements.GetObject("/Fields") is not PdfArray fields)
            return false;

        var removed = false;
        for (var i = fields.Elements.Count - 1; i >= 0; i--)
        {
            if (!TryGetFieldName(fields.Elements[i], out var name))
                continue;

            if (!string.Equals(name, fieldName, StringComparison.Ordinal))
                continue;

            fields.Elements.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    private static bool TryGetFieldName(PdfItem item, out string? name)
    {
        name = null;
        var dictionary = ResolveDictionary(item);
        if (dictionary is null)
            return false;

        name = dictionary.Elements.GetString("/T");
        return !string.IsNullOrWhiteSpace(name);
    }

    private static PdfDictionary? ResolveDictionary(PdfItem item) =>
        item switch
        {
            PdfDictionary dictionary => dictionary,
            PdfSharp.Pdf.Advanced.PdfReference reference when reference.Value is PdfDictionary dictionary => dictionary,
            _ => null
        };

    private static bool TrySetWidgetValue(PdfDocument doc, string fieldName, string value)
    {
        for (var pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
        {
            foreach (var widget in EnumeratePageWidgetAnnotations(doc.Pages[pageIndex]))
            {
                if (!string.Equals(widget.Elements.GetString("/T"), fieldName, StringComparison.Ordinal))
                    continue;

                widget.Elements.SetString("/V", value);
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<PdfDictionary> EnumeratePageWidgetAnnotations(PdfPage page)
    {
        if (!page.Elements.TryGetValue("/Annots", out var item) || item is null)
            yield break;

        foreach (var dictionary in ExpandAnnotationItems(page, item))
        {
            if (dictionary.Elements.GetName("/Subtype") != "/Widget")
                continue;

            yield return dictionary;
        }
    }

    private static IEnumerable<PdfDictionary> ExpandAnnotationItems(PdfPage page, PdfItem item)
    {
        switch (item)
        {
            case PdfArray array:
                foreach (var element in array.Elements)
                {
                    foreach (var dictionary in ExpandAnnotationItems(page, element))
                        yield return dictionary;
                }
                yield break;
            case PdfSharp.Pdf.Advanced.PdfReference reference when reference.Value is PdfDictionary dictionary:
                yield return dictionary;
                yield break;
            case PdfDictionary dictionary:
                yield return dictionary;
                yield break;
        }
    }

    private static PdfDictionary CreateTextFieldWidget(PdfDocument doc, string name, double x, double y,
        double width, double height, string defaultValue, string colorHex, double fontSize, string fontFamily,
        bool bold, bool italic, int rotationDegrees, double pageHeight)
    {
        var widget = new PdfDictionary(doc);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Tx");
        widget.Elements.SetString("/T", name);
        if (!string.IsNullOrWhiteSpace(defaultValue))
            widget.Elements.SetString("/V", defaultValue);
        widget.Elements.SetObject("/Rect", CreateRectArray(doc, x, y, width, height, pageHeight));
        widget.Elements.SetInteger("/F", 4);
        widget.Elements.SetInteger("/Q", 0);
        widget.Elements.SetString("/DA", BuildTextFieldDefaultAppearance(fontFamily, fontSize, colorHex, bold, italic));

        var rotation = NormalizeFormFieldRotation(rotationDegrees);
        if (rotation != 0)
            widget.Elements.SetInteger("/Rotate", rotation);

        return widget;
    }

    private static string BuildTextFieldDefaultAppearance(string fontFamily, double fontSize, string colorHex,
        bool bold, bool italic)
    {
        var pdfFont = MapFormFieldPdfFont(fontFamily, bold, italic);
        var color = ParseColor(colorHex);
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        return $"/{pdfFont} {fontSize.ToString(CultureInfo.InvariantCulture)} Tf {r:0.###} {g:0.###} {b:0.###} rg";
    }

    private static string MapFormFieldPdfFont(string fontFamily, bool bold, bool italic)
    {
        var serif = fontFamily.Contains("Times", StringComparison.OrdinalIgnoreCase)
                      || fontFamily.Contains("Georgia", StringComparison.OrdinalIgnoreCase);
        var mono = fontFamily.Contains("Courier", StringComparison.OrdinalIgnoreCase);

        if (mono)
        {
            if (bold && italic) return "Courier-BoldOblique";
            if (bold) return "Courier-Bold";
            if (italic) return "Courier-Oblique";
            return "Courier";
        }

        if (serif)
        {
            if (bold && italic) return "Times-BoldItalic";
            if (bold) return "Times-Bold";
            if (italic) return "Times-Italic";
            return "Times-Roman";
        }

        if (bold && italic) return "Helvetica-BoldOblique";
        if (bold) return "Helvetica-Bold";
        if (italic) return "Helvetica-Oblique";
        return "Helvetica";
    }

    private static int NormalizeFormFieldRotation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return normalized switch
        {
            90 or 180 or 270 => normalized,
            >= 45 and < 135 => 90,
            >= 135 and < 225 => 180,
            >= 225 and < 315 => 270,
            _ => 0
        };
    }

    private static PdfDictionary CreateComboFieldWidget(PdfDocument doc, string name, double x, double y,
        double width, double height, IReadOnlyList<string> options, string defaultValue, double pageHeight,
        string colorHex = "111827", double fontSize = 12, string fontFamily = "Segoe UI", bool bold = false,
        bool italic = false, int rotationDegrees = 0)
    {
        var widget = new PdfDictionary(doc);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Ch");
        widget.Elements.SetInteger("/Ff", (int)PdfAcroFieldFlags.Combo);
        widget.Elements.SetString("/T", name);

        var optionArray = new PdfArray(doc);
        foreach (var option in options)
            optionArray.Elements.Add(new PdfString(option));
        widget.Elements.SetObject("/Opt", optionArray);
        widget.Elements.SetString("/V", string.IsNullOrWhiteSpace(defaultValue) ? options[0] : defaultValue);
        widget.Elements.SetObject("/Rect", CreateRectArray(doc, x, y, width, height, pageHeight));
        widget.Elements.SetInteger("/F", 4);
        widget.Elements.SetString("/DA", BuildTextFieldDefaultAppearance(fontFamily, fontSize, colorHex, bold, italic));

        var rotation = NormalizeFormFieldRotation(rotationDegrees);
        if (rotation != 0)
            widget.Elements.SetInteger("/Rotate", rotation);

        return widget;
    }

    private static PdfArray CreateRectArray(PdfDocument doc, double x, double topY, double width, double height, double pageHeight)
    {
        var bottomY = pageHeight - topY - height;
        var topPdfY = pageHeight - topY;
        var rect = new PdfArray(doc);
        rect.Elements.Add(new PdfReal(x));
        rect.Elements.Add(new PdfReal(bottomY));
        rect.Elements.Add(new PdfReal(x + width));
        rect.Elements.Add(new PdfReal(topPdfY));
        return rect;
    }

    public void RemoveBrokenAcroFormFieldEntries()
    {
        ModifyAll(doc =>
        {
            if (doc.Internals.Catalog.Elements.GetObject("/AcroForm") is not PdfDictionary form)
                return;

            if (form.Elements.GetObject("/Fields") is not PdfArray fields)
                return;

            for (var i = fields.Elements.Count - 1; i >= 0; i--)
            {
                if (fields.Elements[i] is PdfDictionary)
                    fields.Elements.RemoveAt(i);
            }
        });
    }

    public void Save(string outputPath, bool compress, PdfCompressLevel level)
    {
        if (WorkingCopyPath is null || !File.Exists(WorkingCopyPath))
            throw new InvalidOperationException("No document is open.");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (!compress)
        {
            File.Copy(WorkingCopyPath, outputPath, true);
            return;
        }

        _compress.Compress(WorkingCopyPath, outputPath, level);
    }

    private static void ApplyAnnotation(PdfPage page, PdfEditorAnnotation annotation)
    {
        switch (annotation.Type)
        {
            case PdfEditorTool.Text:
                if (!string.IsNullOrWhiteSpace(annotation.Text))
                    DrawStyledText(page, annotation.Text, annotation.X, annotation.Y, annotation.FontSize,
                        annotation.ColorHex ?? "111827", annotation.Bold, annotation.Italic,
                        annotation.Width, annotation.Height, annotation.RotationDegrees, annotation.FontFamily);
                break;
            case PdfEditorTool.Stamp:
                if (!string.IsNullOrWhiteSpace(annotation.Text))
                    DrawStamp(page, annotation.Text, annotation.X, annotation.Y, annotation.Width, annotation.Height,
                        annotation.FontSize, annotation.ColorHex ?? "E11D48", annotation.RotationDegrees,
                        annotation.FontFamily, annotation.Bold, annotation.Italic);
                break;
            case PdfEditorTool.Image:
            case PdfEditorTool.Signature:
                if (!string.IsNullOrWhiteSpace(annotation.ImagePath) && File.Exists(annotation.ImagePath))
                    DrawImage(page, annotation.ImagePath, annotation.X, annotation.Y, annotation.Width, annotation.Height,
                        annotation.RotationDegrees);
                break;
            case PdfEditorTool.Rectangle:
                DrawRectangle(page, annotation.X, annotation.Y, annotation.Width, annotation.Height, annotation.ColorHex ?? "E11D48", annotation.Filled);
                break;
            case PdfEditorTool.Ellipse:
                DrawEllipse(page, annotation.X, annotation.Y, annotation.Width, annotation.Height, annotation.ColorHex ?? "E11D48", annotation.Filled);
                break;
            case PdfEditorTool.Line:
                DrawLine(page, annotation.X, annotation.Y, annotation.X + annotation.Width, annotation.Y + annotation.Height,
                    annotation.ColorHex ?? "E11D48");
                break;
            case PdfEditorTool.Highlight:
                DrawHighlight(page, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                break;
            case PdfEditorTool.Whiteout:
                DrawWhiteout(page, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                break;
        }
    }

    private static void DrawText(PdfPage page, string text, double x, double y, double fontSize, string colorHex) =>
        DrawStyledText(page, text, x, y, fontSize, colorHex, false, false, 0, 0);

    private static void DrawStyledText(PdfPage page, string text, double x, double y, double fontSize, string colorHex,
        bool bold, bool italic, double width, double height, double rotationDegrees = 0, string fontFamily = "Segoe UI")
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var style = bold && italic ? XFontStyleEx.BoldItalic
            : bold ? XFontStyleEx.Bold
            : italic ? XFontStyleEx.Italic
            : XFontStyleEx.Regular;
        var font = new XFont(fontFamily, fontSize, style);
        var brush = new XSolidBrush(ParseColor(colorHex));

        if (width > 0 && height > 0)
        {
            var rect = new XRect(x, y, width, height);
            if (Math.Abs(rotationDegrees) > 0.01)
            {
                var centerX = rect.X + rect.Width / 2;
                var centerY = rect.Y + rect.Height / 2;
                gfx.TranslateTransform(centerX, centerY);
                gfx.RotateTransform(rotationDegrees);
                gfx.TranslateTransform(-centerX, -centerY);
            }

            gfx.DrawString(text, font, brush, rect, XStringFormats.TopLeft);
            return;
        }

        gfx.DrawString(text, font, brush, new XPoint(x, y));
    }

    private static void DrawImageBytes(PdfPage page, byte[] imageBytes, double x, double y, double width, double height,
        double rotationDegrees = 0)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var stream = new MemoryStream(imageBytes);
        using var image = XImage.FromStream(stream);
        if (Math.Abs(rotationDegrees) > 0.01)
        {
            var centerX = x + width / 2;
            var centerY = y + height / 2;
            gfx.TranslateTransform(centerX, centerY);
            gfx.RotateTransform(rotationDegrees);
            gfx.TranslateTransform(-centerX, -centerY);
        }

        gfx.DrawImage(image, x, y, width, height);
    }

    private static void DrawStamp(PdfPage page, string text, double x, double y, double width, double height, double fontSize,
        string colorHex, double rotationDegrees = 0, string fontFamily = "Segoe UI", bool bold = true, bool italic = false)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var style = bold && italic ? XFontStyleEx.BoldItalic
            : bold ? XFontStyleEx.Bold
            : italic ? XFontStyleEx.Italic
            : XFontStyleEx.Regular;
        var font = new XFont(fontFamily, fontSize, style);
        var color = ParseColor(colorHex);
        var rect = new XRect(x, y, Math.Max(80, width), Math.Max(28, height));

        if (Math.Abs(rotationDegrees) > 0.01)
        {
            var centerX = rect.X + rect.Width / 2;
            var centerY = rect.Y + rect.Height / 2;
            gfx.TranslateTransform(centerX, centerY);
            gfx.RotateTransform(rotationDegrees);
            gfx.TranslateTransform(-centerX, -centerY);
        }

        gfx.DrawRectangle(new XPen(color, 2), rect);
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(28, color)), rect);
        gfx.DrawString(text, font, new XSolidBrush(color), rect, XStringFormats.Center);
    }

    private static void DrawImage(PdfPage page, string imagePath, double x, double y, double width, double height,
        double rotationDegrees = 0)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var image = XImage.FromFile(imagePath);
        if (Math.Abs(rotationDegrees) > 0.01)
        {
            var centerX = x + width / 2;
            var centerY = y + height / 2;
            gfx.TranslateTransform(centerX, centerY);
            gfx.RotateTransform(rotationDegrees);
            gfx.TranslateTransform(-centerX, -centerY);
        }

        gfx.DrawImage(image, x, y, width, height);
    }

    private static void DrawRectangle(PdfPage page, double x, double y, double width, double height, string colorHex, bool filled)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var color = ParseColor(colorHex);
        var rect = new XRect(x, y, width, height);
        if (filled)
            gfx.DrawRectangle(new XSolidBrush(color), rect);
        else
            gfx.DrawRectangle(new XPen(color, 1.5), rect);
    }

    private static void DrawHighlight(PdfPage page, double x, double y, double width, double height)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(96, 255, 235, 59)), new XRect(x, y, width, height));
    }

    private static void DrawEllipse(PdfPage page, double x, double y, double width, double height, string colorHex, bool filled)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var color = ParseColor(colorHex);
        if (filled)
            gfx.DrawEllipse(new XSolidBrush(color), x, y, width, height);
        else
            gfx.DrawEllipse(new XPen(color, 1.5), x, y, width, height);
    }

    private static void DrawLine(PdfPage page, double x1, double y1, double x2, double y2, string colorHex)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.DrawLine(new XPen(ParseColor(colorHex), 2), x1, y1, x2, y2);
    }

    private static void DrawWhiteout(PdfPage page, double x, double y, double width, double height, string colorHex = "FFFFFF")
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        gfx.DrawRectangle(new XSolidBrush(ParseColor(colorHex)), new XRect(x, y, width, height));
    }

    private static void CollectFields(PdfDocument doc, PdfAcroField.PdfAcroFieldCollection fields, string prefix, List<PdfFormFieldItem> list)
    {
        foreach (var name in fields.Names)
        {
            try
            {
                var field = fields[name];
                if (field is null)
                    continue;

                var fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";

                if (field.HasKids)
                {
                    CollectFields(doc, field.Fields, fullName, list);
                    continue;
                }

                var bounds = TryGetFieldBounds(field, doc);
                var appearance = ParseDefaultAppearance(field.Elements.GetString("/DA"));
                var rotation = field.Elements.ContainsKey("/Rotate")
                    ? field.Elements.GetInteger("/Rotate")
                    : 0;
                list.Add(new PdfFormFieldItem
                {
                    Name = fullName,
                    FieldType = field switch
                    {
                        PdfTextField => "Text",
                        PdfComboBoxField => "Combo",
                        PdfListBoxField => "Combo",
                        _ => "Widget"
                    },
                    ReadOnly = field.ReadOnly,
                    Value = GetFieldValue(field),
                    PageIndex = bounds?.PageIndex ?? -1,
                    X = bounds?.X ?? 0,
                    Y = bounds?.Y ?? 0,
                    Width = bounds?.Width ?? 0,
                    Height = bounds?.Height ?? 0,
                    ColorHex = appearance.ColorHex,
                    FontSize = appearance.FontSize,
                    FontFamily = appearance.FontFamily,
                    Bold = appearance.Bold,
                    Italic = appearance.Italic,
                    RotationDegrees = rotation,
                    Options = ReadFieldOptions(field)
                });
            }
            catch
            {
                // skip broken fields
            }
        }
    }

    private static void CollectFieldsFromWidgets(PdfDocument doc, List<PdfFormFieldItem> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var pageIndex = 0; pageIndex < doc.PageCount; pageIndex++)
        {
            foreach (var annotation in EnumeratePageWidgetAnnotations(doc.Pages[pageIndex]))
            {
                try
                {
                    var name = annotation.Elements.GetString("/T");
                    if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                        continue;

                    var rect = TryGetAnnotationRect(annotation);
                    var pageHeight = doc.Pages[pageIndex].Height.Point;
                    var topY = rect is not null ? ConvertPdfBottomYToTopY(pageHeight, rect.Value.Y, rect.Value.Height) : 0;
                    var appearance = ParseDefaultAppearance(annotation.Elements.GetString("/DA"));
                    var rotation = annotation.Elements.ContainsKey("/Rotate")
                        ? annotation.Elements.GetInteger("/Rotate")
                        : 0;
                    list.Add(new PdfFormFieldItem
                    {
                        Name = name,
                        FieldType = annotation.Elements.GetName("/FT") switch
                        {
                            "/Tx" => "Text",
                            "/Ch" => "Combo",
                            _ => "Widget"
                        },
                        ReadOnly = false,
                        Value = annotation.Elements.GetString("/V") ?? string.Empty,
                        PageIndex = pageIndex,
                        X = rect?.X ?? 0,
                        Y = topY,
                        Width = rect?.Width ?? 0,
                        Height = rect?.Height ?? 0,
                        ColorHex = appearance.ColorHex,
                        FontSize = appearance.FontSize,
                        FontFamily = appearance.FontFamily,
                        Bold = appearance.Bold,
                        Italic = appearance.Italic,
                        RotationDegrees = rotation,
                        Options = ReadWidgetOptions(annotation)
                    });
                }
                catch
                {
                    // skip broken widgets
                }
            }
        }
    }

    private static PdfAcroField? FindField(PdfAcroForm? form, string fieldName)
    {
        if (form?.Fields is null || string.IsNullOrWhiteSpace(fieldName))
            return null;

        try
        {
            if (form.Fields.Names.Contains(fieldName))
                return form.Fields[fieldName];
        }
        catch
        {
            // fall through to recursive search
        }

        return FindFieldRecursive(form.Fields, fieldName, string.Empty);
    }

    private static PdfAcroField? FindFieldRecursive(PdfAcroField.PdfAcroFieldCollection fields, string targetName, string prefix)
    {
        foreach (var name in fields.Names)
        {
            try
            {
                var field = fields[name];
                if (field is null)
                    continue;

                var fullName = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
                if (string.Equals(fullName, targetName, StringComparison.Ordinal))
                    return field;

                if (field.HasKids && field.Fields is not null)
                {
                    var match = FindFieldRecursive(field.Fields, targetName, fullName);
                    if (match is not null)
                        return match;
                }
            }
            catch
            {
                // continue
            }
        }

        return null;
    }

    private static string GetFieldValue(PdfAcroField field)
    {
        if (field is PdfTextField textField && !string.IsNullOrEmpty(textField.Text))
            return textField.Text;

        return field.Value?.ToString() ?? string.Empty;
    }

    private static (int PageIndex, double X, double Y, double Width, double Height)? TryGetFieldBounds(PdfAcroField field, PdfDocument doc)
    {
        try
        {
            var rect = TryGetAnnotationRect(field);
            if (rect is null)
                return null;

            var pageIndex = TryGetFieldPageIndex(field, doc);
            if (pageIndex < 0)
                return null;

            var pageHeight = doc.Pages[pageIndex].Height.Point;
            var topY = ConvertPdfBottomYToTopY(pageHeight, rect.Value.Y, rect.Value.Height);
            return (pageIndex, rect.Value.X, topY, rect.Value.Width, rect.Value.Height);
        }
        catch
        {
            return null;
        }
    }

    private static int TryGetFieldPageIndex(PdfAcroField field, PdfDocument doc)
    {
        try
        {
            if (field.Elements.TryGetValue("/P", out var pageRef) && pageRef is PdfSharp.Pdf.Advanced.PdfReference reference)
            {
                for (var i = 0; i < doc.PageCount; i++)
                {
                    if (ReferenceEquals(doc.Pages[i], reference.Value))
                        return i;
                }
            }
        }
        catch
        {
            // ignore
        }

        return -1;
    }

    private static (double FontSize, string ColorHex, string FontFamily, bool Bold, bool Italic) ParseDefaultAppearance(string? da)
    {
        if (string.IsNullOrWhiteSpace(da))
            return (12, "111827", "Segoe UI", false, false);

        var fontSize = 12.0;
        var colorHex = "111827";
        var fontFamily = "Segoe UI";
        var bold = false;
        var italic = false;

        var fontMatch = System.Text.RegularExpressions.Regex.Match(da, @"/(\S+?)\s+([0-9.]+)\s+Tf");
        if (fontMatch.Success)
        {
            var pdfFont = fontMatch.Groups[1].Value;
            if (double.TryParse(fontMatch.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedSize))
                fontSize = parsedSize;

            bold = pdfFont.Contains("Bold", StringComparison.OrdinalIgnoreCase);
            italic = pdfFont.Contains("Oblique", StringComparison.OrdinalIgnoreCase) ||
                     pdfFont.Contains("Italic", StringComparison.OrdinalIgnoreCase);

            fontFamily = pdfFont switch
            {
                var f when f.Contains("Courier", StringComparison.OrdinalIgnoreCase) => "Courier New",
                var f when f.Contains("Times", StringComparison.OrdinalIgnoreCase) => "Times New Roman",
                _ => "Segoe UI"
            };
        }

        var colorMatch = System.Text.RegularExpressions.Regex.Match(da,
            @"([0-9.]+)\s+([0-9.]+)\s+([0-9.]+)\s+rg");
        if (colorMatch.Success &&
            double.TryParse(colorMatch.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var r) &&
            double.TryParse(colorMatch.Groups[2].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var g) &&
            double.TryParse(colorMatch.Groups[3].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var b))
        {
            colorHex = $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
        }

        return (fontSize, colorHex, fontFamily, bold, italic);
    }

    private static double ConvertPdfBottomYToTopY(double pageHeight, double pdfBottomY, double height) =>
        pageHeight - pdfBottomY - height;

    private static PdfComboBoxField ConfigureComboAcroField(PdfDictionary widget, IReadOnlyList<string> options,
        string defaultValue)
    {
        var field = WrapAcroField<PdfComboBoxField>(widget);
        var value = string.IsNullOrWhiteSpace(defaultValue) ? options[0] : defaultValue;
        field.Value = new PdfString(value);
        field.Elements.SetString("/V", value);
        field.Elements.SetInteger("/Ff", (int)PdfAcroFieldFlags.Combo);
        field.Elements.SetName("/FT", "/Ch");
        return field;
    }

    private static List<string> ReadFieldOptions(PdfAcroField field) => ReadWidgetOptions(field);

    private static List<string> ReadWidgetOptions(PdfDictionary widget)
    {
        var options = new List<string>();
        try
        {
            if (widget.Elements.GetObject("/Opt") is not PdfArray optionArray)
                return options;

            foreach (var item in optionArray.Elements)
            {
                if (item is PdfString pdfString && !string.IsNullOrWhiteSpace(pdfString.Value))
                    options.Add(pdfString.Value);
            }
        }
        catch
        {
            // ignore broken option arrays
        }

        return options;
    }

    private static (double X, double Y, double Width, double Height)? TryGetAnnotationRect(PdfDictionary dictionary)
    {
        try
        {
            if (!dictionary.Elements.TryGetValue("/Rect", out var rectItem))
                return null;

            double x1;
            double y1;
            double x2;
            double y2;

            switch (rectItem)
            {
                case PdfRectangle pdfRect:
                    x1 = pdfRect.X1;
                    y1 = pdfRect.Y1;
                    x2 = pdfRect.X2;
                    y2 = pdfRect.Y2;
                    break;
                case PdfArray arr when arr.Elements.Count >= 4:
                    x1 = arr.Elements.GetReal(0);
                    y1 = arr.Elements.GetReal(1);
                    x2 = arr.Elements.GetReal(2);
                    y2 = arr.Elements.GetReal(3);
                    break;
                default:
                    return null;
            }

            var x = Math.Min(x1, x2);
            var y = Math.Min(y1, y2);
            var width = Math.Abs(x2 - x1);
            var height = Math.Abs(y2 - y1);
            if (width <= 0 || height <= 0)
                return null;

            return (x, y, width, height);
        }
        catch
        {
            return null;
        }
    }

    private void Modify(int pageIndex, Action<PdfPage> action)
    {
        if (WorkingCopyPath is null)
            throw new InvalidOperationException("No document is open.");

        PushUndoSnapshot();
        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Modify);
        if (pageIndex < 0 || pageIndex >= doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        action(doc.Pages[pageIndex]);
        doc.Save(WorkingCopyPath);
    }

    private void ModifyAll(Action<PdfDocument> action)
    {
        if (WorkingCopyPath is null)
            throw new InvalidOperationException("No document is open.");

        PushUndoSnapshot();
        using var doc = PdfReader.Open(WorkingCopyPath, PdfDocumentOpenMode.Modify);
        action(doc);
        doc.Save(WorkingCopyPath);
    }

    private void PushUndoSnapshot()
    {
        if (WorkingCopyPath is null)
            return;

        _undoSnapshots.Push(CreateTempCopy(WorkingCopyPath));
        _redoSnapshots.Clear();
        TrimHistory(_undoSnapshots);
    }

    private void TrimHistory(Stack<string> stack)
    {
        if (stack.Count <= 30)
            return;

        var ordered = stack.Reverse().ToList();
        while (ordered.Count > 30)
        {
            DeleteIfExists(ordered[0]);
            ordered.RemoveAt(0);
        }

        stack.Clear();
        foreach (var item in ordered.AsEnumerable().Reverse())
            stack.Push(item);
    }

    private void ClearHistory()
    {
        while (_undoSnapshots.Count > 0)
            DeleteIfExists(_undoSnapshots.Pop());
        while (_redoSnapshots.Count > 0)
            DeleteIfExists(_redoSnapshots.Pop());
    }

    private string CreateTempCopy(string sourcePath)
    {
        var copyPath = Path.Combine(_snapshotDir, $"{Guid.NewGuid():N}.pdf");
        File.Copy(sourcePath, copyPath, true);
        return copyPath;
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is null || !File.Exists(path))
            return;

        try { File.Delete(path); } catch { /* best effort */ }
    }

    private static XColor ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return XColors.Black;

        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return XColor.FromArgb(r, g, b);
        }

        return XColors.Black;
    }
}
