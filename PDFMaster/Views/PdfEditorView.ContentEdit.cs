using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfEditorView
{
    private readonly PdfImageExtractionService _imageExtract = new();
    private readonly List<PdfImageElementItem> _imageElements = [];
    private readonly Stack<List<PdfImageElementItem>> _imageUndo = [];
    private readonly Stack<List<PdfImageElementItem>> _imageRedo = [];

    private PdfImageElementItem? _selectedImageElement;
    private bool _isDraggingContent;
    private Point _contentDragStartScreen;
    private double _contentDragStartX;
    private double _contentDragStartY;
    private bool _suppressFormatPanelEvents;
    private bool _isResizingContent;
    private ContentResizeHandle _activeResizeHandle = ContentResizeHandle.None;
    private double _resizeStartW;
    private double _resizeStartH;
    private double _resizeStartFontSize;
    private bool _isRotatingContent;
    private Point _rotateCenterScreen;
    private double _rotateStartAngle;
    private double _rotateStartDegrees;

    private enum ContentResizeHandle
    {
        None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left
    }

    private static readonly (string Hex, string Name)[] BlockColors =
    [
        ("111827", "Black"),
        ("E11D48", "Red"),
        ("16A34A", "Green"),
        ("2563EB", "Blue"),
        ("F59E0B", "Orange"),
        ("7C3AED", "Purple")
    ];

    private void InitializeContentEditPanel()
    {
        BlockFontSizeCombo.Items.Clear();
        foreach (var size in new[] { 8, 10, 12, 14, 16, 18, 20, 24, 28, 36 })
            BlockFontSizeCombo.Items.Add(size);

        BlockColorPanel.Children.Clear();
        foreach (var (hex, name) in BlockColors)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(ParseHexColor(hex)),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = name
            };
            swatch.MouseLeftButtonDown += BlockColorSwatch_Click;
            BlockColorPanel.Children.Add(swatch);
        }

        var customColor = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)FindResource("SurfaceElevatedBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = _loc.Get("EditorPickColor")
        };
        customColor.Child = new TextBlock
        {
            Text = "+",
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        };
        customColor.MouseLeftButtonDown += PickCustomColor_Click;
        BlockColorPanel.Children.Add(customColor);
    }

    private void ApplyContentEditLocalization()
    {
        ContentEditTitle.Text = _loc.Get("EditorContentEditTitle");
        BlockTextLabel.Text = _loc.Get("EditorBlockText");
        BlockFontSizeLabel.Text = _loc.Get("EditorBlockFontSize");
        BlockColorLabel.Text = _loc.Get("EditorBlockColor");
        ContentEditHint.Text = _loc.Get("EditorContentEditHint");
        DeleteBlockButton.Content = _loc.Get("EditorDeleteSelection");
    }

    private void ReloadImageElementsForPage(int pageIndex)
    {
        if (_document.WorkingCopyPath is null)
            return;

        _imageElements.RemoveAll(i => i.PageIndex == pageIndex);
        var (pageW, pageH) = _document.GetPageSize(pageIndex);
        var images = _imageExtract.ExtractPageImages(_document.WorkingCopyPath, pageIndex, pageH);
        var pixels = _render.GetPagePixels(_document.WorkingCopyPath, pageIndex, (int)(960 * _zoom));
        if (pixels is not null)
        {
            foreach (var image in images)
            {
                image.BackgroundColorHex = PageBackgroundSampler.SampleFromPdfRect(
                    pixels.Value.Bytes,
                    pixels.Value.Width,
                    pixels.Value.Height,
                    pageW,
                    pageH,
                    image.OriginalX,
                    image.OriginalY,
                    image.OriginalWidth,
                    image.OriginalHeight);
            }
        }

        _imageElements.AddRange(images);
    }

    private void ReloadAllPageContent()
    {
        _textBlocks.Clear();
        _imageElements.Clear();
        if (_document.WorkingCopyPath is null)
            return;

        for (var i = 0; i < _document.PageCount; i++)
        {
            ReloadTextBlocksForPage(i);
            ReloadImageElementsForPage(i);
        }
    }

    private void RefreshTextBlockBackgroundColor(PdfTextBlockItem block)
    {
        if (_document.WorkingCopyPath is null)
            return;

        var (pageW, pageH) = _document.GetPageSize(block.PageIndex);
        var renderWidth = (int)(960 * _zoom);
        var pixels = _render.GetPagePixels(_document.WorkingCopyPath, block.PageIndex, renderWidth);
        if (pixels is null)
            return;

        block.BackgroundColorHex = PageBackgroundSampler.SampleFromPdfRect(
            pixels.Value.Bytes,
            pixels.Value.Width,
            pixels.Value.Height,
            pageW,
            pageH,
            block.OriginalX,
            block.OriginalY,
            block.OriginalWidth,
            block.OriginalHeight);

        var (coverX, coverY, coverW, coverH) = TextBlockBoundsHelper.GetCoverBounds(block);
        block.CoverSegments = PageBackgroundSampler.SampleCoverSegmentsFromPdfRect(
            pixels.Value.Bytes,
            pixels.Value.Width,
            pixels.Value.Height,
            pageW,
            pageH,
            coverX,
            coverY,
            coverW,
            coverH).ToList();
    }

    private void RefreshImageBackgroundColor(PdfImageElementItem image)
    {
        if (_document.WorkingCopyPath is null)
            return;

        var (pageW, pageH) = _document.GetPageSize(image.PageIndex);
        var renderWidth = (int)(960 * _zoom);
        var pixels = _render.GetPagePixels(_document.WorkingCopyPath, image.PageIndex, renderWidth);
        if (pixels is null)
            return;

        image.BackgroundColorHex = PageBackgroundSampler.SampleFromPdfRect(
            pixels.Value.Bytes,
            pixels.Value.Width,
            pixels.Value.Height,
            pageW,
            pageH,
            image.OriginalX,
            image.OriginalY,
            image.OriginalWidth,
            image.OriginalHeight);
    }

    private double PdfFontSizeToScreen(double pdfFontSize)
    {
        var (_, scaleY) = GetScreenScale();
        return pdfFontSize / Math.Max(1, scaleY);
    }

    private double ScreenFontSizeToPdf(double screenFontSize)
    {
        var (_, scaleY) = GetScreenScale();
        return screenFontSize * scaleY;
    }

    private static double GetBlockPreviewFontSize(PdfTextBlockItem block, double scaleY) =>
        Math.Max(8, block.FontSize / Math.Max(1, scaleY));

    private static List<PdfImageElementItem> CloneImages(IEnumerable<PdfImageElementItem> source) =>
        source.Select(i => i.Clone()).ToList();

    private List<PdfImageElementItem> CloneImages() => CloneImages(_imageElements);

    private void RestoreImages(IReadOnlyList<PdfImageElementItem> snapshot)
    {
        _imageElements.Clear();
        _imageElements.AddRange(snapshot.Select(i => i.Clone()));
        _selectedImageElement = _imageElements.FirstOrDefault(i => i.Id == _selectedImageElement?.Id);
    }

    private void RegisterContentUndoPoint()
    {
        _textBlockUndo.Push(CloneTextBlocks());
        _imageUndo.Push(CloneImages());
        _undoKinds.Push(EditorUndoKind.TextBlock);
        _textBlockRedo.Clear();
        _imageRedo.Clear();
        _redoKinds.Clear();
    }

    private void DrawPendingContentPreview()
    {
        var pageBlocks = _textBlocks.Where(b => b.PageIndex == _selectedPageIndex).ToList();
        var pageImages = _imageElements.Where(i => i.PageIndex == _selectedPageIndex).ToList();

        foreach (var block in pageBlocks)
        {
            if (block.IsDeleted || block.IsModified)
            {
                foreach (var segment in GetTextCoverSegments(block))
                {
                    DrawOverlay.Children.Add(CreateContentCoverVisual(
                        CoverSegmentToScreenRect(segment),
                        block.OriginalFontSize,
                        segment.ColorHex));
                }
            }
        }

        foreach (var image in pageImages)
        {
            if (image.IsDeleted || image.IsModified)
                DrawOverlay.Children.Add(CreateContentCoverVisual(
                    ImageOriginalPdfToScreenRect(image),
                    12,
                    image.BackgroundColorHex));
        }

        foreach (var block in pageBlocks)
        {
            if (!block.IsDeleted && block.IsModified)
                DrawOverlay.Children.Add(CreateTextBlockPreviewVisual(block));
        }

        foreach (var image in pageImages)
        {
            if (!image.IsDeleted && image.IsModified)
            {
                var preview = CreateImagePreviewVisual(image);
                if (preview is not null)
                    DrawOverlay.Children.Add(preview);
            }
        }
    }

    private void DrawContentSelectionChrome()
    {
        foreach (var block in _textBlocks.Where(b => b.PageIndex == _selectedPageIndex && !b.IsDeleted))
        {
            if (block.IsModified)
                DrawOverlay.Children.Add(CreateTextBlockHitTarget(block));
            else
                DrawOverlay.Children.Add(CreateTextBlockVisual(block));
        }

        foreach (var image in _imageElements.Where(i => i.PageIndex == _selectedPageIndex && !i.IsDeleted))
        {
            if (image.IsModified)
                DrawOverlay.Children.Add(CreateImageHitTarget(image));
            else
                DrawOverlay.Children.Add(CreateImageElementVisual(image));
        }

        if (_selectedTextBlock is { PageIndex: var tp } tb && tp == _selectedPageIndex && !tb.IsDeleted)
        {
            var rect = TextBlockPdfToScreenRect(tb);
            DrawOverlay.Children.Add(CreateTextBlockSelectionVisual(tb));
            AddSelectionResizeHandles(rect);
            AddRotationHandle(rect);
            AddFloatingDeleteButton(rect);
        }

        if (_selectedImageElement is { PageIndex: var ip } img && ip == _selectedPageIndex && !img.IsDeleted)
        {
            var rect = ImagePdfToScreenRect(img);
            DrawOverlay.Children.Add(CreateImageSelectionVisual(img));
            AddSelectionResizeHandles(rect);
            AddRotationHandle(rect);
            AddFloatingDeleteButton(rect);
        }
    }

    private IReadOnlyList<TextCoverSegment> GetTextCoverSegments(PdfTextBlockItem block)
    {
        if (block.CoverSegments.Count > 0)
            return block.CoverSegments;

        var (coverX, coverY, coverW, coverH) = TextBlockBoundsHelper.GetCoverBounds(block);
        return
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
    }

    private Rect CoverSegmentToScreenRect(TextCoverSegment segment)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(segment.X / scaleX, segment.Y / scaleY, segment.Width / scaleX, segment.Height / scaleY);
    }

    private Rect GetTextCoverScreenRect(PdfTextBlockItem block)
    {
        var (scaleX, scaleY) = GetScreenScale();
        var (x, y, width, height) = TextBlockBoundsHelper.GetCoverBounds(block);
        return new Rect(x / scaleX, y / scaleY, width / scaleX, height / scaleY);
    }

    private UIElement CreateContentCoverVisual(Rect screenRect, double referenceFontSize, string backgroundColorHex)
    {
        var (_, scaleY) = GetScreenScale();
        var pad = Math.Max(2, referenceFontSize / Math.Max(1, scaleY) * 0.08);
        var shape = new Rectangle
        {
            Width = Math.Max(1, screenRect.Width + pad * 2),
            Height = Math.Max(1, screenRect.Height + pad * 2),
            Fill = new SolidColorBrush(ParseHexColor(backgroundColorHex)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(shape, screenRect.Left - pad);
        Canvas.SetTop(shape, screenRect.Top - pad);
        Panel.SetZIndex(shape, 0);
        return shape;
    }

    private UIElement CreateTextBlockHitTarget(PdfTextBlockItem block)
    {
        var rect = TextBlockPdfToScreenRect(block);
        var shape = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Fill = Brushes.Transparent,
            Tag = block,
            Cursor = Cursors.SizeAll
        };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        Panel.SetZIndex(shape, 20);
        shape.MouseLeftButtonDown += TextBlockVisual_MouseLeftButtonDown;
        return shape;
    }

    private UIElement CreateImageHitTarget(PdfImageElementItem image)
    {
        var rect = ImagePdfToScreenRect(image);
        var shape = new Rectangle
        {
            Width = Math.Max(8, rect.Width),
            Height = Math.Max(8, rect.Height),
            Fill = Brushes.Transparent,
            Tag = image,
            Cursor = Cursors.SizeAll
        };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        Panel.SetZIndex(shape, 20);
        shape.MouseLeftButtonDown += ImageElementVisual_MouseLeftButtonDown;
        return shape;
    }

    private UIElement CreateTextBlockPreviewVisual(PdfTextBlockItem block)
    {
        var rect = TextBlockPdfToScreenRect(block);
        var (_, scaleY) = GetScreenScale();
        var preview = new TextBlock
        {
            Text = block.CurrentText.Replace('\n', ' '),
            TextWrapping = TextWrapping.NoWrap,
            Foreground = new SolidColorBrush(ParseHexColor(block.ColorHex)),
            FontSize = GetBlockPreviewFontSize(block, scaleY),
            FontWeight = block.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = block.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = TextAlignment.Left,
            Width = Math.Max(8, rect.Width),
            IsHitTestVisible = false
        };
        if (Math.Abs(block.RotationDegrees) > 0.01)
        {
            preview.RenderTransformOrigin = new Point(0, 0);
            preview.RenderTransform = new RotateTransform(block.RotationDegrees);
        }

        Canvas.SetLeft(preview, rect.Left);
        Canvas.SetTop(preview, rect.Top);
        Panel.SetZIndex(preview, 10);
        return preview;
    }

    private UIElement? CreateImagePreviewVisual(PdfImageElementItem image)
    {
        if (image.ImageBytes is null || image.ImageBytes.Length == 0)
            return null;

        try
        {
            var rect = ImagePdfToScreenRect(image);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(image.ImageBytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var preview = new Image
            {
                Source = bitmap,
                Width = Math.Max(8, rect.Width),
                Height = Math.Max(8, rect.Height),
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false
            };
            if (Math.Abs(image.RotationDegrees) > 0.01)
            {
                preview.RenderTransformOrigin = new Point(0.5, 0.5);
                preview.RenderTransform = new RotateTransform(image.RotationDegrees);
            }

            Canvas.SetLeft(preview, rect.Left);
            Canvas.SetTop(preview, rect.Top);
            Panel.SetZIndex(preview, 10);
            return preview;
        }
        catch
        {
            return null;
        }
    }

    private Rect TextBlockOriginalPdfToScreenRect(PdfTextBlockItem block)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(
            block.OriginalX / scaleX,
            block.OriginalY / scaleY,
            block.OriginalWidth / scaleX,
            block.OriginalHeight / scaleY);
    }

    private Rect ImageOriginalPdfToScreenRect(PdfImageElementItem image)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(
            image.OriginalX / scaleX,
            image.OriginalY / scaleY,
            image.OriginalWidth / scaleX,
            image.OriginalHeight / scaleY);
    }

    private UIElement CreateImageElementVisual(PdfImageElementItem image)
    {
        var rect = ImagePdfToScreenRect(image);
        var container = new Grid
        {
            Width = Math.Max(8, rect.Width),
            Height = Math.Max(8, rect.Height),
            Tag = image,
            Cursor = Cursors.SizeAll
        };

        if (image.ImageBytes is { Length: > 0 })
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(image.ImageBytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                container.Children.Add(new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    IsHitTestVisible = false
                });
            }
            catch
            {
                // keep border-only fallback
            }
        }

        container.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 245, 158, 11)),
            BorderThickness = new Thickness(1.5),
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(2),
            IsHitTestVisible = false
        });

        Canvas.SetLeft(container, rect.Left);
        Canvas.SetTop(container, rect.Top);
        container.MouseLeftButtonDown += ImageElementVisual_MouseLeftButtonDown;
        return container;
    }

    private Rectangle CreateImageSelectionVisual(PdfImageElementItem image)
    {
        var rect = ImagePdfToScreenRect(image);
        var selection = new Rectangle
        {
            Width = Math.Max(8, rect.Width),
            Height = Math.Max(8, rect.Height),
            Stroke = new SolidColorBrush(Color.FromArgb(230, 245, 158, 11)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(selection, rect.Left);
        Canvas.SetTop(selection, rect.Top);
        return selection;
    }

    private Rect ImagePdfToScreenRect(PdfImageElementItem image)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(image.X / scaleX, image.Y / scaleY, image.Width / scaleX, image.Height / scaleY);
    }

    private void AddSelectionResizeHandles(Rect rect)
    {
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var points = new (ContentResizeHandle handle, double x, double y)[]
        {
            (ContentResizeHandle.TopLeft, rect.Left, rect.Top),
            (ContentResizeHandle.Top, rect.Left + rect.Width / 2, rect.Top),
            (ContentResizeHandle.TopRight, rect.Right, rect.Top),
            (ContentResizeHandle.Right, rect.Right, rect.Top + rect.Height / 2),
            (ContentResizeHandle.BottomRight, rect.Right, rect.Bottom),
            (ContentResizeHandle.Bottom, rect.Left + rect.Width / 2, rect.Bottom),
            (ContentResizeHandle.BottomLeft, rect.Left, rect.Bottom),
            (ContentResizeHandle.Left, rect.Left, rect.Top + rect.Height / 2)
        };

        foreach (var (handle, x, y) in points)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = accent,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Tag = handle,
                Cursor = GetResizeCursor(handle)
            };
            Canvas.SetLeft(dot, x - 5);
            Canvas.SetTop(dot, y - 5);
            dot.MouseLeftButtonDown += ResizeHandle_MouseLeftButtonDown;
            DrawOverlay.Children.Add(dot);
        }
    }

    private static Cursor GetResizeCursor(ContentResizeHandle handle) => handle switch
    {
        ContentResizeHandle.TopLeft or ContentResizeHandle.BottomRight => Cursors.SizeNWSE,
        ContentResizeHandle.TopRight or ContentResizeHandle.BottomLeft => Cursors.SizeNESW,
        ContentResizeHandle.Top or ContentResizeHandle.Bottom => Cursors.SizeNS,
        ContentResizeHandle.Left or ContentResizeHandle.Right => Cursors.SizeWE,
        _ => Cursors.Arrow
    };

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.EditText || sender is not Ellipse { Tag: ContentResizeHandle handle })
            return;

        RegisterContentUndoPoint();
        _isResizingContent = true;
        _activeResizeHandle = handle;
        _contentDragStartScreen = e.GetPosition(DrawOverlay);
        _contentDragStartX = _selectedTextBlock?.X ?? _selectedImageElement?.X ?? 0;
        _contentDragStartY = _selectedTextBlock?.Y ?? _selectedImageElement?.Y ?? 0;
        _resizeStartW = _selectedTextBlock?.Width ?? _selectedImageElement?.Width ?? 0;
        _resizeStartH = _selectedTextBlock?.Height ?? _selectedImageElement?.Height ?? 0;
        _resizeStartFontSize = _selectedTextBlock?.FontSize ?? 12;
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void HandleContentResizeMove(Point pos)
    {
        if (!_isResizingContent)
            return;

        var delta = pos - _contentDragStartScreen;
        var (scaleX, scaleY) = GetScreenScale();
        var dx = delta.X * scaleX;
        var dy = delta.Y * scaleY;
        const double minW = 12;
        const double minH = 12;

        if (_selectedTextBlock is not null)
            ApplyResize(_selectedTextBlock, dx, dy, minW, minH);
        else if (_selectedImageElement is not null)
            ApplyResize(_selectedImageElement, dx, dy, minW, minH);

        RefreshAnnotationOverlay();
    }

    private void ApplyResize(PdfTextBlockItem block, double dx, double dy, double minW, double minH)
    {
        var x = _contentDragStartX;
        var y = _contentDragStartY;
        var w = _resizeStartW;
        var h = _resizeStartH;

        switch (_activeResizeHandle)
        {
            case ContentResizeHandle.TopLeft:
                x = _contentDragStartX + dx;
                y = _contentDragStartY + dy;
                w = _resizeStartW - dx;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.Top:
                y = _contentDragStartY + dy;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.TopRight:
                y = _contentDragStartY + dy;
                w = _resizeStartW + dx;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.Right:
                w = _resizeStartW + dx;
                break;
            case ContentResizeHandle.BottomRight:
                w = _resizeStartW + dx;
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.Bottom:
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.BottomLeft:
                x = _contentDragStartX + dx;
                w = _resizeStartW - dx;
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.Left:
                x = _contentDragStartX + dx;
                w = _resizeStartW - dx;
                break;
        }

        if (w < minW)
        {
            if (_activeResizeHandle is ContentResizeHandle.Left or ContentResizeHandle.TopLeft or ContentResizeHandle.BottomLeft)
                x -= minW - w;
            w = minW;
        }

        if (h < minH)
        {
            if (_activeResizeHandle is ContentResizeHandle.Top or ContentResizeHandle.TopLeft or ContentResizeHandle.TopRight)
                y -= minH - h;
            h = minH;
        }

        block.X = x;
        block.Y = y;
        block.Width = w;
        block.Height = h;
        if (_resizeStartH > 0)
            block.FontSize = Math.Max(6, _resizeStartFontSize * (h / _resizeStartH));
    }

    private void ApplyResize(PdfImageElementItem image, double dx, double dy, double minW, double minH)
    {
        var x = _contentDragStartX;
        var y = _contentDragStartY;
        var w = _resizeStartW;
        var h = _resizeStartH;

        switch (_activeResizeHandle)
        {
            case ContentResizeHandle.TopLeft:
                x = _contentDragStartX + dx;
                y = _contentDragStartY + dy;
                w = _resizeStartW - dx;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.Top:
                y = _contentDragStartY + dy;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.TopRight:
                y = _contentDragStartY + dy;
                w = _resizeStartW + dx;
                h = _resizeStartH - dy;
                break;
            case ContentResizeHandle.Right:
                w = _resizeStartW + dx;
                break;
            case ContentResizeHandle.BottomRight:
                w = _resizeStartW + dx;
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.Bottom:
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.BottomLeft:
                x = _contentDragStartX + dx;
                w = _resizeStartW - dx;
                h = _resizeStartH + dy;
                break;
            case ContentResizeHandle.Left:
                x = _contentDragStartX + dx;
                w = _resizeStartW - dx;
                break;
        }

        if (w < minW)
        {
            if (_activeResizeHandle is ContentResizeHandle.Left or ContentResizeHandle.TopLeft or ContentResizeHandle.BottomLeft)
                x -= minW - w;
            w = minW;
        }

        if (h < minH)
        {
            if (_activeResizeHandle is ContentResizeHandle.Top or ContentResizeHandle.TopLeft or ContentResizeHandle.TopRight)
                y -= minH - h;
            h = minH;
        }

        image.X = x;
        image.Y = y;
        image.Width = w;
        image.Height = h;
    }

    private void EndContentResize()
    {
        if (!_isResizingContent)
            return;

        DrawOverlay.ReleaseMouseCapture();
        _isResizingContent = false;
        _activeResizeHandle = ContentResizeHandle.None;
        UpdateUndoRedoButtons();
    }

    private void AddFloatingDeleteButton(Rect rect)
    {
        var btn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
            ToolTip = _loc.Get("EditorDeleteSelection")
        };
        if (TryFindResource("SecondaryButton") is Style style)
            btn.Style = style;
        btn.Click += (_, _) => DeleteCurrentContentSelection();
        Canvas.SetLeft(btn, Math.Max(0, rect.Right - 30));
        Canvas.SetTop(btn, Math.Max(0, rect.Top - 34));
        DrawOverlay.Children.Add(btn);
    }

    private void SelectTextBlock(PdfTextBlockItem block)
    {
        _selectedTextBlock = block;
        _selectedImageElement = null;
        HideInlineTextEditor();
        SyncFormatPanelFromSelection();
        RefreshAnnotationOverlay();
        UpdateDocumentUiState();
    }

    private void SelectImageElement(PdfImageElementItem image)
    {
        _selectedImageElement = image;
        _selectedTextBlock = null;
        HideInlineTextEditor();
        SyncFormatPanelFromSelection();
        RefreshAnnotationOverlay();
        UpdateDocumentUiState();
    }

    private void ClearContentSelection()
    {
        _selectedTextBlock = null;
        _selectedImageElement = null;
        ContentEditPanel.Visibility = Visibility.Collapsed;
    }

    private void SyncFormatPanelFromSelection()
    {
        _suppressFormatPanelEvents = true;
        if (_selectedTextBlock is null && _selectedImageElement is null)
        {
            ContentEditPanel.Visibility = Visibility.Collapsed;
            _suppressFormatPanelEvents = false;
            return;
        }

        ContentEditPanel.Visibility = Visibility.Visible;
        var isText = _selectedTextBlock is not null;
        BlockTextLabel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockTextBox.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockFontSizeLabel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockFontSizeCombo.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockColorLabel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockColorPanel.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockBoldToggle.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        BlockItalicToggle.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;

        if (_selectedTextBlock is { } block)
        {
            BlockTextBox.Text = block.CurrentText;
            var screenFontSize = (int)Math.Round(PdfFontSizeToScreen(block.FontSize));
            BlockFontSizeCombo.SelectedItem = BlockFontSizeCombo.Items.Cast<object>()
                .OrderBy(i => Math.Abs(Convert.ToInt32(i) - screenFontSize))
                .FirstOrDefault();
            BlockBoldToggle.IsChecked = block.Bold;
            BlockItalicToggle.IsChecked = block.Italic;
            HighlightColorSwatch(block.ColorHex);
            ContentEditHint.Text = _loc.Get("EditorContentEditHint");
        }
        else
        {
            ContentEditHint.Text = _loc.Get("EditorImageSelected");
        }

        _suppressFormatPanelEvents = false;
    }

    private void HighlightColorSwatch(string hex)
    {
        hex = hex.Trim().TrimStart('#').ToUpperInvariant();
        foreach (Border swatch in BlockColorPanel.Children.OfType<Border>())
        {
            var match = string.Equals(swatch.Tag as string, hex, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = match
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrush");
            swatch.BorderThickness = match ? new Thickness(2) : new Thickness(1);
        }
    }

    private void ApplyFormatPanelToSelection()
    {
        if (_suppressFormatPanelEvents || _selectedTextBlock is null)
            return;

        RegisterContentUndoPoint();
        _selectedTextBlock.ReplacementText = BlockTextBox.Text;
        _selectedTextBlock.Bold = BlockBoldToggle.IsChecked == true;
        _selectedTextBlock.Italic = BlockItalicToggle.IsChecked == true;
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
    }

    private void ApplyFontSizeFromPanel()
    {
        if (_suppressFormatPanelEvents || _selectedTextBlock is null || BlockFontSizeCombo.SelectedItem is null)
            return;

        RegisterContentUndoPoint();
        var oldSize = _selectedTextBlock.FontSize;
        var newSize = ScreenFontSizeToPdf(Convert.ToDouble(BlockFontSizeCombo.SelectedItem));
        if (oldSize > 0.1)
            _selectedTextBlock.Height *= newSize / oldSize;
        _selectedTextBlock.FontSize = newSize;
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
    }

    private void BlockTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFormatPanelEvents || _selectedTextBlock is null)
            return;

        _selectedTextBlock.ReplacementText = BlockTextBox.Text;
        RefreshAnnotationOverlay();
    }

    private void BlockFormat_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressFormatPanelEvents || _selectedTextBlock is null)
            return;

        if (sender == BlockFontSizeCombo)
        {
            ApplyFontSizeFromPanel();
            return;
        }

        ApplyFormatPanelToSelection();
    }

    private void BlockColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedTextBlock is null || sender is not Border { Tag: string hex })
            return;

        RegisterContentUndoPoint();
        _selectedTextBlock.ColorHex = hex;
        HighlightColorSwatch(hex);
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
    }

    private void DeleteBlockButton_Click(object sender, RoutedEventArgs e) => DeleteCurrentContentSelection();

    private void DeleteCurrentContentSelection()
    {
        if (_activeTool != PdfEditorTool.EditText)
            return;

        if (_selectedTextBlock is not null)
        {
            RegisterContentUndoPoint();
            _selectedTextBlock.IsDeleted = true;
            _selectedTextBlock.ReplacementText = null;
            ClearContentSelection();
            RefreshAnnotationOverlay();
            UpdateUndoRedoButtons();
            return;
        }

        if (_selectedImageElement is not null)
        {
            RegisterContentUndoPoint();
            _selectedImageElement.IsDeleted = true;
            ClearContentSelection();
            RefreshAnnotationOverlay();
            UpdateUndoRedoButtons();
        }
    }

    private PdfTextBlockItem? HitTestTextBlock(Point screenPoint)
    {
        for (var i = _textBlocks.Count - 1; i >= 0; i--)
        {
            var block = _textBlocks[i];
            if (block.PageIndex != _selectedPageIndex || block.IsDeleted)
                continue;

            if (TextBlockPdfToScreenRect(block).Contains(screenPoint))
                return block;
        }

        return null;
    }

    private PdfImageElementItem? HitTestImageElement(Point screenPoint)
    {
        for (var i = _imageElements.Count - 1; i >= 0; i--)
        {
            var image = _imageElements[i];
            if (image.PageIndex != _selectedPageIndex || image.IsDeleted)
                continue;

            if (ImagePdfToScreenRect(image).Contains(screenPoint))
                return image;
        }

        return null;
    }

    private void TextBlockVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.EditText || sender is not FrameworkElement { Tag: PdfTextBlockItem block })
            return;

        if (e.ClickCount >= 2)
        {
            SelectTextBlock(block);
            ShowInlineTextEditor(block);
            e.Handled = true;
            return;
        }

        SelectTextBlock(block);
        RefreshTextBlockBackgroundColor(block);
        _isDraggingContent = true;
        _contentDragStartScreen = e.GetPosition(DrawOverlay);
        _contentDragStartX = block.X;
        _contentDragStartY = block.Y;
        RegisterContentUndoPoint();
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void ImageElementVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.EditText || sender is not FrameworkElement { Tag: PdfImageElementItem image })
            return;

        SelectImageElement(image);
        RefreshImageBackgroundColor(image);
        _isDraggingContent = true;
        _contentDragStartScreen = e.GetPosition(DrawOverlay);
        _contentDragStartX = image.X;
        _contentDragStartY = image.Y;
        RegisterContentUndoPoint();
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void HandleContentDragMove(Point pos)
    {
        if (_isRotatingContent)
        {
            HandleContentRotationMove(pos);
            return;
        }

        if (_isResizingContent)
        {
            HandleContentResizeMove(pos);
            return;
        }

        if (!_isDraggingContent)
            return;

        var delta = pos - _contentDragStartScreen;
        var (scaleX, scaleY) = GetScreenScale();

        if (_selectedTextBlock is not null)
        {
            _selectedTextBlock.X = _contentDragStartX + delta.X * scaleX;
            _selectedTextBlock.Y = _contentDragStartY + delta.Y * scaleY;
        }
        else if (_selectedImageElement is not null)
        {
            _selectedImageElement.X = _contentDragStartX + delta.X * scaleX;
            _selectedImageElement.Y = _contentDragStartY + delta.Y * scaleY;
        }

        RefreshAnnotationOverlay();
    }

    private void EndContentDrag()
    {
        if (_isRotatingContent)
        {
            DrawOverlay.ReleaseMouseCapture();
            _isRotatingContent = false;
            UpdateUndoRedoButtons();
            return;
        }

        if (_isResizingContent)
        {
            EndContentResize();
            return;
        }

        if (!_isDraggingContent)
            return;

        DrawOverlay.ReleaseMouseCapture();
        _isDraggingContent = false;
        UpdateUndoRedoButtons();
    }

    private void PickCustomColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedTextBlock is null)
            return;

        var owner = OwnerWindow;
        if (owner is null)
            return;

        var picked = ColorPickerDialog.Pick(owner, ParseHexColor(_selectedTextBlock.ColorHex), _loc);
        if (picked is null)
            return;

        RegisterContentUndoPoint();
        _selectedTextBlock.ColorHex = $"{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        HighlightColorSwatch(_selectedTextBlock.ColorHex);
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
        e.Handled = true;
    }

    private void AddRotationHandle(Rect rect)
    {
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var rotateY = rect.Top - 28;
        DrawOverlay.Children.Add(new Line
        {
            X1 = rect.Left + rect.Width / 2,
            Y1 = rect.Top,
            X2 = rect.Left + rect.Width / 2,
            Y2 = rotateY + 10,
            Stroke = accent,
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        });

        var rotateDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = accent,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Cursor = Cursors.Hand,
            ToolTip = _loc.Get("EditorRotateSelection")
        };
        Canvas.SetLeft(rotateDot, rect.Left + rect.Width / 2 - 6);
        Canvas.SetTop(rotateDot, rotateY);
        rotateDot.MouseLeftButtonDown += RotationHandle_MouseLeftButtonDown;
        DrawOverlay.Children.Add(rotateDot);
    }

    private void RotationHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.EditText)
            return;

        RegisterContentUndoPoint();
        _isRotatingContent = true;
        _contentDragStartScreen = e.GetPosition(DrawOverlay);
        var rect = _selectedTextBlock is not null
            ? TextBlockPdfToScreenRect(_selectedTextBlock)
            : _selectedImageElement is not null
                ? ImagePdfToScreenRect(_selectedImageElement)
                : Rect.Empty;
        _rotateCenterScreen = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        _rotateStartDegrees = _selectedTextBlock?.RotationDegrees ?? _selectedImageElement?.RotationDegrees ?? 0;
        _rotateStartAngle = Math.Atan2(
            _contentDragStartScreen.Y - _rotateCenterScreen.Y,
            _contentDragStartScreen.X - _rotateCenterScreen.X) * 180 / Math.PI;
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void HandleContentRotationMove(Point pos)
    {
        if (!_isRotatingContent)
            return;

        var angle = Math.Atan2(pos.Y - _rotateCenterScreen.Y, pos.X - _rotateCenterScreen.X) * 180 / Math.PI;
        var rotation = _rotateStartDegrees + (angle - _rotateStartAngle);
        if (_selectedTextBlock is not null)
            _selectedTextBlock.RotationDegrees = rotation;
        else if (_selectedImageElement is not null)
            _selectedImageElement.RotationDegrees = rotation;

        RefreshAnnotationOverlay();
    }
}
