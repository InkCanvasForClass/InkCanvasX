using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace SkiaAnnotate.Windows;

public partial class AnnotationOverlayWindow : Window
{
    private static bool _hasLastToolbarPosition;
    private static double _lastToolbarLeft;
    private static double _lastToolbarTop;

    private bool _toolbarCollapsed;
    private bool _isToolbarDragging;
    private Point _dragStartMousePoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private const double ExpandedToolbarWidth = 620;
    private const double CollapsedToolbarWidth = 46;

    public AnnotationOverlayWindow()
    {
        InitializeComponent();

        OverlayInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        OverlayInkCanvas.IsHitTestVisible = true;
        OverlayInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Red,
            Width = 4,
            Height = 4,
            FitToCurve = true
        };

        Loaded += (_, _) =>
        {
            PlaceToolbarTopCenter();
            Activate();
            OverlayInkCanvas.Focus();
        };
        SizeChanged += (_, _) => EnsureToolbarInsideBounds();
    }

    public event Action? NextSlideRequested;
    public event Action? PreviousSlideRequested;
    public event Action? ExitRequested;

    public StrokeCollection GetCurrentStrokesSnapshot() => OverlayInkCanvas.Strokes.Clone();

    public void SetCurrentStrokes(StrokeCollection strokes)
    {
        OverlayInkCanvas.Strokes = strokes.Clone();
    }

    private void PreviousSlide_OnClick(object sender, RoutedEventArgs e) => PreviousSlideRequested?.Invoke();

    private void NextSlide_OnClick(object sender, RoutedEventArgs e) => NextSlideRequested?.Invoke();

    private void Pen_OnClick(object sender, RoutedEventArgs e)
    {
        OverlayInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        OverlayInkCanvas.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
        OverlayInkCanvas.Focus();
    }

    private void Eraser_OnClick(object sender, RoutedEventArgs e)
    {
        OverlayInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        OverlayInkCanvas.Focus();
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => OverlayInkCanvas.Strokes.Clear();

    private void ToggleToolbar_OnClick(object sender, RoutedEventArgs e)
    {
        _toolbarCollapsed = !_toolbarCollapsed;
        FloatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        ToolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.Content = _toolbarCollapsed ? "展开" : "收起";
        EnsureToolbarInsideBounds();
    }

    private void DragHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_toolbarCollapsed)
        {
            ToggleToolbar_OnClick(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        _isToolbarDragging = true;
        _dragStartMousePoint = e.GetPosition(this);
        _dragStartLeft = Canvas.GetLeft(FloatingToolbar);
        _dragStartTop = Canvas.GetTop(FloatingToolbar);
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void DragHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isToolbarDragging)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _dragStartMousePoint.X;
        var deltaY = currentPoint.Y - _dragStartMousePoint.Y;
        Canvas.SetLeft(FloatingToolbar, _dragStartLeft + deltaX);
        Canvas.SetTop(FloatingToolbar, _dragStartTop + deltaY);
        EnsureToolbarInsideBounds();
    }

    private void DragHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isToolbarDragging = false;
        Mouse.Capture(null);
        _lastToolbarLeft = Canvas.GetLeft(FloatingToolbar);
        _lastToolbarTop = Canvas.GetTop(FloatingToolbar);
        _hasLastToolbarPosition = true;
        e.Handled = true;
    }

    private void FloatingToolbar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleToolbar_OnClick(sender, e);
        }
    }

    private void Exit_OnClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.Right or Key.PageDown)
        {
            NextSlideRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.PageUp)
        {
            PreviousSlideRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Escape)
        {
            ExitRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void PlaceToolbarTopCenter()
    {
        FloatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        ToolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.Content = _toolbarCollapsed ? "展开" : "收起";

        if (_hasLastToolbarPosition)
        {
            Canvas.SetLeft(FloatingToolbar, _lastToolbarLeft);
            Canvas.SetTop(FloatingToolbar, _lastToolbarTop);
            EnsureToolbarInsideBounds();
            return;
        }

        // ICC-CE 风格：初始位置靠顶部、略偏右，减少遮挡课件标题区。
        var left = Math.Max(16, ActualWidth * 0.56 - FloatingToolbar.Width / 2);
        Canvas.SetLeft(FloatingToolbar, left);
        Canvas.SetTop(FloatingToolbar, 14);
    }

    private void EnsureToolbarInsideBounds()
    {
        if (!IsLoaded)
        {
            return;
        }

        var left = Canvas.GetLeft(FloatingToolbar);
        var top = Canvas.GetTop(FloatingToolbar);
        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        var maxLeft = Math.Max(0, ActualWidth - FloatingToolbar.Width - 8);
        var maxTop = Math.Max(0, ActualHeight - FloatingToolbar.ActualHeight - 8);
        left = Math.Clamp(left, 8, maxLeft);
        top = Math.Clamp(top, 8, maxTop);
        Canvas.SetLeft(FloatingToolbar, left);
        Canvas.SetTop(FloatingToolbar, top);
        _lastToolbarLeft = left;
        _lastToolbarTop = top;
        _hasLastToolbarPosition = true;
    }
}
