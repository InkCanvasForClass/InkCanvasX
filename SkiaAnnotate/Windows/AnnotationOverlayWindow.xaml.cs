using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaAnnotate.Controls;

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
    private bool _isToolbarHovered;
    private Point _dragStartMousePoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private const double ExpandedToolbarWidth = 560;
    private const double CollapsedToolbarWidth = 46;
    private readonly Brush _activeButtonBackground = new SolidColorBrush(Color.FromRgb(47, 109, 246));
    private readonly Brush _activeButtonBorder = new SolidColorBrush(Color.FromRgb(29, 78, 216));
    private readonly Brush _activeButtonForeground = Brushes.White;
    private readonly Brush _normalButtonBackground = new SolidColorBrush(Color.FromRgb(229, 231, 235));
    private readonly Brush _normalButtonBorder = Brushes.Transparent;
    private readonly Brush _normalButtonForeground = new SolidColorBrush(Color.FromRgb(39, 39, 42));
    private HwndSource? _hwndSource;
    private const double ToolbarDimOpacity = 0.72;
    private const double ToolbarActiveOpacity = 0.96;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    public AnnotationOverlayWindow()
    {
        InitializeComponent();

        OverlayInkCanvas.IsHitTestVisible = true;
        OverlayInkCanvas.Tool = CanvasInkTool.Pen;
        OverlayInkCanvas.PenColor = Colors.Red;
        OverlayInkCanvas.PenWidth = 4f;

        Loaded += (_, _) =>
        {
            PlaceToolbarTopCenter();
            SetActiveToolVisual(CanvasInkTool.Pen, isMouseMode: false);
            UpdateToolbarOpacity();
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

    public StrokeCollection GetCurrentStrokesSnapshot()
    {
        OverlayInkCanvas.CommitActiveStroke();
        return OverlayInkCanvas.GetBoundStrokes().Clone();
    }

    public void SetCurrentStrokes(StrokeCollection strokes)
    {
        OverlayInkCanvas.BindStrokes(strokes.Clone());
    }

    private void PreviousSlide_OnClick(object sender, RoutedEventArgs e) => PreviousSlideRequested?.Invoke();

    private void NextSlide_OnClick(object sender, RoutedEventArgs e) => NextSlideRequested?.Invoke();

    private void Pen_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        OverlayInkCanvas.Tool = CanvasInkTool.Pen;
        SetActiveToolVisual(CanvasInkTool.Pen, _isMouseMode);
        OverlayInkCanvas.Focus();
    }

    private void Eraser_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        OverlayInkCanvas.Tool = CanvasInkTool.Eraser;
        SetActiveToolVisual(CanvasInkTool.Eraser, _isMouseMode);
        OverlayInkCanvas.Focus();
    }

    private void MouseMode_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(!_isMouseMode);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e) => OverlayInkCanvas.Clear();

    private void ToggleToolbar_OnClick(object sender, RoutedEventArgs e)
    {
        _toolbarCollapsed = !_toolbarCollapsed;
        FloatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        ToolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        FloatingToolbar.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedExpandButton.Visibility = _toolbarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.Content = _toolbarCollapsed ? "\uE76B" : "\uE76C";
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
        ToggleButton.Content = "\uE76C";
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

    private void FloatingToolbar_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isToolbarHovered = true;
        UpdateToolbarOpacity();
    }

    private void FloatingToolbar_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isToolbarHovered = false;
        UpdateToolbarOpacity();
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
        ToggleButton.Content = _toolbarCollapsed ? "\uE76B" : "\uE76C";

        if (_hasLastToolbarPosition)
        {
            Canvas.SetLeft(FloatingToolbar, _lastToolbarLeft);
            Canvas.SetTop(FloatingToolbar, _lastToolbarTop);
            EnsureToolbarInsideBounds();
            return;
        }

        // 默认靠近右上边缘，尽量避开页面主体内容区域。
        var left = Math.Max(8, ActualWidth - FloatingToolbar.Width - 20);
        var top = 14d;
        Canvas.SetLeft(FloatingToolbar, left);
        Canvas.SetTop(FloatingToolbar, top);
        Canvas.SetLeft(CollapsedExpandButton, left + ExpandedToolbarWidth - 30);
        Canvas.SetTop(CollapsedExpandButton, top + 6);
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
        Canvas.SetLeft(CollapsedExpandButton, left + Math.Max(0, FloatingToolbar.Width - 34));
        Canvas.SetTop(CollapsedExpandButton, top + 4);
        _lastToolbarLeft = left;
        _lastToolbarTop = top;
        _hasLastToolbarPosition = true;
        if (_isMouseMode)
        {
            ApplyMouseModeHitRegion();
        }

        UpdateToolbarOpacity();
    }

    private void SetActiveToolVisual(CanvasInkTool currentTool, bool isMouseMode)
    {
        ApplyButtonNormal(PenButton);
        ApplyButtonNormal(EraserButton);
        ApplyButtonNormal(MouseModeButton);

        if (isMouseMode)
        {
            ApplyButtonActive(MouseModeButton);
            return;
        }

        if (currentTool == CanvasInkTool.Pen)
        {
            ApplyButtonActive(PenButton);
            return;
        }

        ApplyButtonActive(EraserButton);
    }

    private void ApplyButtonActive(Button button)
    {
        button.Background = _activeButtonBackground;
        button.BorderBrush = _activeButtonBorder;
        button.Foreground = _activeButtonForeground;
    }

    private void ApplyButtonNormal(Button button)
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
        SetActiveToolVisual(OverlayInkCanvas.Tool, _isMouseMode);
        UpdateToolbarOpacity();
        ApplyMouseModeHitRegion();
    }

    private void UpdateToolbarOpacity()
    {
        if (_toolbarCollapsed)
        {
            FloatingToolbar.Opacity = ToolbarActiveOpacity;
            return;
        }

        FloatingToolbar.Opacity = _isToolbarHovered || !_isMouseMode
            ? ToolbarActiveOpacity
            : ToolbarDimOpacity;
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
            // Remove custom region: whole window receives hit test.
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        // Ensure ActualWidth/ActualHeight are up-to-date before building region.
        UpdateLayout();

        // Mouse mode: only toolbar region remains hit-testable, rest will click-through.
        var toolbarLeft = Canvas.GetLeft(FloatingToolbar);
        var toolbarTop = Canvas.GetTop(FloatingToolbar);
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
            // Safety fallback: don't break UI hit area.
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (CollapsedExpandButton.Visibility == Visibility.Visible)
        {
            var cx = Canvas.GetLeft(CollapsedExpandButton);
            var cy = Canvas.GetTop(CollapsedExpandButton);
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

        // After successful call, system owns region handle; on failure we must free it.
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
