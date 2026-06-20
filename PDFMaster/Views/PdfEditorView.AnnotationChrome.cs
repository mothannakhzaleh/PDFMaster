using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PDFMaster.Models;

namespace PDFMaster.Views;

public partial class PdfEditorView
{
    private bool _isResizingAnnotation;
    private bool _isRotatingAnnotation;
    private AnnotationTransformHandle _activeAnnotationHandle = AnnotationTransformHandle.None;
    private Point _annTransformStartScreen;
    private double _annTransformStartX;
    private double _annTransformStartY;
    private double _annTransformStartW;
    private double _annTransformStartH;
    private double _annTransformStartFontSize;
    private double _annTransformStartRotation;
    private double _annTransformStartAngle;
    private Point _annTransformCenterScreen;

    private enum AnnotationTransformHandle
    {
        None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Rotate
    }

    private static bool SupportsTransformHandles(PdfEditorAnnotation annotation) =>
        annotation.Type is PdfEditorTool.Stamp or PdfEditorTool.Signature or PdfEditorTool.Image or PdfEditorTool.Text
            or PdfEditorTool.Rectangle or PdfEditorTool.Ellipse or PdfEditorTool.Line;

    private void AddAnnotationTransformHandles(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

        var handles = new (AnnotationTransformHandle handle, double x, double y)[]
        {
            (AnnotationTransformHandle.TopLeft, rect.Left, rect.Top),
            (AnnotationTransformHandle.Top, rect.Left + rect.Width / 2, rect.Top),
            (AnnotationTransformHandle.TopRight, rect.Right, rect.Top),
            (AnnotationTransformHandle.Right, rect.Right, rect.Top + rect.Height / 2),
            (AnnotationTransformHandle.BottomRight, rect.Right, rect.Bottom),
            (AnnotationTransformHandle.Bottom, rect.Left + rect.Width / 2, rect.Bottom),
            (AnnotationTransformHandle.BottomLeft, rect.Left, rect.Bottom),
            (AnnotationTransformHandle.Left, rect.Left, rect.Top + rect.Height / 2)
        };

        foreach (var (handle, x, y) in handles)
        {
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = accent,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Tag = handle,
                Cursor = GetAnnotationTransformCursor(handle)
            };
            Canvas.SetLeft(dot, x - 5);
            Canvas.SetTop(dot, y - 5);
            dot.MouseLeftButtonDown += AnnotationHandle_MouseLeftButtonDown;
            DrawOverlay.Children.Add(dot);
        }

