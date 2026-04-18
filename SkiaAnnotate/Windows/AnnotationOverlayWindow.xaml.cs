using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaAnnotate.Controls;

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
    private readonly Brush _activeButtonBackground = new SolidColorBrush(Color.FromRgb(58, 122, 254));
    private readonly Brush _activeButtonBorder = new SolidColorBrush(Color.FromRgb(45, 103, 215));
    private readonly Brush _activeButtonForeground = Brushes.White;
    private readonly Brush _normalButtonBackground = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    private readonly Brush _normalButtonBorder = new SolidColorBrush(Color.FromRgb(213, 213, 213));
    private readonly Brush _normalButtonForeground = new SolidColorBrush(Color.FromRgb(31, 31, 31));

    public AnnotationOverlayWindow()
    {
        InitializeComponent();

        OverlayInkCanvas.IsHitTestVisible = true;
        OverlayInkCanvas.Tool = SkiaInkTool.Pen;
        OverlayInkCanvas.PenColor = Colors.Red;
        OverlayInkCanvas.PenWidth = 4f;

        Loaded += (_, _) =>
        {
            PlaceToolbarTopCenter();
            SetActiveToolVisual(isPenActive: true);
            Activate();
            OverlayInkCanvas.Focus();
        };
        SizeChanged += (_, _) => EnsureToolbarInsideBounds();
    }

    public event Action? NextSlideRequested;
    public event Action? PreviousSlideRequested;
    public event Action? ExitRequested;

    public List<SkiaStroke> GetCurrentStrokesSnapshot()
    {
        OverlayInkCanvas.CommitActiveStroke();
        return OverlayInkCanvas.GetBoundStrokes();
    }

    public void SetCurrentStrokes(List<SkiaStroke> strokes)
    {
        OverlayInkCanvas.BindStrokes(strokes);
    }

    private void PreviousSlide_OnClick(object sender, RoutedEventArgs e) => PreviousSlideRequested?.Invoke();

    private void NextSlide_OnClick(object sender, RoutedEventArgs e) => NextSlideRequested?.Invoke();

    private void Pen_OnClick(object sender, RoutedEventArgs e)
    {
        OverlayInkCanvas.Tool = SkiaInkTool.Pen;
        SetActiveToolVisual(isPenActive: true);
        OverlayInkCanvas.Focus();
    }

    private void Eraser_OnClick(object sender, RoutedEventArgs e)
    {
        OverlayInkCanvas.Tool = SkiaInkTool.Eraser;
        SetActiveToolVisual(isPenActive: false);
        OverlayInkCanvas.Focus();
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

        // ICC-CE 风格：初始靠近底部中间，避免遮挡页面主内容。
        var left = Math.Max(16, ActualWidth * 0.56 - FloatingToolbar.Width / 2);
        var estimatedHeight = 42d;
        var top = Math.Max(8, ActualHeight - estimatedHeight - 26);
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
    }

    private void SetActiveToolVisual(bool isPenActive)
    {
        if (isPenActive)
        {
            ApplyButtonActive(PenButton);
            ApplyButtonNormal(EraserButton);
            return;
        }

        ApplyButtonNormal(PenButton);
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
}
