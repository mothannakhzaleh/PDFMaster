using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfEditorView
{
    private PdfFormFieldItem? _movingFormField;
    private bool _isResizingFormField;
    private bool _isRotatingFormField;
    private FormFieldTransformHandle _activeFormFieldHandle = FormFieldTransformHandle.None;
    private Point _formFieldTransformStartScreen;
    private double _formFieldTransformStartX;
    private double _formFieldTransformStartY;
    private double _formFieldTransformStartW;
    private double _formFieldTransformStartH;
    private double _formFieldTransformStartFontSize;
    private double _formFieldTransformStartRotation;
    private double _formFieldTransformStartAngle;
    private Point _formFieldTransformCenterScreen;

    private enum FormFieldTransformHandle
    {
        None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Rotate
    }

    private PdfFormFieldItem? GetSelectedFormField() =>
        FormFieldsList.SelectedItem as PdfFormFieldItem;

    private void BeginFormFieldMove(PdfFormFieldItem field, Point screenPos)
    {
        if (_activeTool != PdfEditorTool.Select || field.ReadOnly)
            return;

        FormFieldsList.SelectedItem = field;
        SyncFormValueEditors(field);
        _movingFormField = field;
        _moveStartScreen = screenPos;
        _moveStartX = field.X;
        _moveStartY = field.Y;
        RegisterDocumentUndoPoint();
        DrawOverlay.CaptureMouse();
        RefreshAnnotationOverlay();
    }

    private void AddFormFieldTransformHandles(PdfFormFieldItem field)
    {
        var rect = FormFieldPdfToScreenRect(field);
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

        var outline = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Stroke = accent,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 2],
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, rect.Left);
        Canvas.SetTop(outline, rect.Top);
        DrawOverlay.Children.Add(outline);

        var handles = new (FormFieldTransformHandle handle, double x, double y)[]
        {
            (FormFieldTransformHandle.TopLeft, rect.Left, rect.Top),
            (FormFieldTransformHandle.Top, rect.Left + rect.Width / 2, rect.Top),
            (FormFieldTransformHandle.TopRight, rect.Right, rect.Top),
            (FormFieldTransformHandle.Right, rect.Right, rect.Top + rect.Height / 2),
            (FormFieldTransformHandle.BottomRight, rect.Right, rect.Bottom),
            (FormFieldTransformHandle.Bottom, rect.Left + rect.Width / 2, rect.Bottom),
            (FormFieldTransformHandle.BottomLeft, rect.Left, rect.Bottom),
            (FormFieldTransformHandle.Left, rect.Left, rect.Top + rect.Height / 2)
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
                Cursor = GetFormFieldTransformCursor(handle)
            };
            Canvas.SetLeft(dot, x - 5);
            Canvas.SetTop(dot, y - 5);
            dot.MouseLeftButtonDown += FormFieldHandle_MouseLeftButtonDown;
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
            Tag = FormFieldTransformHandle.Rotate,
            Cursor = Cursors.Hand,
            ToolTip = _loc.Get("EditorRotateSelection")
        };
        Canvas.SetLeft(rotateDot, rect.Left + rect.Width / 2 - 6);
        Canvas.SetTop(rotateDot, rotateY);
        rotateDot.MouseLeftButtonDown += FormFieldHandle_MouseLeftButtonDown;
        DrawOverlay.Children.Add(rotateDot);
    }

    private static Cursor GetFormFieldTransformCursor(FormFieldTransformHandle handle) => handle switch
    {
        FormFieldTransformHandle.TopLeft or FormFieldTransformHandle.BottomRight => Cursors.SizeNWSE,
        FormFieldTransformHandle.TopRight or FormFieldTransformHandle.BottomLeft => Cursors.SizeNESW,
        FormFieldTransformHandle.Top or FormFieldTransformHandle.Bottom => Cursors.SizeNS,
        FormFieldTransformHandle.Left or FormFieldTransformHandle.Right => Cursors.SizeWE,
        FormFieldTransformHandle.Rotate => Cursors.Hand,
        _ => Cursors.Arrow
    };

    private void FormFieldHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.Select || GetSelectedFormField() is not { ReadOnly: false } field ||
            sender is not Ellipse { Tag: FormFieldTransformHandle handle })
            return;

        RegisterDocumentUndoPoint();
        _activeFormFieldHandle = handle;
        _formFieldTransformStartScreen = e.GetPosition(DrawOverlay);
        _formFieldTransformStartX = field.X;
        _formFieldTransformStartY = field.Y;
        _formFieldTransformStartW = field.Width;
        _formFieldTransformStartH = field.Height;
        _formFieldTransformStartFontSize = field.FontSize;
        _formFieldTransformStartRotation = field.RotationDegrees;

        var rect = FormFieldPdfToScreenRect(field);
        _formFieldTransformCenterScreen = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        _formFieldTransformStartAngle = Math.Atan2(
            _formFieldTransformStartScreen.Y - _formFieldTransformCenterScreen.Y,
            _formFieldTransformStartScreen.X - _formFieldTransformCenterScreen.X) * 180 / Math.PI;

        _isRotatingFormField = handle == FormFieldTransformHandle.Rotate;
        _isResizingFormField = !_isRotatingFormField;
        DrawOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void HandleFormFieldTransformMove(Point pos)
    {
        if (GetSelectedFormField() is not { ReadOnly: false } field)
            return;

        if (_isRotatingFormField)
        {
            var angle = Math.Atan2(pos.Y - _formFieldTransformCenterScreen.Y, pos.X - _formFieldTransformCenterScreen.X) * 180 / Math.PI;
            field.RotationDegrees = NormalizeRotation(_formFieldTransformStartRotation + (angle - _formFieldTransformStartAngle));
            RefreshAnnotationOverlay();
            return;
        }

        if (!_isResizingFormField)
            return;

        var delta = pos - _formFieldTransformStartScreen;
        var (scaleX, scaleY) = GetScreenScale();
        ApplyFormFieldResize(field, delta.X / scaleX, delta.Y / scaleY);
        RefreshAnnotationOverlay();
    }

    private static int NormalizeRotation(double degrees)
    {
        var normalized = ((int)Math.Round(degrees) % 360 + 360) % 360;
        return normalized switch
        {
            >= 45 and < 135 => 90,
            >= 135 and < 225 => 180,
            >= 225 and < 315 => 270,
            _ => 0
        };
    }

    private void ApplyFormFieldResize(PdfFormFieldItem field, double dx, double dy)
    {
        var x = _formFieldTransformStartX;
        var y = _formFieldTransformStartY;
        var w = _formFieldTransformStartW;
        var h = _formFieldTransformStartH;
        const double minW = 40;
        const double minH = 18;

        switch (_activeFormFieldHandle)
        {
            case FormFieldTransformHandle.TopLeft:
                x = _formFieldTransformStartX + dx;
                y = _formFieldTransformStartY + dy;
                w = _formFieldTransformStartW - dx;
                h = _formFieldTransformStartH - dy;
                break;
            case FormFieldTransformHandle.Top:
                y = _formFieldTransformStartY + dy;
                h = _formFieldTransformStartH - dy;
                break;
            case FormFieldTransformHandle.TopRight:
                y = _formFieldTransformStartY + dy;
                w = _formFieldTransformStartW + dx;
                h = _formFieldTransformStartH - dy;
                break;
            case FormFieldTransformHandle.Right:
                w = _formFieldTransformStartW + dx;
                break;
            case FormFieldTransformHandle.BottomRight:
                w = _formFieldTransformStartW + dx;
                h = _formFieldTransformStartH + dy;
                break;
            case FormFieldTransformHandle.Bottom:
                h = _formFieldTransformStartH + dy;
                break;
            case FormFieldTransformHandle.BottomLeft:
                x = _formFieldTransformStartX + dx;
                w = _formFieldTransformStartW - dx;
                h = _formFieldTransformStartH + dy;
                break;
            case FormFieldTransformHandle.Left:
                x = _formFieldTransformStartX + dx;
                w = _formFieldTransformStartW - dx;
                break;
        }

        if (w < minW)
        {
            if (_activeFormFieldHandle is FormFieldTransformHandle.Left or FormFieldTransformHandle.TopLeft or FormFieldTransformHandle.BottomLeft)
                x -= minW - w;
            w = minW;
        }

        if (h < minH)
        {
            if (_activeFormFieldHandle is FormFieldTransformHandle.Top or FormFieldTransformHandle.TopLeft or FormFieldTransformHandle.TopRight)
                y -= minH - h;
            h = minH;
        }

        field.X = x;
        field.Y = y;
        field.Width = w;
        field.Height = h;

        if (_formFieldTransformStartH > 0)
            field.FontSize = Math.Max(8, _formFieldTransformStartFontSize * (h / _formFieldTransformStartH));
    }

    private void EndFormFieldTransform()
    {
        if (_movingFormField is not null)
        {
            DrawOverlay.ReleaseMouseCapture();
            PersistFormFieldGeometry(_movingFormField);
            _movingFormField = null;
            return;
        }

        if (!_isResizingFormField && !_isRotatingFormField)
            return;

        DrawOverlay.ReleaseMouseCapture();
        if (GetSelectedFormField() is { ReadOnly: false } field)
            PersistFormFieldGeometry(field);

        _isResizingFormField = false;
        _isRotatingFormField = false;
        _activeFormFieldHandle = FormFieldTransformHandle.None;
        UpdateUndoRedoButtons();
    }

    private void PersistFormFieldGeometry(PdfFormFieldItem field)
    {
        try
        {
            _document.UpdateFormFieldBounds(field.Name, field.PageIndex, field.X, field.Y, field.Width, field.Height);
            _document.UpdateFormFieldRotation(field.Name, field.RotationDegrees);

            if (field.FieldType == "Text")
            {
                _document.UpdateTextFormField(field.Name, new TextFormFieldPromptResult(
                    field.Name,
                    field.Value,
                    field.ColorHex,
                    field.FontSize,
                    field.FontFamily,
                    field.Bold,
                    field.Italic,
                    field.RotationDegrees));
            }
            else if (field.IsComboField && field.Options.Count > 0)
            {
                _document.UpdateComboFormField(field.Name, new ComboFormFieldPromptResult(
                    field.Name,
                    field.Options,
                    field.Value,
                    field.ColorHex,
                    field.FontSize,
                    field.FontFamily,
                    field.Bold,
                    field.Italic,
                    field.RotationDegrees));
            }

            RefreshPreview();
            _setStatus?.Invoke(string.Format(_loc.Get("EditorFormApplied"), field.Name));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            ReloadFormFields();
            RefreshPreview();
        }
    }
}
