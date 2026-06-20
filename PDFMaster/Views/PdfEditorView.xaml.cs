using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PDFMaster.Models;
using PDFMaster.Services;

namespace PDFMaster.Views;

public partial class PdfEditorView : UserControl
{
    private enum EditorUndoKind
    {
        Document,
        Annotation,
        TextBlock
    }

    private readonly PdfDocumentService _document = new();
    private readonly PdfRenderService _render = new();
    private readonly PdfTextExtractionService _textExtract = new();
    private readonly ObservableCollection<PdfPageItem> _pages = [];
    private readonly ObservableCollection<PdfFormFieldItem> _formFields = [];
    private readonly List<PdfEditorAnnotation> _annotations = [];
    private readonly List<PdfTextBlockItem> _textBlocks = [];
    private readonly Stack<List<PdfEditorAnnotation>> _annotationUndo = [];
    private readonly Stack<List<PdfEditorAnnotation>> _annotationRedo = [];
    private readonly Stack<List<PdfTextBlockItem>> _textBlockUndo = [];
    private readonly Stack<List<PdfTextBlockItem>> _textBlockRedo = [];
    private readonly Stack<EditorUndoKind> _undoKinds = [];
    private readonly Stack<EditorUndoKind> _redoKinds = [];

    private SettingsService? _settings;
    private LocalizationService _loc = new();
    private Action<string>? _setStatus;
    private PdfEditorTool _activeTool = PdfEditorTool.Select;
    private int _selectedPageIndex = -1;
    private double _zoom = 1.0;
    private Point? _drawStart;
    private UIElement? _drawPreview;
    private TextFormFieldPromptResult? _pendingTextFormField;
    private ComboFormFieldPromptResult? _pendingComboFormField;
    private PdfPageItem? _draggedPage;
    private Point _dragStartPoint;
    private string? _signatureImagePath;
    private string? _pendingStampText;
    private string _pendingStampColorHex = "E11D48";
    private double _pendingStampFontSize = 18;
    private string _pendingStampFontFamily = "Segoe UI";
    private bool _pendingStampBold = true;
    private bool _pendingStampItalic;
    private string? _pendingAddText;
    private string _pendingAddTextColorHex = "111827";
    private double _pendingAddTextFontSize = 14;
    private string _pendingAddTextFontFamily = "Segoe UI";
    private bool _pendingAddTextBold;
    private bool _pendingAddTextItalic;
    private double _pendingAddTextRotationDegrees;
    private PdfEditorAnnotation? _selectedAnnotation;
    private PdfEditorAnnotation? _movingAnnotation;
    private bool _suppressFormValueEvents;
    private PdfTextBlockItem? _selectedTextBlock;
    private PdfTextBlockItem? _editingTextBlock;
    private bool _inlineEditorCommitOnLostFocus = true;
    private Point _moveStartScreen;
    private double _moveStartX;
    private double _moveStartY;

    public PdfEditorView()
    {
        InitializeComponent();
        PagesList.ItemsSource = _pages;
        FormFieldsList.ItemsSource = _formFields;
    }

    public void Initialize(SettingsService settings, LocalizationService loc, Action<string> setStatus)
    {
        _settings = settings;
        _loc = loc;
        _setStatus = setStatus;
        CompressOnSaveCheck.IsChecked = settings.Current.CompressOnSave;
        ApplyLocalization(loc);
        InitializeContentEditPanel();
        UpdateUndoRedoButtons();
        UpdateToolButtonStyles();
        SetActiveTool(PdfEditorTool.Select);
    }

