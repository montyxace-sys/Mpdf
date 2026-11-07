using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Mpdf.Models;
using Mpdf.Services;

namespace Mpdf.Controls;

public partial class PageView : UserControl
{
    private PageModel? _page;
    private DocumentSession? _session;
    private bool _toolHooked;
    private bool _dragging;
    private Point _start;
    private Shape? _preview;
    private List<Point>? _penPoints;
    private Polyline? _penPreview;

    public PageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_toolHooked)
        {
            EditorState.ToolChanged += OnToolChanged;
            _toolHooked = true;
        }
        UpdateCursor();
        if (_page != null)
        {
            _ = _page.EnsureRenderedAsync();
            RedrawOverlay();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_toolHooked)
        {
            EditorState.ToolChanged -= OnToolChanged;
            _toolHooked = false;
        }
        CancelDrag();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_page != null)
        {
            _page.PropertyChanged -= OnPagePropertyChanged;
            _page.Annotations.CollectionChanged -= OnAnnotationsChanged;
        }
        if (_session != null)
        {
            _session.SearchChanged -= OnSearchChanged;
            _session = null;
        }

        _page = DataContext as PageModel;
        if (_page != null)
        {
            _page.PropertyChanged += OnPagePropertyChanged;
            _page.Annotations.CollectionChanged += OnAnnotationsChanged;
            _session = _page.Session;
            _session.SearchChanged += OnSearchChanged;
        }

        pageImage.Source = _page?.Bitmap;
        UpdateLoadingState();
        UpdateCursor();
        RedrawOverlay();

        if (_page != null && IsLoaded) _ = _page.EnsureRenderedAsync();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_page == null) return;
        switch (e.PropertyName)
        {
            case nameof(PageModel.Bitmap):
                pageImage.Source = _page.Bitmap;
                UpdateLoadingState();
                if (_page.Bitmap == null && !_page.IsRendering) _ = _page.EnsureRenderedAsync();
                break;
            case nameof(PageModel.IsRendering):
                UpdateLoadingState();
                if (!_page.IsRendering && _page.Bitmap == null && string.IsNullOrEmpty(_page.RenderError))
                    _ = _page.EnsureRenderedAsync();
                break;
            case nameof(PageModel.RenderError):
                errorText.Text = _page.RenderError;
                errorBox.Visibility = string.IsNullOrEmpty(_page.RenderError) ? Visibility.Collapsed : Visibility.Visible;
                break;
            case nameof(PageModel.DisplayWidth):
            case nameof(PageModel.DisplayHeight):
                RedrawOverlay();
                if (_page.Bitmap == null) _ = _page.EnsureRenderedAsync();
                break;
        }
    }

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawOverlay();

    private void OnSearchChanged(object? sender, EventArgs e) => RedrawOverlay();

    private void OnToolChanged(object? sender, EventArgs e)
    {
        CancelDrag();
        UpdateCursor();
    }

    private void UpdateLoadingState()
    {
        var show = _page != null && _page.IsRendering && _page.Bitmap == null;
        loadingBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCursor()
    {
        Cursor = EditorState.CurrentTool switch
        {
            EditorTool.Pen or EditorTool.Highlight or EditorTool.Underline or EditorTool.Strikeout
                or EditorTool.Rectangle => Cursors.Cross,
            EditorTool.Eraser => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    private void RedrawOverlay()
    {
        overlay.Children.Clear();
        _preview = null;
        _penPreview = null;

        var page = _page;
        if (page == null) return;

        var width = page.DisplayWidth;
        var height = page.DisplayHeight;
        if (width <= 0 || height <= 0) return;

        foreach (var annotation in page.Annotations) DrawAnnotation(annotation, width, height);
        DrawSearchResults(page, width, height);

        if (_penPreview != null) overlay.Children.Add(_penPreview);
        if (_preview != null) overlay.Children.Add(_preview);
    }

    private void DrawAnnotation(AnnotationModel annotation, double width, double height)
    {
        var bounds = annotation.Bounds;
        if (bounds.IsEmpty) return;

        Shape? shape = null;
        switch (annotation.Kind)
        {
            case AnnotationKind.Highlight:
            {
                var fill = new SolidColorBrush(Color.FromArgb(92, annotation.Color.R, annotation.Color.G,
                    annotation.Color.B));
                shape = new Rectangle
                {
                    Fill = fill,
                    RadiusX = 1,
                    RadiusY = 1,
                    Width = Math.Max(1, bounds.Width * width),
                    Height = Math.Max(1, bounds.Height * height)
                };
                Canvas.SetLeft(shape, bounds.Left * width);
                Canvas.SetTop(shape, bounds.Top * height);
                break;
            }
            case AnnotationKind.Underline:
            case AnnotationKind.Strikeout:
            {
                var y = annotation.Kind == AnnotationKind.Underline
                    ? bounds.Bottom * height - 1
                    : bounds.Top * height + bounds.Height * height / 2;
                shape = new Line
                {
                    X1 = bounds.Left * width,
                    X2 = bounds.Right * width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(annotation.Color),
                    StrokeThickness = 2
                };
                break;
            }
            case AnnotationKind.Rectangle:
            {
                shape = new Rectangle
                {
                    Stroke = new SolidColorBrush(annotation.Color),
                    StrokeThickness = 1.8,
                    Width = Math.Max(1, bounds.Width * width),
                    Height = Math.Max(1, bounds.Height * height)
                };
                Canvas.SetLeft(shape, bounds.Left * width);
                Canvas.SetTop(shape, bounds.Top * height);
                break;
            }
            case AnnotationKind.Ink:
            {
                var line = new Polyline
                {
                    Stroke = new SolidColorBrush(annotation.Color),
                    StrokeThickness = Math.Max(1.2, annotation.StrokeWidth * width / Math.Max(1, _page!.DisplayWidthPoints)),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                foreach (var point in annotation.Points)
                    line.Points.Add(new Point(point.X * width, point.Y * height));
                shape = line;
                break;
            }
        }

        if (shape == null) return;
        shape.IsHitTestVisible = false;
        overlay.Children.Add(shape);
    }

    private void DrawSearchResults(PageModel page, double width, double height)
    {
        if (_session == null) return;
        var hits = _session.SearchHitsForPage(page.Id);
        if (hits.Count == 0) return;
        var current = _session.CurrentSearchHit;

        foreach (var hit in hits)
        {
            var isCurrent = current != null && current.PageId == hit.PageId && current.Rect == hit.Rect;
            var rectangle = new Rectangle
            {
                Width = Math.Max(2, hit.Rect.Width * width),
                Height = Math.Max(2, hit.Rect.Height * height),
                Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
                StrokeThickness = isCurrent ? 2 : 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Fill = new SolidColorBrush(isCurrent
                    ? Color.FromArgb(70, 0xF5, 0x9E, 0x0B)
                    : Color.FromArgb(30, 0xF5, 0x9E, 0x0B)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rectangle, hit.Rect.Left * width);
            Canvas.SetTop(rectangle, hit.Rect.Top * height);
            overlay.Children.Add(rectangle);
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_page == null) return;
        var point = Clamp(e.GetPosition(overlay), _page);

        switch (EditorState.CurrentTool)
        {
            case EditorTool.Eraser:
                EraseAt(point);
                e.Handled = true;
                break;
            case EditorTool.Highlight:
            case EditorTool.Underline:
            case EditorTool.Strikeout:
            case EditorTool.Rectangle:
                BeginAreaDrag(point);
                e.Handled = true;
                break;
            case EditorTool.Pen:
                BeginPen(point);
                e.Handled = true;
                break;
        }
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _page == null) return;
        var point = Clamp(e.GetPosition(overlay), _page);

        if (_penPoints != null && _penPreview != null)
        {
            var last = _penPoints[^1];
            if ((point - last).Length >= 1.6)
            {
                _penPoints.Add(point);
                _penPreview.Points.Add(point);
            }
        }
        else if (_preview != null)
        {
            UpdatePreview(_start, point);
        }
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || _page == null) return;
        var point = Clamp(e.GetPosition(overlay), _page);

        if (_penPoints != null) FinishPen();
        else FinishArea(point);
        e.Handled = true;
    }

    private void BeginAreaDrag(Point point)
    {
        _dragging = true;
        _start = point;
        _penPoints = null;
        _penPreview = null;
        _preview = new Rectangle
        {
            Stroke = new SolidColorBrush(EditorState.CurrentTool == EditorTool.Highlight
                ? EditorState.HighlightColor
                : EditorState.PenColor),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(28, 0xE1, 0x1D, 0x48)),
            IsHitTestVisible = false
        };
        overlay.Children.Add(_preview);
        UpdatePreview(point, point);
        overlay.CaptureMouse();
    }

    private void BeginPen(Point point)
    {
        _dragging = true;
        _start = point;
        _preview = null;
        _penPoints = new List<Point> { point };
        _penPreview = new Polyline
        {
            Stroke = new SolidColorBrush(EditorState.PenColor),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _penPreview.Points.Add(point);
        overlay.Children.Add(_penPreview);
        overlay.CaptureMouse();
    }

    private void UpdatePreview(Point start, Point current)
    {
        if (_preview == null) return;
        var rect = new Rect(start, current);
        _preview.Width = Math.Max(1, rect.Width);
        _preview.Height = Math.Max(1, rect.Height);
        Canvas.SetLeft(_preview, rect.Left);
        Canvas.SetTop(_preview, rect.Top);
    }

    private void FinishArea(Point end)
    {
        var page = _page;
        if (page == null) return;
        var width = page.DisplayWidth;
        var height = page.DisplayHeight;
        var rect = new Rect(_start, end);
        CancelDrag();
        if (width <= 0 || height <= 0) return;
        if (rect.Width < 3 || rect.Height < 3) return;

        var kind = EditorState.CurrentTool switch
        {
            EditorTool.Highlight => AnnotationKind.Highlight,
            EditorTool.Underline => AnnotationKind.Underline,
            EditorTool.Strikeout => AnnotationKind.Strikeout,
            _ => AnnotationKind.Rectangle
        };
        var color = kind == AnnotationKind.Highlight ? EditorState.HighlightColor : EditorState.PenColor;

        var annotation = new AnnotationModel
        {
            Kind = kind,
            Color = color,
            StrokeWidth = EditorState.PenWidth
        };
        annotation.Points.Add(new Point(rect.Left / width, rect.Top / height));
        annotation.Points.Add(new Point(rect.Right / width, rect.Bottom / height));
        page.Session.AddAnnotation(page, annotation);
    }

    private void FinishPen()
    {
        var page = _page;
        var points = _penPoints;
        CancelDrag();
        if (page == null || points == null || points.Count < 2) return;

        var width = page.DisplayWidth;
        var height = page.DisplayHeight;
        if (width <= 0 || height <= 0) return;

        var length = 0.0;
        for (var i = 1; i < points.Count; i++) length += (points[i] - points[i - 1]).Length;
        if (length < 3) return;

        var annotation = new AnnotationModel
        {
            Kind = AnnotationKind.Ink,
            Color = EditorState.PenColor,
            StrokeWidth = EditorState.PenWidth
        };
        foreach (var point in points)
            annotation.Points.Add(new Point(
                Math.Clamp(point.X / width, 0, 1),
                Math.Clamp(point.Y / height, 0, 1)));
        page.Session.AddAnnotation(page, annotation);
    }

    private void EraseAt(Point point)
    {
        var page = _page;
        if (page == null) return;
        var width = Math.Max(1, page.DisplayWidth);
        var height = Math.Max(1, page.DisplayHeight);
        var normalized = new Point(point.X / width, point.Y / height);
        const double tolerancePx = 6;

        for (var i = page.Annotations.Count - 1; i >= 0; i--)
        {
            var annotation = page.Annotations[i];
            if (!annotation.HitTest(normalized, tolerancePx / width)) continue;
            page.Session.RemoveAnnotation(page, annotation);
            return;
        }
    }

    private void CancelDrag()
    {
        _dragging = false;
        _penPoints = null;
        if (_preview != null)
        {
            overlay.Children.Remove(_preview);
            _preview = null;
        }
        if (_penPreview != null)
        {
            overlay.Children.Remove(_penPreview);
            _penPreview = null;
        }
        if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture();
    }

    private static Point Clamp(Point point, PageModel page)
    {
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(1, page.DisplayWidth)),
            Math.Clamp(point.Y, 0, Math.Max(1, page.DisplayHeight)));
    }
}