        var rotateY = rect.Top - 28;
        var rotateLine = new Line
        {
            X1 = rect.Left + rect.Width / 2,
            Y1 = rect.Top,
            X2 = rect.Left + rect.Width / 2,
            Y2 = rotateY + 10,
            Stroke = accent,
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        DrawOverlay.Children.Add(rotateLine);

        var rotateDot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = accent,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Tag = AnnotationTransformHandle.Rotate,
            Cursor = Cursors.Hand,
            ToolTip = _loc.Get("EditorRotateSelection")
        };
        Canvas.SetLeft(rotateDot, rect.Left + rect.Width / 2 - 6);
        Canvas.SetTop(rotateDot, rotateY);
        rotateDot.MouseLeftButtonDown += AnnotationHandle_MouseLeftButtonDown;
        DrawOverlay.Children.Add(rotateDot);
    }

    private static Cursor GetAnnotationTransformCursor(AnnotationTransformHandle handle) => handle switch
    {
        AnnotationTransformHandle.TopLeft or AnnotationTransformHandle.BottomRight => Cursors.SizeNWSE,
        AnnotationTransformHandle.TopRight or AnnotationTransformHandle.BottomLeft => Cursors.SizeNESW,
        AnnotationTransformHandle.Top or AnnotationTransformHandle.Bottom => Cursors.SizeNS,
        AnnotationTransformHandle.Left or AnnotationTransformHandle.Right => Cursors.SizeWE,
        AnnotationTransformHandle.Rotate => Cursors.Hand,
        _ => Cursors.Arrow
    };

    private void AnnotationHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.Select || _selectedAnnotation is null ||
            sender is not Ellipse { Tag: AnnotationTransformHandle handle })
            return;

        RegisterAnnotationUndoPoint();
        _activeAnnotationHandle = handle;
        _annTransformStartScreen = e.GetPosition(DrawOverlay);
        _annTransformStartX = _selectedAnnotation.X;
        _annTransformStartY = _selectedAnnotation.Y;
        _annTransformStartW = _selectedAnnotation.Width;
        _annTransformStartH = _selectedAnnotation.Height;
        _annTransformStartFontSize = _selectedAnnotation.FontSize;
        _annTransformStartRotation = _selectedAnnotation.RotationDegrees;

        var rect = AnnotationPdfToScreenRect(_selectedAnnotation);
        _annTransformCenterScreen = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        _annTransformStartAngle = Math.Atan2(
            _annTransformStartScreen.Y - _annTransformCenterScreen.Y,
            _annTransformStartScreen.X - _annTransformCenterScreen.X) * 180 / Math.PI;

        _isRotatingAnnotation = handle == AnnotationTransformHandle.Rotate;
        _isResizingAnnotation = !_isRotatingAnnotation;
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void HandleAnnotationTransformMove(Point pos)
    {
        if (_selectedAnnotation is null)
            return;

        if (_isRotatingAnnotation)
        {
            var angle = Math.Atan2(pos.Y - _annTransformCenterScreen.Y, pos.X - _annTransformCenterScreen.X) * 180 / Math.PI;
            _selectedAnnotation.RotationDegrees = _annTransformStartRotation + (angle - _annTransformStartAngle);
            RefreshAnnotationOverlay();
            return;
        }

        if (!_isResizingAnnotation)
            return;

        var delta = pos - _annTransformStartScreen;
        var (scaleX, scaleY) = GetScreenScale();
        var dx = delta.X / scaleX;
        var dy = delta.Y / scaleY;
        ApplyAnnotationResize(_selectedAnnotation, dx, dy);
        RefreshAnnotationOverlay();
    }

    private void ApplyAnnotationResize(PdfEditorAnnotation annotation, double dx, double dy)
    {
        var x = _annTransformStartX;
        var y = _annTransformStartY;
        var w = _annTransformStartW;
        var h = _annTransformStartH;
        const double minW = 24;
        const double minH = 16;

        switch (_activeAnnotationHandle)
        {
            case AnnotationTransformHandle.TopLeft:
                x = _annTransformStartX + dx;
                y = _annTransformStartY + dy;
                w = _annTransformStartW - dx;
                h = _annTransformStartH - dy;
                break;
            case AnnotationTransformHandle.Top:
                y = _annTransformStartY + dy;
                h = _annTransformStartH - dy;
                break;
            case AnnotationTransformHandle.TopRight:
                y = _annTransformStartY + dy;
                w = _annTransformStartW + dx;
                h = _annTransformStartH - dy;
                break;
            case AnnotationTransformHandle.Right:
                w = _annTransformStartW + dx;
                break;
            case AnnotationTransformHandle.BottomRight:
                w = _annTransformStartW + dx;
                h = _annTransformStartH + dy;
                break;
            case AnnotationTransformHandle.Bottom:
                h = _annTransformStartH + dy;
                break;
            case AnnotationTransformHandle.BottomLeft:
                x = _annTransformStartX + dx;
                w = _annTransformStartW - dx;
                h = _annTransformStartH + dy;
                break;
            case AnnotationTransformHandle.Left:
                x = _annTransformStartX + dx;
                w = _annTransformStartW - dx;
                break;
        }

        if (w < minW)
        {
            if (_activeAnnotationHandle is AnnotationTransformHandle.Left or AnnotationTransformHandle.TopLeft or AnnotationTransformHandle.BottomLeft)
                x -= minW - w;
            w = minW;
        }

        if (h < minH)
        {
            if (_activeAnnotationHandle is AnnotationTransformHandle.Top or AnnotationTransformHandle.TopLeft or AnnotationTransformHandle.TopRight)
                y -= minH - h;
            h = minH;
        }

        annotation.X = x;
        annotation.Y = y;
        annotation.Width = w;
        annotation.Height = h;

        if (annotation.Type is PdfEditorTool.Stamp or PdfEditorTool.Text && _annTransformStartH > 0)
            annotation.FontSize = Math.Max(8, _annTransformStartFontSize * (h / _annTransformStartH));
    }

    private void EndAnnotationTransform()
    {
        if (!_isResizingAnnotation && !_isRotatingAnnotation)
            return;

        DrawOverlay.ReleaseMouseCapture();
        _isResizingAnnotation = false;
        _isRotatingAnnotation = false;
        _activeAnnotationHandle = AnnotationTransformHandle.None;
        UpdateUndoRedoButtons();
    }
}