    public void RefreshSettings(AppSettings settings) =>
        CompressOnSaveCheck.IsChecked = settings.CompressOnSave;

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        HintText.Text = loc.Get("EditorHint");
        OpenButton.Content = loc.Get("EditorOpen");
        NewPdfButton.Content = loc.Get("EditorNewPdf");
        CloseButton.Content = loc.Get("EditorClose");
        SaveButton.Content = loc.Get("EditorSave");
        SaveAsButton.Content = loc.Get("EditorSaveAs");
        UndoButton.Content = loc.Get("EditorUndo");
        RedoButton.Content = loc.Get("EditorRedo");
        SelectToolButton.Content = loc.Get("EditorToolSelect");
        EditTextToolButton.Content = loc.Get("EditorToolEditText");
        TextToolButton.Content = loc.Get("EditorToolText");
        ImageToolButton.Content = loc.Get("EditorToolImage");
        SignatureToolButton.Content = loc.Get("EditorToolSignature");
        StampToolButton.Content = loc.Get("EditorToolStamp");
        RectangleToolButton.Content = loc.Get("EditorToolRectangle");
        EllipseToolButton.Content = loc.Get("EditorToolEllipse");
        LineToolButton.Content = loc.Get("EditorToolLine");
        HighlightToolButton.Content = loc.Get("EditorToolHighlight");
        WhiteoutToolButton.Content = loc.Get("EditorToolWhiteout");
        DeleteSelectionButton.Content = loc.Get("EditorDeleteSelection");
        RotateLeftButton.Content = loc.Get("EditorRotateLeft");
        RotateRightButton.Content = loc.Get("EditorRotateRight");
        AddPageButton.Content = loc.Get("EditorAddPage");
        DeletePageButton.Content = loc.Get("EditorDeletePage");
        MoveUpButton.Content = loc.Get("EditorMoveUp");
        MoveDownButton.Content = loc.Get("EditorMoveDown");
        ZoomInButton.Content = loc.Get("EditorZoomIn");
        ZoomOutButton.Content = loc.Get("EditorZoomOut");
        CompressOnSaveCheck.Content = loc.Get("EditorCompressOnSave");
        PagesLabel.Text = loc.Get("EditorPages");
        PagesDragHint.Text = loc.Get("EditorPagesDragHint");
        EmptyText.Text = loc.Get("EditorNoDocument");
        FormFieldsLabel.Text = loc.Get("EditorFormFields");
        FormFieldsHint.Text = loc.Get("EditorNoFormFields");
        AddTextFieldButton.Content = loc.Get("EditorAddTextField");
        AddComboFieldButton.Content = loc.Get("EditorAddComboField");
        FormValueLabel.Text = loc.Get("EditorFormValue");
        ApplyFormValueButton.Content = loc.Get("EditorApplyFormValue");
        EditFormFieldButton.Content = loc.Get("EditorEditFormField");
        DeleteFormFieldButton.Content = loc.Get("EditorDeleteFormField");
        FormFieldEditMenuItem.Header = loc.Get("EditorEditFormField");
        FormFieldDeleteMenuItem.Header = loc.Get("EditorDeleteFormField");
        ApplyContentEditLocalization();
        UpdateDocumentUiState();
    }

    private Window? OwnerWindow => Window.GetWindow(this);

    private void PdfEditorView_Loaded(object sender, RoutedEventArgs e) => Focus();

    private void UpdateDocumentUiState()
    {
        var hasDoc = _document.WorkingCopyPath is not null;
        CloseButton.IsEnabled = hasDoc;
        SaveButton.IsEnabled = hasDoc;
        SaveAsButton.IsEnabled = hasDoc;
        DeleteSelectionButton.IsEnabled = hasDoc && (_selectedAnnotation is not null ||
            (_activeTool == PdfEditorTool.EditText && (_selectedTextBlock is not null || _selectedImageElement is not null)));
    }

    private void ClearDocument()
    {
        _document.Close();
        _pages.Clear();
        _formFields.Clear();
        _annotations.Clear();
        _textBlocks.Clear();
        _imageElements.Clear();
        ClearHistoryStacks();
        _selectedAnnotation = null;
        _selectedTextBlock = null;
        _selectedImageElement = null;
        HideInlineTextEditor();
        _selectedPageIndex = -1;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Visible;
        FormFieldsHint.Visibility = Visibility.Visible;
        FormValueBox.Text = string.Empty;
        SyncFormValueEditors(null);
        DrawOverlay.Children.Clear();
        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
        SetActiveTool(PdfEditorTool.Select);
        _setStatus?.Invoke(_loc.Get("EditorClosed"));
    }

    private void ClearHistoryStacks()
    {
        _annotationUndo.Clear();
        _annotationRedo.Clear();
        _textBlockUndo.Clear();
        _textBlockRedo.Clear();
        _imageUndo.Clear();
        _imageRedo.Clear();
        _undoKinds.Clear();
        _redoKinds.Clear();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document.WorkingCopyPath is null)
            return;

        ClearDocument();
    }

    private void ShowError(string message, MessageBoxImage icon = MessageBoxImage.Error)
    {
        if (OwnerWindow is null)
            MessageBox.Show(message, _loc.Get("AppTitle"), MessageBoxButton.OK, icon);
        else
            ThemedMessageBox.Show(OwnerWindow, message, _loc.Get("AppTitle"), _loc.Get("Ok"), icon);
    }

    private void ShowInfo(string message) => ShowError(message, MessageBoxImage.Information);

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = _loc.Get("EditorOpenFilter"),
            Title = _loc.Get("EditorOpen")
        };
        if (dlg.ShowDialog() != true)
            return;

        LoadDocument(dlg.FileName);
    }

    private void NewPdfButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _document.CreateNew();
            ResetEditorStateAfterDocumentChange();
            _setStatus?.Invoke(string.Format(_loc.Get("EditorNewCreated"), _pages.Count));
            SetActiveTool(PdfEditorTool.Select);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            _document.Close();
            _document.Open(path);
            ResetEditorStateAfterDocumentChange();

            var name = System.IO.Path.GetFileName(path);
            _setStatus?.Invoke(string.Format(_loc.Get("EditorLoaded"), name, _pages.Count));
            SetActiveTool(PdfEditorTool.EditText);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ResetEditorStateAfterDocumentChange()
    {
        try
        {
            _document.RemoveBrokenAcroFormFieldEntries();
        }
        catch
        {
            // ignore cleanup failures on load
        }

        _annotations.Clear();
        _textBlocks.Clear();
        _imageElements.Clear();
        ClearHistoryStacks();
        _selectedAnnotation = null;
        _selectedTextBlock = null;
        _selectedImageElement = null;
        HideInlineTextEditor();
        ReloadPages();
        ReloadFormFields();
        ReloadAllPageContent();
        EmptyText.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
        if (_pages.Count > 0)
        {
            PagesList.SelectedIndex = 0;
            _selectedPageIndex = 0;
            RefreshPreview();
        }

        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void ReloadPages()
    {
        _pages.Clear();
        if (_document.WorkingCopyPath is null)
            return;

        var count = _document.PageCount;
        for (var i = 0; i < count; i++)
        {
            _pages.Add(new PdfPageItem
            {
                PageNumber = i + 1,
                SourceIndex = i,
                Thumbnail = _render.RenderPage(_document.WorkingCopyPath, i, 128)
            });
        }
    }

    private void ReloadFormFields()
    {
        _formFields.Clear();
        foreach (var field in _document.GetFormFields())
            _formFields.Add(field);

        UpdateFormFieldsPanel();
    }

    private void UpdateFormFieldsPanel()
    {
        var pendingName = _pendingTextFormField?.Name ?? _pendingComboFormField?.Name;
        var hasPending = !string.IsNullOrWhiteSpace(pendingName);
        PendingFormFieldHint.Visibility = hasPending ? Visibility.Visible : Visibility.Collapsed;
        if (hasPending)
            PendingFormFieldHint.Text = string.Format(_loc.Get("EditorPendingFormFieldHint"), pendingName);

        FormFieldsHint.Visibility = _formFields.Count == 0 && !hasPending ? Visibility.Visible : Visibility.Collapsed;
        DeleteFormFieldButton.IsEnabled = FormFieldsList.SelectedItem is PdfFormFieldItem;
        EditFormFieldButton.IsEnabled = FormFieldsList.SelectedItem is PdfFormFieldItem;
    }

    private void PagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PagesList.SelectedItem is PdfPageItem page)
        {
            _selectedPageIndex = page.SourceIndex;
            _selectedAnnotation = null;
            ClearContentSelection();
            HideInlineTextEditor();
            RefreshPreview();
            UpdateDocumentUiState();
        }
    }

    private void ReloadTextBlocksForPage(int pageIndex)
    {
        if (_document.WorkingCopyPath is null)
            return;

        _textBlocks.RemoveAll(b => b.PageIndex == pageIndex);
        var (pageW, pageH) = _document.GetPageSize(pageIndex);
        var renderWidth = (int)(960 * _zoom);
        _textBlocks.AddRange(_textExtract.ExtractPageBlocks(_document.WorkingCopyPath, pageIndex, pageW, pageH, renderWidth));
    }

    private void RefreshPreview()
    {
        if (_document.WorkingCopyPath is null || _selectedPageIndex < 0)
            return;

        var maxWidth = (int)(960 * _zoom);
        PreviewImage.Source = _render.RenderPage(_document.WorkingCopyPath, _selectedPageIndex, maxWidth);
        PreviewImage.SizeChanged -= PreviewImage_SizeChanged;
        PreviewImage.SizeChanged += PreviewImage_SizeChanged;
        RefreshAnnotationOverlay();
    }

    private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshAnnotationOverlay();

    private void RefreshAnnotationOverlay()
    {
        DrawOverlay.Children.Clear();
        SyncOverlaySize();
        UpdateOverlayHitTesting();

        if (_document.WorkingCopyPath is null || _selectedPageIndex < 0)
            return;

        DrawPendingContentPreview();
        DrawFormFieldOverlay();

        if (_activeTool == PdfEditorTool.Select &&
            GetSelectedFormField() is { HasBounds: true, PageIndex: var formPage } selectedField &&
            formPage == _selectedPageIndex &&
            !selectedField.ReadOnly)
        {
            AddFormFieldTransformHandles(selectedField);
        }

        if (_activeTool == PdfEditorTool.EditText)
        {
            DrawContentSelectionChrome();
            return;
        }

        foreach (var annotation in _annotations.Where(a => a.PageIndex == _selectedPageIndex))
            DrawOverlay.Children.Add(CreateAnnotationVisual(annotation));

        if (_selectedAnnotation is { PageIndex: var page } ann && page == _selectedPageIndex)
        {
            DrawOverlay.Children.Add(CreateSelectionVisual(ann));
            if (_activeTool == PdfEditorTool.Select && SupportsTransformHandles(ann))
                AddAnnotationTransformHandles(ann);
        }
    }

    private void UpdateOverlayHitTesting() =>
        DrawOverlay.IsHitTestVisible = _document.WorkingCopyPath is not null &&
            (_activeTool is PdfEditorTool.Select or PdfEditorTool.EditText
             || IsFormDrawToolActive);

    private bool IsFormDrawToolActive =>
        (_activeTool == PdfEditorTool.FormTextField && _pendingTextFormField is not null) ||
        (_activeTool == PdfEditorTool.FormComboBox && _pendingComboFormField is not null);

    private void SyncOverlaySize()
    {
        DrawOverlay.Width = PreviewImage.ActualWidth;
        DrawOverlay.Height = PreviewImage.ActualHeight;

        if (PreviewImage.Source is not null && PreviewImage.ActualWidth > 0 && PreviewImage.ActualHeight > 0)
        {
            PreviewPageBackground.Width = PreviewImage.ActualWidth;
            PreviewPageBackground.Height = PreviewImage.ActualHeight;
            PreviewPageBackground.Visibility = Visibility.Visible;
        }
        else
        {
            PreviewPageBackground.Visibility = Visibility.Collapsed;
        }
    }

    private UIElement CreateAnnotationVisual(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        var shape = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            RadiusX = annotation.Type is PdfEditorTool.Highlight or PdfEditorTool.Whiteout ? 2 : 0,
            RadiusY = annotation.Type is PdfEditorTool.Highlight or PdfEditorTool.Whiteout ? 2 : 0,
            Tag = annotation,
            Cursor = Cursors.Hand
        };

        switch (annotation.Type)
        {
            case PdfEditorTool.Highlight:
                shape.Fill = new SolidColorBrush(Color.FromArgb(80, 255, 235, 59));
                shape.Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 193, 7));
                shape.StrokeThickness = 1;
                break;
            case PdfEditorTool.Whiteout:
                shape.Fill = Brushes.White;
                shape.Stroke = new SolidColorBrush(Color.FromArgb(160, 148, 163, 184));
                shape.StrokeThickness = 1;
                break;
            case PdfEditorTool.Rectangle:
                shape.Fill = annotation.Filled
                    ? new SolidColorBrush(ParseHexColor(annotation.ColorHex ?? "E11D48"))
                    : Brushes.Transparent;
                shape.Stroke = new SolidColorBrush(ParseHexColor(annotation.ColorHex ?? "E11D48"));
                shape.StrokeThickness = 1.5;
                break;
            case PdfEditorTool.Ellipse:
                return CreateEllipseAnnotationVisual(annotation);
            case PdfEditorTool.Line:
                return CreateLineAnnotationVisual(annotation);
            case PdfEditorTool.Text:
            case PdfEditorTool.Stamp:
                return CreateTextAnnotationVisual(annotation);
            case PdfEditorTool.Image:
            case PdfEditorTool.Signature:
                return CreateImageAnnotationVisual(annotation);
            default:
                shape.Fill = new SolidColorBrush(Color.FromArgb(40, 59, 130, 246));
                shape.Stroke = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246));
                shape.StrokeThickness = 1;
                break;
        }

        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        shape.MouseLeftButtonDown += AnnotationVisual_MouseLeftButtonDown;
        shape.MouseLeftButtonUp += AnnotationVisual_MouseLeftButtonUp;
        return shape;
    }

    private UIElement CreateTextAnnotationVisual(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        var color = ParseHexColor(annotation.ColorHex ?? (annotation.Type == PdfEditorTool.Stamp ? "E11D48" : "111827"));
        var isStamp = annotation.Type == PdfEditorTool.Stamp;
        var label = new TextBlock
        {
            Text = annotation.Text ?? string.Empty,
            Foreground = new SolidColorBrush(color),
            FontFamily = new FontFamily(annotation.FontFamily),
            FontWeight = annotation.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = annotation.Italic ? FontStyles.Italic : FontStyles.Normal,
            FontSize = Math.Max(isStamp ? 10 : 8, annotation.FontSize / GetScreenScale().ScaleY * 0.82),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = isStamp ? TextAlignment.Center : TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = label
        };
        var border = new Border
        {
            Width = Math.Max(isStamp ? 80 : 40, rect.Width),
            Height = Math.Max(isStamp ? 28 : 18, rect.Height),
            BorderBrush = isStamp ? new SolidColorBrush(color) : Brushes.Transparent,
            BorderThickness = isStamp ? new Thickness(2) : new Thickness(0),
            Background = isStamp
                ? new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B))
                : Brushes.Transparent,
            CornerRadius = isStamp ? new CornerRadius(4) : new CornerRadius(0),
            Tag = annotation,
            Cursor = Cursors.Hand,
            Child = content
        };

        border.MouseLeftButtonDown += AnnotationVisual_MouseLeftButtonDown;
        border.MouseLeftButtonUp += AnnotationVisual_MouseLeftButtonUp;
        if (Math.Abs(annotation.RotationDegrees) > 0.01)
        {
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.RenderTransform = new RotateTransform(annotation.RotationDegrees);
        }

        Canvas.SetLeft(border, rect.Left);
        Canvas.SetTop(border, rect.Top);
        return border;
    }

    private UIElement CreateEllipseAnnotationVisual(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        var color = ParseHexColor(annotation.ColorHex ?? "E11D48");
        var shape = new Ellipse
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Fill = annotation.Filled ? new SolidColorBrush(color) : Brushes.Transparent,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5,
            Tag = annotation,
            Cursor = Cursors.Hand
        };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        shape.MouseLeftButtonDown += AnnotationVisual_MouseLeftButtonDown;
        shape.MouseLeftButtonUp += AnnotationVisual_MouseLeftButtonUp;
        return shape;
    }

    private UIElement CreateLineAnnotationVisual(PdfEditorAnnotation annotation)
    {
        var start = PdfPointToScreen(annotation.X, annotation.Y);
        var end = PdfPointToScreen(annotation.X + annotation.Width, annotation.Y + annotation.Height);
        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(ParseHexColor(annotation.ColorHex ?? "E11D48")),
            StrokeThickness = 2,
            Tag = annotation,
            Cursor = Cursors.Hand
        };
        line.MouseLeftButtonDown += AnnotationVisual_MouseLeftButtonDown;
        line.MouseLeftButtonUp += AnnotationVisual_MouseLeftButtonUp;
        return line;
    }

    private Point PdfPointToScreen(double pdfX, double pdfY)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Point(pdfX / scaleX, pdfY / scaleY);
    }

    private UIElement CreateImageAnnotationVisual(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        UIElement body;
        if (!string.IsNullOrWhiteSpace(annotation.ImagePath) && File.Exists(annotation.ImagePath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(annotation.ImagePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            body = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Width = Math.Max(8, rect.Width),
                Height = Math.Max(8, rect.Height)
            };
        }
        else
        {
            body = new Rectangle
            {
                Width = Math.Max(8, rect.Width),
                Height = Math.Max(8, rect.Height),
                Fill = new SolidColorBrush(Color.FromArgb(40, 59, 130, 246)),
                Stroke = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246)),
                StrokeThickness = 1
            };
        }

        var container = new Border
        {
            Width = Math.Max(8, rect.Width),
            Height = Math.Max(8, rect.Height),
            Background = Brushes.Transparent,
            Tag = annotation,
            Cursor = Cursors.Hand,
            Child = body
        };
        container.MouseLeftButtonDown += AnnotationVisual_MouseLeftButtonDown;
        container.MouseLeftButtonUp += AnnotationVisual_MouseLeftButtonUp;
        if (Math.Abs(annotation.RotationDegrees) > 0.01)
        {
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            container.RenderTransform = new RotateTransform(annotation.RotationDegrees);
        }

        Canvas.SetLeft(container, rect.Left);
        Canvas.SetTop(container, rect.Top);
        return container;
    }

    private Rectangle CreateSelectionVisual(PdfEditorAnnotation annotation)
    {
        var rect = AnnotationPdfToScreenRect(annotation);
        var selection = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Stroke = new SolidColorBrush(Color.FromArgb(220, 225, 29, 72)),
            StrokeThickness = 2,
            StrokeDashArray = [4, 2],
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(selection, rect.Left);
        Canvas.SetTop(selection, rect.Top);
        return selection;
    }

    private UIElement CreateTextBlockVisual(PdfTextBlockItem block)
    {
        var rect = TextBlockPdfToScreenRect(block);
        var shape = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromArgb(180, 59, 130, 246)),
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            RadiusX = 2,
            RadiusY = 2,
            Tag = block,
            Cursor = Cursors.SizeAll
        };

        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        shape.MouseLeftButtonDown += TextBlockVisual_MouseLeftButtonDown;
        return shape;
    }

    private Rectangle CreateTextBlockSelectionVisual(PdfTextBlockItem block)
    {
        var rect = TextBlockPdfToScreenRect(block);
        var selection = new Rectangle
        {
            Width = Math.Max(1, rect.Width),
            Height = Math.Max(1, rect.Height),
            Stroke = new SolidColorBrush(Color.FromArgb(230, 37, 99, 235)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(24, 59, 130, 246)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(selection, rect.Left);
        Canvas.SetTop(selection, rect.Top);
        return selection;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveDocument(null);

    private void SaveAsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = _loc.Get("EditorSaveFilter"),
            Title = _loc.Get("EditorSaveAs"),
            FileName = System.IO.Path.GetFileNameWithoutExtension(_document.OriginalPath ?? "document") + "_edited.pdf",
            InitialDirectory = _settings?.Current.DefaultSavePath
        };
        if (dlg.ShowDialog() != true)
            return;

        SaveDocument(dlg.FileName);
    }

    private void SaveDocument(string? path)
    {
        if (_document.WorkingCopyPath is null)
        {
            ShowInfo(_loc.Get("EditorNoDocument"));
            return;
        }

        var output = path ?? System.IO.Path.Combine(
            _settings?.Current.DefaultSavePath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            System.IO.Path.GetFileName(_document.OriginalPath ?? "document_edited.pdf"));

        try
        {
            _document.FlushAnnotations(_annotations);
            _document.ApplyTextReplacements(_textBlocks);
            _document.ApplyImageChanges(_imageElements);
            var compress = CompressOnSaveCheck.IsChecked == true;
            var level = _settings?.Current.DefaultCompressLevel ?? PdfCompressLevel.Medium;
            _document.Save(output, compress, level);
            _annotations.Clear();
            _textBlocks.Clear();
            _imageElements.Clear();
            ClearHistoryStacks();
            _selectedAnnotation = null;
            ClearContentSelection();
            HideInlineTextEditor();
            ReloadFormFields();
            ReloadAllPageContent();
            RefreshPreview();
            UpdateUndoRedoButtons();
            UpdateDocumentUiState();
            _setStatus?.Invoke(string.Format(_loc.Get("EditorSaved"), output));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private bool EnsurePageSelected()
    {
        if (_selectedPageIndex >= 0)
            return true;

        ShowInfo(_loc.Get("EditorSelectPage"));
        return false;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_undoKinds.Count == 0)
        {
            if (_document.CanUndo)
            {
                _document.Undo();
                ReloadPages();
                ReloadFormFields();
                ReloadAllPageContent();
                RefreshPreview();
                UpdateUndoRedoButtons();
            }

            return;
        }

        var kind = _undoKinds.Pop();
        switch (kind)
        {
            case EditorUndoKind.TextBlock:
                _textBlockRedo.Push(CloneTextBlocks());
                _imageRedo.Push(CloneImages());
                _redoKinds.Push(EditorUndoKind.TextBlock);
                RestoreTextBlocks(_textBlockUndo.Pop());
                RestoreImages(_imageUndo.Pop());
                ClearContentSelection();
                HideInlineTextEditor();
                RefreshPreview();
                break;
            case EditorUndoKind.Annotation:
                _annotationRedo.Push(CloneAnnotations(_annotations));
                _redoKinds.Push(EditorUndoKind.Annotation);
                RestoreAnnotations(_annotationUndo.Pop());
                RefreshPreview();
                break;
            case EditorUndoKind.Document:
                _document.Undo();
                ReloadPages();
                ReloadFormFields();
                ReloadAllPageContent();
                RefreshPreview();
                break;
        }

        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_redoKinds.Count == 0)
        {
            if (_document.CanRedo)
            {
                _document.Redo();
                ReloadPages();
                ReloadFormFields();
                ReloadAllPageContent();
                RefreshPreview();
                UpdateUndoRedoButtons();
            }

            return;
        }

        var kind = _redoKinds.Pop();
        switch (kind)
        {
            case EditorUndoKind.TextBlock:
                _textBlockUndo.Push(CloneTextBlocks());
                _imageUndo.Push(CloneImages());
                _undoKinds.Push(EditorUndoKind.TextBlock);
                RestoreTextBlocks(_textBlockRedo.Pop());
                RestoreImages(_imageRedo.Pop());
                ClearContentSelection();
                HideInlineTextEditor();
                RefreshPreview();
                break;
            case EditorUndoKind.Annotation:
                _annotationUndo.Push(CloneAnnotations(_annotations));
                _undoKinds.Push(EditorUndoKind.Annotation);
                RestoreAnnotations(_annotationRedo.Pop());
                RefreshPreview();
                break;
            case EditorUndoKind.Document:
                _document.Redo();
                ReloadPages();
                ReloadFormFields();
                ReloadAllPageContent();
                RefreshPreview();
                break;
        }

        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undoKinds.Count > 0 || _document.CanUndo;
        RedoButton.IsEnabled = _redoKinds.Count > 0 || _document.CanRedo;
    }

    private void SetActiveTool(PdfEditorTool tool)
    {
        HideInlineTextEditor();
        if (tool != PdfEditorTool.FormTextField)
            _pendingTextFormField = null;
        if (tool != PdfEditorTool.FormComboBox)
            _pendingComboFormField = null;

        _activeTool = tool;
        _selectedAnnotation = tool == PdfEditorTool.Select ? _selectedAnnotation : null;
        _selectedTextBlock = tool == PdfEditorTool.EditText ? _selectedTextBlock : null;
        if (tool != PdfEditorTool.EditText)
        {
            _selectedImageElement = null;
            ContentEditPanel.Visibility = Visibility.Collapsed;
        }
        UpdateToolButtonStyles();
        UpdateOverlayHitTesting();
        RefreshAnnotationOverlay();
        UpdateDocumentUiState();
        UpdateFormFieldsPanel();

        _setStatus?.Invoke(tool switch
        {
            PdfEditorTool.EditText => _loc.Get("EditorEditTextHint"),
            PdfEditorTool.Select => _loc.Get("EditorSelectHint"),
            PdfEditorTool.FormTextField when _pendingTextFormField is not null =>
                string.Format(_loc.Get("EditorPendingFormFieldHint"), _pendingTextFormField.Name),
            PdfEditorTool.FormComboBox when _pendingComboFormField is not null =>
                string.Format(_loc.Get("EditorPendingFormFieldHint"), _pendingComboFormField.Name),
            PdfEditorTool.Stamp when !string.IsNullOrWhiteSpace(_pendingStampText) =>
                string.Format(_loc.Get("EditorStampReady"), _pendingStampText),
            PdfEditorTool.Text when !string.IsNullOrWhiteSpace(_pendingAddText) =>
                string.Format(_loc.Get("EditorAddTextReady"), _pendingAddText),
            _ => _loc.Get("Ready")
        });
    }

    private void UpdateToolButtonStyles()
    {
        var primary = (Style)FindResource("PrimaryButton");
        var secondary = (Style)FindResource("SecondaryButton");

        SelectToolButton.Style = _activeTool == PdfEditorTool.Select ? primary : secondary;
        EditTextToolButton.Style = _activeTool == PdfEditorTool.EditText ? primary : secondary;
        TextToolButton.Style = _activeTool == PdfEditorTool.Text ? primary : secondary;
        ImageToolButton.Style = _activeTool == PdfEditorTool.Image ? primary : secondary;
        SignatureToolButton.Style = _activeTool == PdfEditorTool.Signature ? primary : secondary;
        StampToolButton.Style = _activeTool == PdfEditorTool.Stamp ? primary : secondary;
        RectangleToolButton.Style = _activeTool == PdfEditorTool.Rectangle ? primary : secondary;
        EllipseToolButton.Style = _activeTool == PdfEditorTool.Ellipse ? primary : secondary;
        LineToolButton.Style = _activeTool == PdfEditorTool.Line ? primary : secondary;
        HighlightToolButton.Style = _activeTool == PdfEditorTool.Highlight ? primary : secondary;
        WhiteoutToolButton.Style = _activeTool == PdfEditorTool.Whiteout ? primary : secondary;
    }

    private void SelectToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Select);
    private void EditTextToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.EditText);
    private void TextToolButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner is null)
            return;

        var result = AddTextPrompt.Show(owner, _loc, _pendingAddText ?? string.Empty, _pendingAddTextColorHex,
            _pendingAddTextFontSize, _pendingAddTextFontFamily, _pendingAddTextBold, _pendingAddTextItalic,
            _pendingAddTextRotationDegrees);
        if (result is null)
            return;

        _pendingAddText = result.Text;
        _pendingAddTextColorHex = result.ColorHex;
        _pendingAddTextFontSize = result.FontSize;
        _pendingAddTextFontFamily = result.FontFamily;
        _pendingAddTextBold = result.Bold;
        _pendingAddTextItalic = result.Italic;
        _pendingAddTextRotationDegrees = result.RotationDegrees;
        SetActiveTool(PdfEditorTool.Text);
        _setStatus?.Invoke(string.Format(_loc.Get("EditorAddTextReady"), result.Text));
    }
    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Rectangle);
    private void EllipseToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Ellipse);
    private void LineToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Line);
    private void HighlightToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Highlight);
    private void WhiteoutToolButton_Click(object sender, RoutedEventArgs e) => SetActiveTool(PdfEditorTool.Whiteout);
    private void StampToolButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner is null)
            return;

        var result = StampPrompt.Show(owner, _loc, _pendingStampText ?? "APPROVED", _pendingStampColorHex,
            _pendingStampFontSize, _pendingStampFontFamily, _pendingStampBold, _pendingStampItalic);
        if (result is null)
            return;

        _pendingStampText = result.Text;
        _pendingStampColorHex = result.ColorHex;
        _pendingStampFontSize = result.FontSize;
        _pendingStampFontFamily = result.FontFamily;
        _pendingStampBold = result.Bold;
        _pendingStampItalic = result.Italic;
        SetActiveTool(PdfEditorTool.Stamp);
        _setStatus?.Invoke(string.Format(_loc.Get("EditorStampReady"), result.Text));
    }

    private void SignatureToolButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner is null)
            return;

        var result = SignaturePrompt.Show(owner, _loc);
        if (result is null)
            return;

        _signatureImagePath = result.ImagePath;
        SetActiveTool(PdfEditorTool.Signature);
        _setStatus?.Invoke(_loc.Get("EditorSignatureReady"));
    }

    private void ImageToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected())
            return;

        var dlg = new OpenFileDialog { Filter = _loc.Get("EditorImageFilter") };
        if (dlg.ShowDialog() != true)
            return;

        RegisterAnnotationUndoPoint();
        _annotations.Add(new PdfEditorAnnotation
        {
            PageIndex = _selectedPageIndex,
            Type = PdfEditorTool.Image,
            X = 72,
            Y = 72,
            Width = 180,
            Height = 120,
            ImagePath = dlg.FileName
        });
        _selectedAnnotation = _annotations[^1];
        SetActiveTool(PdfEditorTool.Select);
        _setStatus?.Invoke(_loc.Get("EditorTransformHint"));
        RefreshPreview();
        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!EnsurePageSelected())
            return;

        var pos = e.GetPosition(PreviewImage);

        if (_activeTool is PdfEditorTool.Text or PdfEditorTool.Stamp)
        {
            ApplyPointTool(pos);
            return;
        }

        if (_activeTool is PdfEditorTool.Signature && _signatureImagePath is not null)
        {
            ApplySignature(pos);
            return;
        }

        if (_activeTool is PdfEditorTool.Rectangle or PdfEditorTool.Ellipse or PdfEditorTool.Highlight or PdfEditorTool.Whiteout
            or PdfEditorTool.Line or PdfEditorTool.FormTextField or PdfEditorTool.FormComboBox)
        {
            _drawStart = pos;
            PreviewImage.CaptureMouse();
            StartDrawPreview(pos);
        }
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drawStart is null || _drawPreview is null)
            return;

        var pos = e.GetPosition(PreviewImage);
        UpdateDrawPreview(_drawStart.Value, pos);
    }

    private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawStart is null)
            return;

        FinishDrawTool(e.GetPosition(PreviewImage));
    }

    private bool TryBeginFormFieldDraw(Point pos)
    {
        if (!IsFormDrawToolActive)
            return false;

        _drawStart = pos;
        DrawOverlay.CaptureMouse();
        StartDrawPreview(pos);
        return true;
    }

    private void FinishDrawTool(Point end)
    {
        if (_drawStart is null)
            return;

        DrawOverlay.ReleaseMouseCapture();
        PreviewImage.ReleaseMouseCapture();
        var start = _drawStart.Value;
        ApplyDrawTool(start, end);
        CancelDrawPreview();
    }

    private void CancelDrawPreview()
    {
        _drawStart = null;
        _drawPreview = null;
        RefreshAnnotationOverlay();
    }

    private void DrawOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!EnsurePageSelected())
            return;

        var pos = e.GetPosition(DrawOverlay);

        if (TryBeginFormFieldDraw(pos))
        {
            e.Handled = true;
            return;
        }

        if (_activeTool == PdfEditorTool.EditText)
        {
            if (_editingTextBlock is not null)
                CommitInlineTextEditor();

            var image = HitTestImageElement(pos);
            if (image is not null)
            {
                SelectImageElement(image);
                _isDraggingContent = true;
                _contentDragStartScreen = pos;
                _contentDragStartX = image.X;
                _contentDragStartY = image.Y;
                RegisterContentUndoPoint();
                DrawOverlay.CaptureMouse();
                e.Handled = true;
                return;
            }

            var block = HitTestTextBlock(pos);
            if (block is not null)
            {
                SelectTextBlock(block);
                _isDraggingContent = true;
                _contentDragStartScreen = pos;
                _contentDragStartX = block.X;
                _contentDragStartY = block.Y;
                RegisterContentUndoPoint();
                DrawOverlay.CaptureMouse();
            }
            else
            {
                ClearContentSelection();
                RefreshAnnotationOverlay();
            }

            UpdateDocumentUiState();
            e.Handled = true;
            return;
        }

        if (_activeTool != PdfEditorTool.Select)
            return;

        var field = HitTestFormField(pos);
        if (field is not null)
        {
            if (e.ClickCount >= 2)
            {
                FormFieldsList.SelectedItem = field;
                SyncFormValueEditors(field);
                EditSelectedFormField();
            }
            else
            {
                _selectedAnnotation = null;
                BeginFormFieldMove(field, pos);
            }

            e.Handled = true;
            return;
        }

        _selectedAnnotation = HitTestAnnotation(pos);
        RefreshAnnotationOverlay();
        UpdateDocumentUiState();
        e.Handled = true;
    }

    private void AnnotationVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.Select || sender is not FrameworkElement { Tag: PdfEditorAnnotation annotation })
            return;

        if (e.ClickCount >= 2)
        {
            EditAnnotation(annotation);
            e.Handled = true;
            return;
        }

        _selectedAnnotation = annotation;
        _movingAnnotation = annotation;
        _moveStartScreen = e.GetPosition(DrawOverlay);
        _moveStartX = annotation.X;
        _moveStartY = annotation.Y;
        RegisterAnnotationUndoPoint();
        DrawOverlay.CaptureMouse();
        RefreshAnnotationOverlay();
        UpdateDocumentUiState();
        e.Handled = true;
    }

    private void AnnotationVisual_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingAnnotation is null)
            return;

        DrawOverlay.ReleaseMouseCapture();
        _movingAnnotation = null;
        RefreshAnnotationOverlay();
        e.Handled = true;
    }

    private void DrawOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drawStart is not null && _drawPreview is not null && IsFormDrawToolActive)
        {
            UpdateDrawPreview(_drawStart.Value, e.GetPosition(DrawOverlay));
            return;
        }

        if (_isResizingFormField || _isRotatingFormField)
        {
            HandleFormFieldTransformMove(e.GetPosition(DrawOverlay));
            return;
        }

        if (_movingFormField is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var movePos = e.GetPosition(DrawOverlay);
            var moveDelta = movePos - _moveStartScreen;
            var (moveScaleX, moveScaleY) = GetScreenScale();
            _movingFormField.X = _moveStartX + moveDelta.X / moveScaleX;
            _movingFormField.Y = _moveStartY + moveDelta.Y / moveScaleY;
            RefreshAnnotationOverlay();
            return;
        }

        if (_isDraggingContent || _isResizingContent || _isRotatingContent)
        {
            HandleContentDragMove(e.GetPosition(DrawOverlay));
            return;
        }

        if (_isResizingAnnotation || _isRotatingAnnotation)
        {
            HandleAnnotationTransformMove(e.GetPosition(DrawOverlay));
            return;
        }

        if (_movingAnnotation is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(DrawOverlay);
        var delta = pos - _moveStartScreen;
        var (scaleX, scaleY) = GetScreenScale();
        _movingAnnotation.X = _moveStartX + delta.X / scaleX;
        _movingAnnotation.Y = _moveStartY + delta.Y / scaleY;
        RefreshAnnotationOverlay();
    }

    private void DrawOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawStart is not null && IsFormDrawToolActive)
        {
            FinishDrawTool(e.GetPosition(DrawOverlay));
            e.Handled = true;
            return;
        }

        if (_isDraggingContent || _isResizingContent || _isRotatingContent)
        {
            EndContentDrag();
            e.Handled = true;
            return;
        }

        if (_isResizingFormField || _isRotatingFormField || _movingFormField is not null)
        {
            EndFormFieldTransform();
            e.Handled = true;
            return;
        }

        if (_isResizingAnnotation || _isRotatingAnnotation)
        {
            EndAnnotationTransform();
            e.Handled = true;
            return;
        }

        if (_movingAnnotation is null)
            return;

        DrawOverlay.ReleaseMouseCapture();
        _movingAnnotation = null;
    }

    private void ApplyPointTool(Point pos)
    {
        var (pdfX, pdfY) = ScreenToPdf(pos);
        try
        {
            switch (_activeTool)
            {
                case PdfEditorTool.Text:
                    if (string.IsNullOrWhiteSpace(_pendingAddText))
                    {
                        TextToolButton_Click(this, new RoutedEventArgs());
                        return;
                    }

                    RegisterAnnotationUndoPoint();
                    _selectedAnnotation = new PdfEditorAnnotation
                    {
                        PageIndex = _selectedPageIndex,
                        Type = PdfEditorTool.Text,
                        X = pdfX,
                        Y = pdfY,
                        Width = Math.Max(80, _pendingAddText.Length * (_pendingAddTextFontSize * 0.55)),
                        Height = Math.Max(18, _pendingAddTextFontSize * 1.35),
                        Text = _pendingAddText,
                        FontSize = _pendingAddTextFontSize,
                        FontFamily = _pendingAddTextFontFamily,
                        Bold = _pendingAddTextBold,
                        Italic = _pendingAddTextItalic,
                        ColorHex = _pendingAddTextColorHex,
                        RotationDegrees = _pendingAddTextRotationDegrees
                    };
                    _annotations.Add(_selectedAnnotation);
                    SelectPlacedAnnotation(_selectedAnnotation);
                    break;

                case PdfEditorTool.Stamp:
                    if (string.IsNullOrWhiteSpace(_pendingStampText))
                    {
                        StampToolButton_Click(this, new RoutedEventArgs());
                        return;
                    }

                    RegisterAnnotationUndoPoint();
                    _selectedAnnotation = new PdfEditorAnnotation
                    {
                        PageIndex = _selectedPageIndex,
                        Type = PdfEditorTool.Stamp,
                        X = pdfX,
                        Y = pdfY,
                        Width = Math.Max(120, _pendingStampText.Length * (_pendingStampFontSize * 0.55)),
                        Height = Math.Max(28, _pendingStampFontSize * 1.6),
                        Text = _pendingStampText,
                        FontSize = _pendingStampFontSize,
                        FontFamily = _pendingStampFontFamily,
                        Bold = _pendingStampBold,
                        Italic = _pendingStampItalic,
                        ColorHex = _pendingStampColorHex
                    };
                    _annotations.Add(_selectedAnnotation);
                    SelectPlacedAnnotation(_selectedAnnotation);
                    break;
            }

            UpdateUndoRedoButtons();
            UpdateDocumentUiState();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SelectPlacedAnnotation(PdfEditorAnnotation annotation)
    {
        _selectedAnnotation = annotation;
        SetActiveTool(PdfEditorTool.Select);
        _setStatus?.Invoke(_loc.Get("EditorTransformHint"));
    }

    private void ApplySignature(Point pos)
    {
        if (_signatureImagePath is null)
            return;

        var (pdfX, pdfY) = ScreenToPdf(pos);
        RegisterAnnotationUndoPoint();
        _selectedAnnotation = new PdfEditorAnnotation
        {
            PageIndex = _selectedPageIndex,
            Type = PdfEditorTool.Signature,
            X = pdfX,
            Y = pdfY,
            Width = 160,
            Height = 60,
            ImagePath = _signatureImagePath
        };
        _annotations.Add(_selectedAnnotation);
        SelectPlacedAnnotation(_selectedAnnotation);
        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void ApplyDrawTool(Point start, Point end)
    {
        if (_activeTool == PdfEditorTool.Line)
        {
            if ((end - start).Length < 4)
                return;

            var startPdf = ScreenToPdf(start);
            var endPdf = ScreenToPdf(end);
            RegisterAnnotationUndoPoint();
            _selectedAnnotation = new PdfEditorAnnotation
            {
                PageIndex = _selectedPageIndex,
                Type = PdfEditorTool.Line,
                X = startPdf.X,
                Y = startPdf.Y,
                Width = endPdf.X - startPdf.X,
                Height = endPdf.Y - startPdf.Y,
                ColorHex = "E11D48"
            };
            _annotations.Add(_selectedAnnotation);
            SelectPlacedAnnotation(_selectedAnnotation);
            return;
        }

        var rect = NormalizeRect(start, end);
        if (rect.Width < 4 || rect.Height < 4)
            return;

        var topLeft = ScreenToPdf(new Point(rect.Left, rect.Top));
        var bottomRight = ScreenToPdf(new Point(rect.Right, rect.Bottom));
        var pdfWidth = Math.Abs(bottomRight.X - topLeft.X);
        var pdfHeight = Math.Abs(bottomRight.Y - topLeft.Y);
        var pdfX = Math.Min(topLeft.X, bottomRight.X);
        var pdfY = Math.Min(topLeft.Y, bottomRight.Y);

        if (_activeTool == PdfEditorTool.FormTextField && _pendingTextFormField is not null)
        {
            var addedField = _pendingTextFormField;
            try
            {
                RegisterDocumentUndoPoint();
                _document.AddTextFormField(_selectedPageIndex, addedField.Name, pdfX, pdfY, pdfWidth, pdfHeight,
                    addedField.DefaultValue,
                    addedField.ColorHex,
                    addedField.FontSize,
                    addedField.FontFamily,
                    addedField.Bold,
                    addedField.Italic,
                    addedField.RotationDegrees);
                ReloadFormFields();
                SelectFormFieldByName(addedField.Name);
                RefreshPreview();
                SetActiveTool(PdfEditorTool.Select);
                _setStatus?.Invoke(string.Format(_loc.Get("EditorFormFieldAdded"), addedField.Name));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }

            return;
        }

        if (_activeTool == PdfEditorTool.FormComboBox && _pendingComboFormField is not null)
        {
            var addedField = _pendingComboFormField;
            try
            {
                RegisterDocumentUndoPoint();
                _document.AddComboFormField(_selectedPageIndex, addedField.Name, pdfX, pdfY, pdfWidth, pdfHeight,
                    addedField.Options, addedField.DefaultValue,
                    addedField.ColorHex,
                    addedField.FontSize,
                    addedField.FontFamily,
                    addedField.Bold,
                    addedField.Italic,
                    addedField.RotationDegrees);
                ReloadFormFields();
                SelectFormFieldByName(addedField.Name);
                RefreshPreview();
                SetActiveTool(PdfEditorTool.Select);
                _setStatus?.Invoke(string.Format(_loc.Get("EditorFormFieldAdded"), addedField.Name));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }

            return;
        }

        RegisterAnnotationUndoPoint();
        _selectedAnnotation = new PdfEditorAnnotation
        {
            PageIndex = _selectedPageIndex,
            Type = _activeTool,
            X = pdfX,
            Y = pdfY,
            Width = pdfWidth,
            Height = pdfHeight,
            ColorHex = "E11D48"
        };
        _annotations.Add(_selectedAnnotation);
        SelectPlacedAnnotation(_selectedAnnotation);
    }

    private void SelectFormFieldByName(string name)
    {
        var field = _formFields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (field is null)
            return;

        FormFieldsList.SelectedItem = field;
        SyncFormValueEditors(field);
    }

    private void AddTextFieldButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner is null || !EnsurePageSelected())
            return;

        var result = FormFieldPrompt.ShowTextField(owner, _loc, NextTextFormFieldName());
        if (result is null)
            return;

        _pendingTextFormField = result;
        _pendingComboFormField = null;
        SetActiveTool(PdfEditorTool.FormTextField);
    }

    private string NextTextFormFieldName()
    {
        var index = 1;
        while (_formFields.Any(f => string.Equals(f.Name, $"TextFormField{index}", StringComparison.OrdinalIgnoreCase)))
            index++;
        return $"TextFormField{index}";
    }

    private string NextComboFormFieldName()
    {
        var index = 1;
        while (_formFields.Any(f => string.Equals(f.Name, $"ComboFormField{index}", StringComparison.OrdinalIgnoreCase)))
            index++;
        return $"ComboFormField{index}";
    }

    private void AddComboFieldButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = OwnerWindow;
        if (owner is null || !EnsurePageSelected())
            return;

        var result = FormFieldPrompt.ShowComboField(owner, _loc, NextComboFormFieldName());
        if (result is null)
            return;

        _pendingComboFormField = result;
        _pendingTextFormField = null;
        SetActiveTool(PdfEditorTool.FormComboBox);
    }

    private void EditAnnotation(PdfEditorAnnotation annotation)
    {
        if (annotation.Type is not (PdfEditorTool.Text or PdfEditorTool.Stamp))
            return;

        var owner = OwnerWindow;
        if (owner is null)
            return;

        if (annotation.Type == PdfEditorTool.Stamp)
        {
            var result = StampPrompt.Show(owner, _loc, annotation.Text ?? "APPROVED", annotation.ColorHex ?? "E11D48",
                annotation.FontSize, annotation.FontFamily, annotation.Bold, annotation.Italic);
            if (result is null)
                return;

            RegisterAnnotationUndoPoint();
            annotation.Text = result.Text;
            annotation.ColorHex = result.ColorHex;
            annotation.FontSize = result.FontSize;
            annotation.FontFamily = result.FontFamily;
            annotation.Bold = result.Bold;
            annotation.Italic = result.Italic;
            annotation.Width = Math.Max(120, result.Text.Length * (result.FontSize * 0.55));
            annotation.Height = Math.Max(28, result.FontSize * 1.6);
            _pendingStampText = result.Text;
            _pendingStampColorHex = result.ColorHex;
            _pendingStampFontSize = result.FontSize;
            _pendingStampFontFamily = result.FontFamily;
            _pendingStampBold = result.Bold;
            _pendingStampItalic = result.Italic;
        }
        else
        {
            var result = AddTextPrompt.Show(owner, _loc, annotation.Text ?? string.Empty,
                annotation.ColorHex ?? "111827", annotation.FontSize, annotation.FontFamily, annotation.Bold,
                annotation.Italic, annotation.RotationDegrees);
            if (result is null)
                return;

            RegisterAnnotationUndoPoint();
            annotation.Text = result.Text;
            annotation.ColorHex = result.ColorHex;
            annotation.FontSize = result.FontSize;
            annotation.FontFamily = result.FontFamily;
            annotation.Bold = result.Bold;
            annotation.Italic = result.Italic;
            annotation.RotationDegrees = result.RotationDegrees;
            annotation.Width = Math.Max(80, result.Text.Length * (result.FontSize * 0.55));
            annotation.Height = Math.Max(18, result.FontSize * 1.35);
            _pendingAddText = result.Text;
            _pendingAddTextColorHex = result.ColorHex;
            _pendingAddTextFontSize = result.FontSize;
            _pendingAddTextFontFamily = result.FontFamily;
            _pendingAddTextBold = result.Bold;
            _pendingAddTextItalic = result.Italic;
            _pendingAddTextRotationDegrees = result.RotationDegrees;
        }

        _selectedAnnotation = annotation;
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
    }

    private void DeleteSelectionButton_Click(object sender, RoutedEventArgs e) => DeleteCurrentSelection();

    private void PdfEditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        if (FormFieldsList.SelectedItem is PdfFormFieldItem &&
            (FormFieldsList.IsKeyboardFocusWithin || FormFieldsList.IsFocused))
        {
            DeleteFormFieldButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        DeleteCurrentSelection();
    }

    private void DeleteCurrentSelection()
    {
        if (_activeTool == PdfEditorTool.EditText && (_selectedTextBlock is not null || _selectedImageElement is not null))
        {
            DeleteCurrentContentSelection();
            return;
        }

        DeleteSelectedAnnotation();
    }

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation is null)
            return;

        RegisterAnnotationUndoPoint();
        _annotations.RemoveAll(a => a.Id == _selectedAnnotation.Id);
        _selectedAnnotation = null;
        RefreshPreview();
        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void RegisterAnnotationUndoPoint()
    {
        _annotationUndo.Push(CloneAnnotations(_annotations));
        _undoKinds.Push(EditorUndoKind.Annotation);
        _annotationRedo.Clear();
        _textBlockRedo.Clear();
        _imageRedo.Clear();
        _redoKinds.Clear();
    }

    private void RegisterDocumentUndoPoint()
    {
        _undoKinds.Push(EditorUndoKind.Document);
        _annotationRedo.Clear();
        _textBlockRedo.Clear();
        _imageRedo.Clear();
        _redoKinds.Clear();
    }

    private static List<PdfTextBlockItem> CloneTextBlocks(IEnumerable<PdfTextBlockItem> source) =>
        source.Select(b => b.Clone()).ToList();

    private List<PdfTextBlockItem> CloneTextBlocks() => CloneTextBlocks(_textBlocks);

    private void RestoreTextBlocks(IReadOnlyList<PdfTextBlockItem> snapshot)
    {
        _textBlocks.Clear();
        _textBlocks.AddRange(snapshot.Select(b => b.Clone()));
        _selectedTextBlock = _textBlocks.FirstOrDefault(b => b.Id == _selectedTextBlock?.Id);
    }

    private static List<PdfEditorAnnotation> CloneAnnotations(IEnumerable<PdfEditorAnnotation> source) =>
        source.Select(a => a.Clone()).ToList();

    private void RestoreAnnotations(IReadOnlyList<PdfEditorAnnotation> snapshot)
    {
        _annotations.Clear();
        _annotations.AddRange(snapshot.Select(a => a.Clone()));
        _selectedAnnotation = _annotations.FirstOrDefault(a => a.Id == _selectedAnnotation?.Id);
    }

    private PdfEditorAnnotation? HitTestAnnotation(Point screenPoint)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
        {
            var annotation = _annotations[i];
            if (annotation.PageIndex != _selectedPageIndex)
                continue;

            if (AnnotationPdfToScreenRect(annotation).Contains(screenPoint))
                return annotation;
        }

        return null;
    }

    private PdfFormFieldItem? HitTestFormField(Point screenPoint)
    {
        foreach (var field in _formFields.Where(f => f.HasBounds && f.PageIndex == _selectedPageIndex))
        {
            if (FormFieldPdfToScreenRect(field).Contains(screenPoint))
                return field;
        }

        return null;
    }

    private void ShowInlineTextEditor(PdfTextBlockItem block)
    {
        var rect = TextBlockPdfToScreenRect(block);
        _editingTextBlock = block;
        InlineTextEditor.Text = block.CurrentText;
        InlineTextEditor.FontSize = Math.Max(10, PdfFontSizeToScreen(block.FontSize) * 0.95);
        InlineTextEditor.Width = Math.Max(120, rect.Width + 8);
        InlineTextEditor.MinHeight = Math.Max(28, rect.Height + 4);
        InlineTextEditor.MaxHeight = Math.Max(InlineTextEditor.MinHeight, rect.Height * 3);
        InlineTextEditor.Margin = new Thickness(rect.Left - 2, rect.Top - 2, 0, 0);
        InlineTextEditor.HorizontalAlignment = HorizontalAlignment.Left;
        InlineTextEditor.VerticalAlignment = VerticalAlignment.Top;
        InlineTextEditor.Visibility = Visibility.Visible;
        InlineTextEditor.Focus();
        InlineTextEditor.SelectAll();
    }

    private void HideInlineTextEditor()
    {
        _inlineEditorCommitOnLostFocus = false;
        InlineTextEditor.Visibility = Visibility.Collapsed;
        _editingTextBlock = null;
        _inlineEditorCommitOnLostFocus = true;
    }

    private void CommitInlineTextEditor()
    {
        if (_editingTextBlock is null)
            return;

        var newText = InlineTextEditor.Text;
        if (string.Equals(newText, _editingTextBlock.CurrentText, StringComparison.Ordinal))
        {
            HideInlineTextEditor();
            RefreshAnnotationOverlay();
            return;
        }

        RegisterContentUndoPoint();
        if (string.IsNullOrWhiteSpace(newText))
        {
            _editingTextBlock.IsDeleted = true;
            _editingTextBlock.ReplacementText = null;
        }
        else
        {
            _editingTextBlock.IsDeleted = false;
            _editingTextBlock.ReplacementText = newText;
        }

        if (_selectedTextBlock is not null)
            SyncFormatPanelFromSelection();

        HideInlineTextEditor();
        RefreshAnnotationOverlay();
        UpdateUndoRedoButtons();
        UpdateDocumentUiState();
    }

    private void InlineTextEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideInlineTextEditor();
            RefreshAnnotationOverlay();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CommitInlineTextEditor();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !InlineTextEditor.AcceptsReturn)
        {
            CommitInlineTextEditor();
            e.Handled = true;
        }
    }

    private void InlineTextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_inlineEditorCommitOnLostFocus || _editingTextBlock is null)
            return;

        CommitInlineTextEditor();
    }

    private void StartDrawPreview(Point start)
    {
        SyncOverlaySize();
        DrawOverlay.IsHitTestVisible = false;

        if (_activeTool == PdfEditorTool.Line)
        {
            _drawPreview = new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = start.X,
                Y2 = start.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 225, 29, 72)),
                StrokeThickness = 2
            };
        }
        else
        {
            UIElement preview = _activeTool == PdfEditorTool.Ellipse
                ? new Ellipse()
                : new Rectangle();

            if (preview is Shape shape)
            {
                var strokeColor = _activeTool is PdfEditorTool.FormTextField && _pendingTextFormField is not null
                    ? ParseHexColor(_pendingTextFormField.ColorHex)
                    : Color.FromArgb(180, 225, 29, 72);
                shape.Stroke = new SolidColorBrush(strokeColor);
                shape.StrokeThickness = 1.5;
                shape.Fill = _activeTool == PdfEditorTool.Highlight
                    ? new SolidColorBrush(Color.FromArgb(80, 255, 235, 59))
                    : _activeTool == PdfEditorTool.Whiteout
                        ? Brushes.White
                        : _activeTool is PdfEditorTool.FormTextField && _pendingTextFormField is not null
                            ? new SolidColorBrush(Color.FromArgb(40,
                                ParseHexColor(_pendingTextFormField.ColorHex).R,
                                ParseHexColor(_pendingTextFormField.ColorHex).G,
                                ParseHexColor(_pendingTextFormField.ColorHex).B))
                            : _activeTool is PdfEditorTool.FormTextField or PdfEditorTool.FormComboBox
                                ? new SolidColorBrush(Color.FromArgb(40, 59, 130, 246))
                                : Brushes.Transparent;
            }

            _drawPreview = preview;
            Canvas.SetLeft(_drawPreview, start.X);
            Canvas.SetTop(_drawPreview, start.Y);
        }

        DrawOverlay.Children.Add(_drawPreview);
    }

    private void UpdateDrawPreview(Point start, Point end)
    {
        if (_drawPreview is null)
            return;

        if (_drawPreview is Line line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            return;
        }

        var rect = NormalizeRect(start, end);
        Canvas.SetLeft(_drawPreview, rect.Left);
        Canvas.SetTop(_drawPreview, rect.Top);
        if (_drawPreview is FrameworkElement element)
        {
            element.Width = rect.Width;
            element.Height = rect.Height;
        }

        if (_activeTool == PdfEditorTool.FormTextField &&
            _pendingTextFormField is not null &&
            _drawPreview is FrameworkElement previewElement &&
            Math.Abs(_pendingTextFormField.RotationDegrees) > 0.01)
        {
            previewElement.RenderTransformOrigin = new Point(0.5, 0.5);
            previewElement.RenderTransform = new RotateTransform(_pendingTextFormField.RotationDegrees);
        }
    }

    private (double X, double Y) ScreenToPdf(Point screenPoint)
    {
        var (pageW, pageH) = _document.GetPageSize(_selectedPageIndex);
        var (scaleX, scaleY) = GetScreenScale();
        return (screenPoint.X * scaleX, screenPoint.Y * scaleY);
    }

    private (double ScaleX, double ScaleY) GetScreenScale()
    {
        var (pageW, pageH) = _document.GetPageSize(_selectedPageIndex);
        return (pageW / Math.Max(1, PreviewImage.ActualWidth), pageH / Math.Max(1, PreviewImage.ActualHeight));
    }

    private Rect AnnotationPdfToScreenRect(PdfEditorAnnotation annotation)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(
            annotation.X / scaleX,
            annotation.Y / scaleY,
            annotation.Width / scaleX,
            annotation.Height / scaleY);
    }

    private void DrawFormFieldOverlay()
    {
        var (_, scaleY) = GetScreenScale();
        foreach (var field in _formFields.Where(f => f.HasBounds && f.PageIndex == _selectedPageIndex))
        {
            var rect = FormFieldPdfToScreenRect(field);
            var selected = FormFieldsList.SelectedItem is PdfFormFieldItem selectedField &&
                           selectedField.Name == field.Name;

            var container = new Border
            {
                Width = Math.Max(1, rect.Width),
                Height = Math.Max(1, rect.Height),
                Background = field.IsComboField
                    ? Brushes.Transparent
                    : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(selected ? 220 : 160), 59, 130, 246)),
                BorderThickness = new Thickness(selected ? 2 : 1.5),
                CornerRadius = new CornerRadius(2),
                Tag = field,
                Cursor = field.IsComboField ? Cursors.Hand : Cursors.Hand
            };

            if (field.IsComboField && field.Options.Count > 0)
            {
                container.Child = CreateFormFieldOverlayCombo(field, scaleY);
            }
            else
            {
                container.Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(field.Value) ? field.Name : field.Value,
                    Foreground = new SolidColorBrush(ParseHexColor(field.ColorHex)),
                    FontFamily = new FontFamily(field.FontFamily),
                    FontSize = Math.Max(8, field.FontSize / Math.Max(1, scaleY)),
                    FontWeight = field.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = field.Italic ? FontStyles.Italic : FontStyles.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 2, 4, 2),
                    IsHitTestVisible = false
                };
            }

            if (Math.Abs(field.RotationDegrees) > 0.01)
            {
                container.RenderTransformOrigin = new Point(0.5, 0.5);
                container.RenderTransform = new RotateTransform(field.RotationDegrees);
            }

            container.PreviewMouseLeftButtonDown += FormFieldOverlay_PreviewMouseLeftButtonDown;
            Canvas.SetLeft(container, rect.Left);
            Canvas.SetTop(container, rect.Top);
            Panel.SetZIndex(container, field.IsComboField ? 50 : 5);
            DrawOverlay.Children.Add(container);
        }
    }


    private void FormFieldOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PdfFormFieldItem field })
            return;

        if (IsComboControl(e.OriginalSource as DependencyObject))
            return;

        FormFieldOverlay_MouseLeftButtonDown(sender, e);
    }

    private static bool IsComboControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ComboBox)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void SyncFormValueEditors(PdfFormFieldItem? field)
    {
        _suppressFormValueEvents = true;
        try
        {
            if (field is null || !field.IsComboField || field.Options.Count == 0)
            {
                FormValueCombo.Visibility = Visibility.Collapsed;
                FormValueBox.Visibility = Visibility.Visible;
                FormValueBox.Text = field?.Value ?? string.Empty;
                return;
            }

            FormValueBox.Visibility = Visibility.Collapsed;
            FormValueCombo.Visibility = Visibility.Visible;
            ApplyThemeToFormFieldCombo(FormValueCombo);
            FormValueCombo.ItemsSource = field.Options;
            FormValueCombo.SelectedItem = field.Options.Contains(field.Value) ? field.Value : field.Options[0];
        }
        finally
        {
            _suppressFormValueEvents = false;
        }
    }

    private void FormFieldOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != PdfEditorTool.Select || sender is not FrameworkElement { Tag: PdfFormFieldItem field })
            return;

        FormFieldsList.SelectedItem = field;
        SyncFormValueEditors(field);

        if (e.ClickCount >= 2)
        {
            EditSelectedFormField();
            e.Handled = true;
            return;
        }

        _selectedAnnotation = null;
        BeginFormFieldMove(field, e.GetPosition(DrawOverlay));
        e.Handled = true;
    }

    private void EditSelectedFormField()
    {
        if (FormFieldsList.SelectedItem is not PdfFormFieldItem field)
        {
            ShowInfo(_loc.Get("EditorSelectFormField"));
            return;
        }

        if (field.ReadOnly)
        {
            ShowInfo(_loc.Get("EditorFormReadOnly"));
            return;
        }

        if (field.FieldType == "Text")
        {
            var owner = OwnerWindow;
            if (owner is null)
                return;

            var existing = new TextFormFieldPromptResult(
                field.Name,
                field.Value,
                field.ColorHex,
                field.FontSize,
                field.FontFamily,
                field.Bold,
                field.Italic,
                field.RotationDegrees);
            var result = FormFieldPrompt.ShowTextField(owner, _loc, field.Name, existing, lockName: true);
            if (result is null)
                return;

            try
            {
                RegisterDocumentUndoPoint();
                _document.UpdateTextFormField(field.Name, result);
                field.Value = result.DefaultValue;
                field.ColorHex = result.ColorHex;
                field.FontSize = result.FontSize;
                field.FontFamily = result.FontFamily;
                field.Bold = result.Bold;
                field.Italic = result.Italic;
                field.RotationDegrees = result.RotationDegrees;
                FormValueBox.Text = result.DefaultValue;
                SyncFormValueEditors(field);
                RefreshPreview();
                UpdateUndoRedoButtons();
                _setStatus?.Invoke(string.Format(_loc.Get("EditorFormApplied"), field.Name));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }

            return;
        }

        if (field.IsComboField)
        {
            var owner = OwnerWindow;
            if (owner is null)
                return;

            var existing = new ComboFormFieldPromptResult(
                field.Name,
                field.Options,
                field.Value,
                field.ColorHex,
                field.FontSize,
                field.FontFamily,
                field.Bold,
                field.Italic,
                field.RotationDegrees);
            var result = FormFieldPrompt.ShowComboField(owner, _loc, field.Name, existing, lockName: true);
            if (result is null)
                return;

            try
            {
                RegisterDocumentUndoPoint();
                _document.UpdateComboFormField(field.Name, result);
                field.Value = result.DefaultValue;
                field.Options = result.Options.ToList();
                field.ColorHex = result.ColorHex;
                field.FontSize = result.FontSize;
                field.FontFamily = result.FontFamily;
                field.Bold = result.Bold;
                field.Italic = result.Italic;
                field.RotationDegrees = result.RotationDegrees;
                FormValueBox.Text = result.DefaultValue;
                SyncFormValueEditors(field);
                RefreshPreview();
                UpdateUndoRedoButtons();
                _setStatus?.Invoke(string.Format(_loc.Get("EditorFormApplied"), field.Name));
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }

            return;
        }

        var editOwner = OwnerWindow;
        if (editOwner is null)
            return;

        var updated = InputPrompt.Show(editOwner, string.Format(_loc.Get("EditorFormFieldEdited"), field.Name),
            _loc.Get("EditorFormValue"), field.Value, okText: _loc.Get("Ok"), cancelText: _loc.Get("Cancel"));
        if (updated is not null)
        {
            ApplyFormFieldValue(field, updated);
            SyncFormValueEditors(field);
        }
    }

    private Rect FormFieldPdfToScreenRect(PdfFormFieldItem field)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(
            field.X / scaleX,
            field.Y / scaleY,
            field.Width / scaleX,
            field.Height / scaleY);
    }

    private Rect TextBlockPdfToScreenRect(PdfTextBlockItem block)
    {
        var (scaleX, scaleY) = GetScreenScale();
        return new Rect(
            block.X / scaleX,
            block.Y / scaleY,
            block.Width / scaleX,
            block.Height / scaleY);
    }

    private static Rect NormalizeRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Color ParseHexColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return Colors.Black;

        return Color.FromRgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected()) return;
        RegisterDocumentUndoPoint();
        _document.RotatePage(_selectedPageIndex, -90);
        ReloadPages();
        RefreshPreview();
        UpdateUndoRedoButtons();
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected()) return;
        RegisterDocumentUndoPoint();
        _document.RotatePage(_selectedPageIndex, 90);
        ReloadPages();
        RefreshPreview();
        UpdateUndoRedoButtons();
    }

    private void AddPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document.WorkingCopyPath is null)
        {
            ShowInfo(_loc.Get("EditorSelectPage"));
            return;
        }

        RegisterDocumentUndoPoint();
        var insertAfter = _selectedPageIndex >= 0 ? _selectedPageIndex : _pages.Count - 1;
        var newPageIndex = _document.InsertBlankPageAfter(insertAfter);
        ShiftPageIndices(newPageIndex, 1);

        ReloadPages();
        PagesList.SelectedIndex = newPageIndex;
        ReloadFormFields();
        UpdateUndoRedoButtons();
        _setStatus?.Invoke(string.Format(_loc.Get("EditorPageAdded"), newPageIndex + 1));
    }

    private void DeletePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected()) return;
        if (_pages.Count <= 1)
        {
            ShowInfo(_loc.Get("EditorCannotDeleteLastPage"));
            return;
        }

        RegisterDocumentUndoPoint();
        var deletedIndex = _selectedPageIndex;
        _document.DeletePage(deletedIndex);
        _annotations.RemoveAll(a => a.PageIndex == deletedIndex);
        _textBlocks.RemoveAll(b => b.PageIndex == deletedIndex);
        _imageElements.RemoveAll(i => i.PageIndex == deletedIndex);
        ShiftPageIndices(deletedIndex + 1, -1);

        ReloadPages();
        PagesList.SelectedIndex = Math.Min(deletedIndex, _pages.Count - 1);
        ReloadFormFields();
        UpdateUndoRedoButtons();
    }

    private void ShiftPageIndices(int fromIndex, int delta)
    {
        if (delta == 0)
            return;

        foreach (var annotation in _annotations.Where(a => a.PageIndex >= fromIndex))
            annotation.PageIndex += delta;

        foreach (var block in _textBlocks.Where(b => b.PageIndex >= fromIndex))
            block.PageIndex += delta;

        foreach (var image in _imageElements.Where(i => i.PageIndex >= fromIndex))
            image.PageIndex += delta;
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected() || _selectedPageIndex == 0) return;
        RegisterDocumentUndoPoint();
        _document.MovePage(_selectedPageIndex, -1);
        RelocateAnnotationsForPageMove(_selectedPageIndex, _selectedPageIndex - 1);
        ReloadPages();
        PagesList.SelectedIndex = _selectedPageIndex - 1;
        UpdateUndoRedoButtons();
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePageSelected() || _selectedPageIndex >= _pages.Count - 1) return;
        RegisterDocumentUndoPoint();
        _document.MovePage(_selectedPageIndex, 1);
        RelocateAnnotationsForPageMove(_selectedPageIndex, _selectedPageIndex + 1);
        ReloadPages();
        PagesList.SelectedIndex = _selectedPageIndex + 1;
        UpdateUndoRedoButtons();
    }

    private void RelocateAnnotationsForPageMove(int fromIndex, int toIndex)
    {
        var moving = _annotations.Where(a => a.PageIndex == fromIndex).ToList();
        var displaced = _annotations.Where(a => a.PageIndex == toIndex).ToList();
        foreach (var annotation in moving)
            annotation.PageIndex = toIndex;
        foreach (var annotation in displaced)
            annotation.PageIndex = fromIndex;
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(2.5, _zoom + 0.25);
        RefreshPreview();
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(0.5, _zoom - 0.25);
        RefreshPreview();
    }

    private void PagesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedPage = GetPageItemAt(e.GetPosition(PagesList));
    }

    private void PagesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPage is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(PagesList, _draggedPage, DragDropEffects.Move);
    }

    private void PagesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PdfPageItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PagesList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PdfPageItem)) is not PdfPageItem source)
            return;

        var target = GetPageItemAt(e.GetPosition(PagesList));
        if (target is null || ReferenceEquals(source, target))
            return;

        var from = _pages.IndexOf(source);
        var to = _pages.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;

        RegisterDocumentUndoPoint();
        _document.MovePageTo(from, to);
        RelocateAnnotationsForPageReorder(from, to);
        ReloadPages();
        PagesList.SelectedIndex = to;
        UpdateUndoRedoButtons();
        _draggedPage = null;
    }

    private void RelocateAnnotationsForPageReorder(int fromIndex, int toIndex)
    {
        foreach (var annotation in _annotations)
        {
            if (annotation.PageIndex == fromIndex)
                annotation.PageIndex = toIndex;
            else if (fromIndex < toIndex && annotation.PageIndex > fromIndex && annotation.PageIndex <= toIndex)
                annotation.PageIndex--;
            else if (fromIndex > toIndex && annotation.PageIndex >= toIndex && annotation.PageIndex < fromIndex)
                annotation.PageIndex++;
        }
    }

    private PdfPageItem? GetPageItemAt(Point position)
    {
        var element = PagesList.InputHitTest(position) as DependencyObject;
        while (element is not null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);

        return element is ListBoxItem item ? item.Content as PdfPageItem : null;
    }

    private void FormFieldsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteFormFieldButton.IsEnabled = FormFieldsList.SelectedItem is PdfFormFieldItem;
        EditFormFieldButton.IsEnabled = FormFieldsList.SelectedItem is PdfFormFieldItem;

        if (FormFieldsList.SelectedItem is PdfFormFieldItem field)
        {
            SyncFormValueEditors(field);
            if (field.HasBounds && field.PageIndex >= 0 && field.PageIndex < _pages.Count)
            {
                PagesList.SelectedIndex = field.PageIndex;
                _selectedPageIndex = field.PageIndex;
                RefreshPreview();
            }
            else
            {
                RefreshAnnotationOverlay();
            }
        }
    }

    private void FormFieldsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = FormFieldsList.SelectedItem is PdfFormFieldItem;
        FormFieldEditMenuItem.IsEnabled = hasSelection;
        FormFieldDeleteMenuItem.IsEnabled = hasSelection;
    }

    private void FormFieldsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedFormField();

    private void EditFormFieldButton_Click(object sender, RoutedEventArgs e) => EditSelectedFormField();

    private void ApplyFormValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (FormFieldsList.SelectedItem is not PdfFormFieldItem field)
        {
            ShowInfo(_loc.Get("EditorSelectFormField"));
            return;
        }

        if (field.ReadOnly)
        {
            ShowInfo(_loc.Get("EditorFormReadOnly"));
            return;
        }

        ApplyFormFieldValue(field, GetFormValueEditorText(field));
    }

    private void FormValueCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (_suppressFormValueEvents || FormFieldsList.SelectedItem is not PdfFormFieldItem field ||
            FormValueCombo.SelectedItem is not string value)
            return;

        ApplyFormFieldValue(field, value);
    }

    private string GetFormValueEditorText(PdfFormFieldItem field)
    {
        if (field.IsComboField && FormValueCombo.SelectedItem is string comboValue)
            return comboValue;

        return FormValueBox.Text;
    }

    private void DeleteFormFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (FormFieldsList.SelectedItem is not PdfFormFieldItem field)
        {
            ShowInfo(_loc.Get("EditorSelectFormField"));
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(_loc.Get("EditorDeleteFormFieldConfirm"), field.Name),
            _loc.Get("AppTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            RegisterDocumentUndoPoint();
            _document.RemoveFormField(field.Name);
            ReloadFormFields();
            SyncFormValueEditors(null);
            RefreshPreview();
            UpdateUndoRedoButtons();
            _setStatus?.Invoke(string.Format(_loc.Get("EditorFormFieldDeleted"), field.Name));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ApplyFormFieldValue(PdfFormFieldItem field, string value, bool refreshOverlay = true)
    {
        if (string.Equals(field.Value, value, StringComparison.Ordinal))
            return;

        try
        {
            RegisterDocumentUndoPoint();
            _document.SetFormFieldValue(field.Name, value);
            field.Value = value;
            SyncFormValueEditors(field);
            if (refreshOverlay)
                RefreshAnnotationOverlay();
            UpdateUndoRedoButtons();
            _setStatus?.Invoke(string.Format(_loc.Get("EditorFormApplied"), field.Name));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private ComboBox CreateFormFieldOverlayCombo(PdfFormFieldItem field, double scaleY)
    {
        var combo = new ComboBox
        {
            ItemsSource = field.Options,
            SelectedItem = field.Options.Contains(field.Value) ? field.Value : field.Options[0],
            IsEditable = false,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = Math.Max(22, field.FontSize / Math.Max(1, scaleY) * 1.35),
            Tag = field
        };

        ApplyThemeToFormFieldCombo(combo);
        combo.SelectionChanged += FormFieldOverlayCombo_SelectionChanged;
        return combo;
    }

    private void FormFieldOverlayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFormValueEvents || e.AddedItems.Count == 0 ||
            sender is not ComboBox { Tag: PdfFormFieldItem field } combo)
            return;

        if (combo.SelectedItem is not string value)
            return;

        FormFieldsList.SelectedItem = field;
        ApplyFormFieldValue(field, value, refreshOverlay: false);
    }

    private static void ApplyThemeToFormFieldCombo(ComboBox combo)
    {
        if (Application.Current.TryFindResource(typeof(ComboBox)) is Style comboStyle)
            combo.Style = comboStyle;
    }

    private void FormValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || FormFieldsList.SelectedItem is not PdfFormFieldItem field)
            return;

        ApplyFormFieldValue(field, GetFormValueEditorText(field));
        e.Handled = true;
    }

    private void DropArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        var pdf = files.FirstOrDefault(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        if (pdf is not null)
            LoadDocument(pdf);
    }
}
