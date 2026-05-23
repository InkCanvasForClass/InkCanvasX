using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Jalium.UI.Controls.Ink;
using WpfButton = System.Windows.Controls.Button;
using WpfInkCanvasEditingMode = System.Windows.Controls.InkCanvasEditingMode;
using WpfInkCanvas = System.Windows.Controls.InkCanvas;

namespace SkiaAnnotate.Windows;

public partial class AnnotationOverlayWindow : Window
{
    private const int RgnOr = 2;
    private static bool _hasLastToolbarPosition;
    private static double _lastToolbarLeft;
    private static double _lastToolbarTop;

    private bool _toolbarCollapsed;
    private bool _isMouseMode;
    private bool _isToolbarDragging;
    private Point _dragStartMousePoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private const double ExpandedToolbarWidth = 620;
    private const double CollapsedToolbarWidth = 46;
    private readonly Brush _activeButtonBackground = new SolidColorBrush(Color.FromRgb(58, 122, 254));
    private readonly Brush _activeButtonBorder = new SolidColorBrush(Color.FromRgb(45, 103, 215));
    private readonly Brush _activeButtonForeground = Brushes.White;
    private readonly Brush _normalButtonBackground = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    private readonly Brush _normalButtonBorder = new SolidColorBrush(Color.FromRgb(213, 213, 213));
    private readonly Brush _normalButtonForeground = new SolidColorBrush(Color.FromRgb(31, 31, 31));
    private HwndSource? _hwndSource;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    public AnnotationOverlayWindow()
    {
        InitializeComponent();

        ConfigureInkCanvas();

        Loaded += (_, _) =>
        {
            PlaceToolbarTopCenter();
            SetActiveToolVisual(WpfInkCanvasEditingMode.Ink, isMouseMode: false);
            Activate();
            OverlayInkCanvas.Focus();
        };
        SizeChanged += (_, _) => EnsureToolbarInsideBounds();
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                _hwndSource = source;
                ApplyMouseModeHitRegion();
            }
        };
    }

    public event Action? NextSlideRequested;
    public event Action? PreviousSlideRequested;
    public event Action? ExitRequested;

    public StrokeCollection GetCurrentStrokesSnapshot() => OverlayInkCanvas.Strokes.Clone();

    public void SetCurrentStrokes(StrokeCollection strokes)
    {
        OverlayInkCanvas.Strokes = strokes.Clone();
    }

    private void ConfigureInkCanvas()
    {
        OverlayInkCanvas.IsHitTestVisible = true;
        OverlayInkCanvas.EditingMode = WpfInkCanvasEditingMode.Ink;
        OverlayInkCanvas.DefaultDrawingAttributes = new System.Windows.Ink.DrawingAttributes
        {
            Color = Colors.Red,
            Width = 4,
            Height = 4,
            FitToCurve = true,
            IgnorePressure = false
        };
    }

    private void PreviousSlide_OnClick(object sender, RoutedEventArgs e) => PreviousSlideRequested?.Invoke();

    private void NextSlide_OnClick(object sender, RoutedEventArgs e) => NextSlideRequested?.Invoke();

    private void Pen_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        OverlayInkCanvas.EditingMode = WpfInkCanvasEditingMode.Ink;
        SetActiveToolVisual(WpfInkCanvasEditingMode.Ink, _isMouseMode);
        OverlayInkCanvas.Focus();
    }

    private void Eraser_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        OverlayInkCanvas.EditingMode = WpfInkCanvasEditingMode.EraseByStroke;
        SetActiveToolVisual(WpfInkCanvasEditingMode.EraseByStroke, _isMouseMode);
        OverlayInkCanvas.Focus();
    }

    private void MouseMode_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(!_isMouseMode);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => OverlayInkCanvas.Strokes.Clear();

    private void ToggleToolbar_OnClick(object sender, RoutedEventArgs e)
    {
        _toolbarCollapsed = !_toolbarCollapsed;
        FloatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        ToolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        FloatingToolbar.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedExpandButton.Visibility = _toolbarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.Content = _toolbarCollapsed ? "" : "";
        EnsureToolbarInsideBounds();
        ApplyMouseModeHitRegion();
    }

    private void CollapsedExpandButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_toolbarCollapsed)
        {
            return;
        }

        _toolbarCollapsed = false;
        FloatingToolbar.Width = ExpandedToolbarWidth;
        FloatingToolbar.Visibility = Visibility.Visible;
        ToolbarButtonPanel.Visibility = Visibility.Visible;
        CollapsedExpandButton.Visibility = Visibility.Collapsed;
        ToggleButton.Content = "";
        EnsureToolbarInsideBounds();
        ApplyMouseModeHitRegion();
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
        _dragStartLeft = System.Windows.Controls.Canvas.GetLeft(FloatingToolbar);
        _dragStartTop = System.Windows.Controls.Canvas.GetTop(FloatingToolbar);
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
        System.Windows.Controls.Canvas.SetLeft(FloatingToolbar, _dragStartLeft + deltaX);
        System.Windows.Controls.Canvas.SetTop(FloatingToolbar, _dragStartTop + deltaY);
        EnsureToolbarInsideBounds();
    }

    private void DragHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isToolbarDragging = false;
        Mouse.Capture(null);
        _lastToolbarLeft = System.Windows.Controls.Canvas.GetLeft(FloatingToolbar);
        _lastToolbarTop = System.Windows.Controls.Canvas.GetTop(FloatingToolbar);
        _hasLastToolbarPosition = true;
        ApplyMouseModeHitRegion();
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
        FloatingToolbar.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedExpandButton.Visibility = _toolbarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.Content = _toolbarCollapsed ? "" : "";

        if (_hasLastToolbarPosition)
        {
            System.Windows.Controls.Canvas.SetLeft(FloatingToolbar, _lastToolbarLeft);
            System.Windows.Controls.Canvas.SetTop(FloatingToolbar, _lastToolbarTop);
            EnsureToolbarInsideBounds();
            return;
        }

        var left = Math.Max(16, ActualWidth * 0.56 - FloatingToolbar.Width / 2);
        var estimatedHeight = 42d;
        var top = Math.Max(8, ActualHeight - estimatedHeight - 26);
        System.Windows.Controls.Canvas.SetLeft(FloatingToolbar, left);
        System.Windows.Controls.Canvas.SetTop(FloatingToolbar, top);
        System.Windows.Controls.Canvas.SetLeft(CollapsedExpandButton, left + ExpandedToolbarWidth - 30);
        System.Windows.Controls.Canvas.SetTop(CollapsedExpandButton, top + 6);
    }

    private void EnsureToolbarInsideBounds()
    {
        if (!IsLoaded)
        {
            return;
        }

        var left = System.Windows.Controls.Canvas.GetLeft(FloatingToolbar);
        var top = System.Windows.Controls.Canvas.GetTop(FloatingToolbar);
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
        System.Windows.Controls.Canvas.SetLeft(FloatingToolbar, left);
        System.Windows.Controls.Canvas.SetTop(FloatingToolbar, top);
        System.Windows.Controls.Canvas.SetLeft(CollapsedExpandButton, left + Math.Max(0, FloatingToolbar.Width - 34));
        System.Windows.Controls.Canvas.SetTop(CollapsedExpandButton, top + 4);
        _lastToolbarLeft = left;
        _lastToolbarTop = top;
        _hasLastToolbarPosition = true;
        if (_isMouseMode)
        {
            ApplyMouseModeHitRegion();
        }
    }

    private void SetActiveToolVisual(WpfInkCanvasEditingMode currentTool, bool isMouseMode)
    {
        ApplyButtonNormal(PenButton);
        ApplyButtonNormal(EraserButton);
        ApplyButtonNormal(MouseModeButton);

        if (isMouseMode)
        {
            ApplyButtonActive(MouseModeButton);
            return;
        }

        if (currentTool == WpfInkCanvasEditingMode.Ink)
        {
            ApplyButtonActive(PenButton);
            return;
        }

        ApplyButtonActive(EraserButton);
    }

    private void ApplyButtonActive(WpfButton button)
    {
        button.Background = _activeButtonBackground;
        button.BorderBrush = _activeButtonBorder;
        button.Foreground = _activeButtonForeground;
    }

    private void ApplyButtonNormal(WpfButton button)
    {
        button.Background = _normalButtonBackground;
        button.BorderBrush = _normalButtonBorder;
        button.Foreground = _normalButtonForeground;
    }

    private void SetMouseMode(bool enabled)
    {
        _isMouseMode = enabled;
        OverlayInkCanvas.IsHitTestVisible = !enabled;
        OverlayInkCanvas.Cursor = enabled ? Cursors.Arrow : Cursors.Pen;
        SetActiveToolVisual(OverlayInkCanvas.EditingMode, _isMouseMode);
        ApplyMouseModeHitRegion();
    }

    private void ApplyMouseModeHitRegion()
    {
        if (_hwndSource is null)
        {
            return;
        }

        var hwnd = _hwndSource.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!_isMouseMode)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        UpdateLayout();

        var toolbarLeft = System.Windows.Controls.Canvas.GetLeft(FloatingToolbar);
        var toolbarTop = System.Windows.Controls.Canvas.GetTop(FloatingToolbar);
        if (double.IsNaN(toolbarLeft) || double.IsNaN(toolbarTop))
        {
            return;
        }

        var toolbarWidth = Math.Max(1d, FloatingToolbar.ActualWidth > 0 ? FloatingToolbar.ActualWidth : FloatingToolbar.Width);
        var toolbarHeight = Math.Max(1d, FloatingToolbar.ActualHeight > 0 ? FloatingToolbar.ActualHeight : 42d);

        var mainRect = DipRectToPixelRect(new Rect(toolbarLeft, toolbarTop, toolbarWidth, toolbarHeight));
        var region = CreateRectRgn(mainRect.left, mainRect.top, mainRect.right, mainRect.bottom);
        if (region == IntPtr.Zero)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (CollapsedExpandButton.Visibility == Visibility.Visible)
        {
            var cx = System.Windows.Controls.Canvas.GetLeft(CollapsedExpandButton);
            var cy = System.Windows.Controls.Canvas.GetTop(CollapsedExpandButton);
            var cw = Math.Max(1d, CollapsedExpandButton.ActualWidth > 0 ? CollapsedExpandButton.ActualWidth : CollapsedExpandButton.Width);
            var ch = Math.Max(1d, CollapsedExpandButton.ActualHeight > 0 ? CollapsedExpandButton.ActualHeight : CollapsedExpandButton.Height);
            var collapsedRect = DipRectToPixelRect(new Rect(cx, cy, cw, ch));
            var collapsedRegion = CreateRectRgn(
                collapsedRect.left,
                collapsedRect.top,
                collapsedRect.right,
                collapsedRect.bottom);
            if (collapsedRegion != IntPtr.Zero)
            {
                CombineRgn(region, region, collapsedRegion, RgnOr);
                DeleteObject(collapsedRegion);
            }
        }

        var result = SetWindowRgn(hwnd, region, true);
        if (result == 0)
        {
            DeleteObject(region);
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
    }

    private (int left, int top, int right, int bottom) DipRectToPixelRect(Rect dipRect)
    {
        var matrix = _hwndSource?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var left = (int)Math.Floor(dipRect.Left * matrix.M11);
        var top = (int)Math.Floor(dipRect.Top * matrix.M22);
        var right = (int)Math.Ceiling((dipRect.Left + dipRect.Width) * matrix.M11);
        var bottom = (int)Math.Ceiling((dipRect.Top + dipRect.Height) * matrix.M22);
        return (left, top, right, bottom);
    }
}
