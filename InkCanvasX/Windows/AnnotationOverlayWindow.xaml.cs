using System;
using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Input;
using Jalium.UI.Media;
using JaliumButton = Jalium.UI.Controls.Button;
using JaliumCanvas = Jalium.UI.Controls.Canvas;
using JaliumWindow = Jalium.UI.Controls.Window;

namespace SkiaAnnotate.Windows;

public sealed class AnnotationOverlayWindow : JaliumWindow
{
    private const int RgnOr = 2;
    private const double ExpandedToolbarWidth = 620;
    private const double CollapsedToolbarWidth = 46;
    private static bool _hasLastToolbarPosition;
    private static double _lastToolbarLeft;
    private static double _lastToolbarTop;

    private readonly InkCanvas _overlayInkCanvas = new();
    private readonly JaliumCanvas _toolbarHost = new();
    private readonly Border _floatingToolbar;
    private readonly StackPanel _toolbarButtonPanel;
    private readonly JaliumButton _collapsedExpandButton;
    private readonly JaliumButton _penButton;
    private readonly JaliumButton _eraserButton;
    private readonly JaliumButton _mouseModeButton;
    private readonly JaliumButton _toggleButton;
    private readonly Brush _activeButtonBackground = Brush(58, 122, 254);
    private readonly Brush _activeButtonBorder = Brush(45, 103, 215);
    private readonly Brush _activeButtonForeground = Brush(255, 255, 255);
    private readonly Brush _normalButtonBackground = Brush(250, 250, 250);
    private readonly Brush _normalButtonBorder = Brush(213, 213, 213);
    private readonly Brush _normalButtonForeground = Brush(31, 31, 31);
    private bool _toolbarCollapsed;
    private bool _isMouseMode;
    private bool _isToolbarDragging;
    private DateTime _lastToolbarClickTime;
    private Point _dragStartMousePoint;
    private double _dragStartLeft;
    private double _dragStartTop;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    public AnnotationOverlayWindow()
    {
        Title = "InkCanvasX Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Topmost = true;
        ShowInTaskbar = false;
        IsShowTitleBar = false;
        ShowActivated = true;
        Content = BuildContent();

        ConfigureInkCanvas();

        Loaded += (_, _) =>
        {
            PlaceToolbarTopCenter();
            SetActiveToolVisual(InkCanvasEditingMode.Ink, isMouseMode: false);
            Activate();
            _overlayInkCanvas.Focus();
            ApplyMouseModeHitRegion();
        };
        SizeChanged += (_, _) => EnsureToolbarInsideBounds();

        _penButton = CreateToolbarButton("", Pen_OnClick);
        _eraserButton = CreateToolbarButton("", Eraser_OnClick);
        _mouseModeButton = CreateToolbarButton("", MouseMode_OnClick);
        _toggleButton = CreateToolbarButton("", ToggleToolbar_OnClick);
        _collapsedExpandButton = CreateCollapsedButton();
        _toolbarButtonPanel = CreateToolbarButtons();
        _floatingToolbar = CreateToolbar();
        _toolbarHost.Children.Add(_floatingToolbar);
        _toolbarHost.Children.Add(_collapsedExpandButton);
    }

    public event Action? NextSlideRequested;
    public event Action? PreviousSlideRequested;
    public event Action? ExitRequested;
    public event EventHandler? Closed;

    public StrokeCollection GetCurrentStrokesSnapshot() => _overlayInkCanvas.Strokes.Clone();

    public void SetCurrentStrokes(StrokeCollection strokes)
    {
        _overlayInkCanvas.Strokes = strokes.Clone();
    }

    public void Close()
    {
        Hide();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private Grid BuildContent()
    {
        var root = new Grid
        {
            Background = Brush(1, 0, 0, 0)
        };

        _overlayInkCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _overlayInkCanvas.VerticalAlignment = VerticalAlignment.Stretch;
        _overlayInkCanvas.Margin = new Thickness(0);
        _toolbarHost.IsHitTestVisible = true;

        root.Children.Add(_overlayInkCanvas);
        root.Children.Add(_toolbarHost);
        return root;
    }

    private void ConfigureInkCanvas()
    {
        _overlayInkCanvas.IsHitTestVisible = true;
        _overlayInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        _overlayInkCanvas.Cursor = CursorType.Pen;
        _overlayInkCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromRgb(255, 0, 0),
            Width = 4,
            Height = 4,
            FitToCurve = true,
            IgnorePressure = false
        };
    }

    private Border CreateToolbar()
    {
        var toolbar = new Border
        {
            Width = ExpandedToolbarWidth,
            Background = Brush(233, 246, 247, 249),
            BorderBrush = Brush(167, 176, 186),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = CreateToolbarLayout()
        };
        toolbar.MouseLeftButtonDown += FloatingToolbar_OnMouseLeftButtonDown;
        return toolbar;
    }

    private StackPanel CreateToolbarLayout()
    {
        var layout = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        var dragHandle = new Border
        {
            Width = 30,
            Background = Brush(74, 80, 86),
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            Cursor = CursorType.ScrollAll,
            Child = new TextBlock
            {
                Text = "⋮⋮",
                Foreground = Brush(255, 255, 255),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        dragHandle.MouseLeftButtonDown += DragHandle_OnMouseLeftButtonDown;
        dragHandle.MouseMove += DragHandle_OnMouseMove;
        dragHandle.MouseLeftButtonUp += DragHandle_OnMouseLeftButtonUp;

        layout.Children.Add(dragHandle);
        layout.Children.Add(_toolbarButtonPanel);
        return layout;
    }

    private StackPanel CreateToolbarButtons()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 5, 6, 5)
        };

        panel.Children.Add(CreateToolbarButton("", (_, _) => PreviousSlideRequested?.Invoke()));
        panel.Children.Add(CreateToolbarButton("", (_, _) => NextSlideRequested?.Invoke()));
        panel.Children.Add(CreateSpacer());
        panel.Children.Add(_penButton);
        panel.Children.Add(_eraserButton);
        panel.Children.Add(_mouseModeButton);
        panel.Children.Add(CreateToolbarButton("", (_, _) => _overlayInkCanvas.ClearStrokes()));
        panel.Children.Add(CreateSpacer());
        panel.Children.Add(_toggleButton);
        panel.Children.Add(CreateToolbarButton("", (_, _) => ExitRequested?.Invoke(), isDanger: true));
        return panel;
    }

    private JaliumButton CreateToolbarButton(string content, RoutedEventHandler click, bool isDanger = false)
    {
        var button = new JaliumButton
        {
            Content = content,
            Width = 34,
            Height = 30,
            MinWidth = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 1, 2, 1),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            FontWeight = FontWeights.Normal,
            Background = isDanger ? Brush(230, 82, 69) : _normalButtonBackground,
            BorderBrush = isDanger ? Brush(199, 61, 50) : _normalButtonBorder,
            Foreground = isDanger ? Brush(255, 255, 255) : _normalButtonForeground,
            BorderThickness = new Thickness(1),
            Cursor = CursorType.Hand
        };
        button.Click += click;
        return button;
    }

    private JaliumButton CreateCollapsedButton()
    {
        var button = CreateToolbarButton("", (_, _) => CollapsedExpandButton_OnClick());
        button.Width = 28;
        button.Height = 28;
        button.MinWidth = 28;
        button.Background = Brush(74, 80, 86);
        button.BorderBrush = Brush(112, 120, 128);
        button.Foreground = Brush(255, 255, 255);
        button.Visibility = Visibility.Collapsed;
        return button;
    }

    private static Border CreateSpacer()
    {
        return new Border
        {
            Width = 1,
            Height = 20,
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brush(207, 207, 207),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void Pen_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        _overlayInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
        SetActiveToolVisual(InkCanvasEditingMode.Ink, _isMouseMode);
        _overlayInkCanvas.Focus();
    }

    private void Eraser_OnClick(object sender, RoutedEventArgs e)
    {
        SetMouseMode(false);
        _overlayInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        SetActiveToolVisual(InkCanvasEditingMode.EraseByStroke, _isMouseMode);
        _overlayInkCanvas.Focus();
    }

    private void MouseMode_OnClick(object sender, RoutedEventArgs e) => SetMouseMode(!_isMouseMode);

    private void ToggleToolbar_OnClick(object sender, RoutedEventArgs e)
    {
        _toolbarCollapsed = !_toolbarCollapsed;
        _floatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        _toolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _floatingToolbar.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _collapsedExpandButton.Visibility = _toolbarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        _toggleButton.Content = _toolbarCollapsed ? "" : "";
        EnsureToolbarInsideBounds();
        ApplyMouseModeHitRegion();
    }

    private void CollapsedExpandButton_OnClick()
    {
        if (!_toolbarCollapsed)
        {
            return;
        }

        _toolbarCollapsed = false;
        _floatingToolbar.Width = ExpandedToolbarWidth;
        _floatingToolbar.Visibility = Visibility.Visible;
        _toolbarButtonPanel.Visibility = Visibility.Visible;
        _collapsedExpandButton.Visibility = Visibility.Collapsed;
        _toggleButton.Content = "";
        EnsureToolbarInsideBounds();
        ApplyMouseModeHitRegion();
    }

    private void DragHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_toolbarCollapsed)
        {
            ToggleToolbar_OnClick(sender, e);
            e.Handled = true;
            return;
        }

        _isToolbarDragging = true;
        _dragStartMousePoint = e.GetPosition(this);
        _dragStartLeft = JaliumCanvas.GetLeft(_floatingToolbar);
        _dragStartTop = JaliumCanvas.GetTop(_floatingToolbar);
        if (sender is UIElement element)
        {
            element.CaptureMouse();
        }
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
        JaliumCanvas.SetLeft(_floatingToolbar, _dragStartLeft + deltaX);
        JaliumCanvas.SetTop(_floatingToolbar, _dragStartTop + deltaY);
        EnsureToolbarInsideBounds();
    }

    private void DragHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isToolbarDragging = false;
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }
        _lastToolbarLeft = JaliumCanvas.GetLeft(_floatingToolbar);
        _lastToolbarTop = JaliumCanvas.GetTop(_floatingToolbar);
        _hasLastToolbarPosition = true;
        ApplyMouseModeHitRegion();
        e.Handled = true;
    }

    private void FloatingToolbar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastToolbarClickTime).TotalMilliseconds <= 400)
        {
            ToggleToolbar_OnClick(sender, e);
        }
        _lastToolbarClickTime = now;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var key = e.Key.ToString();
        if (key is "Right" or "PageDown")
        {
            NextSlideRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (key is "Left" or "PageUp")
        {
            PreviousSlideRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (key == "Escape")
        {
            ExitRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void PlaceToolbarTopCenter()
    {
        _floatingToolbar.Width = _toolbarCollapsed ? CollapsedToolbarWidth : ExpandedToolbarWidth;
        _floatingToolbar.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _toolbarButtonPanel.Visibility = _toolbarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _collapsedExpandButton.Visibility = _toolbarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        _toggleButton.Content = _toolbarCollapsed ? "" : "";

        if (_hasLastToolbarPosition)
        {
            JaliumCanvas.SetLeft(_floatingToolbar, _lastToolbarLeft);
            JaliumCanvas.SetTop(_floatingToolbar, _lastToolbarTop);
            EnsureToolbarInsideBounds();
            return;
        }

        var left = Math.Max(16, ActualWidth * 0.56 - _floatingToolbar.Width / 2);
        var estimatedHeight = 42d;
        var top = Math.Max(8, ActualHeight - estimatedHeight - 26);
        JaliumCanvas.SetLeft(_floatingToolbar, left);
        JaliumCanvas.SetTop(_floatingToolbar, top);
        JaliumCanvas.SetLeft(_collapsedExpandButton, left + ExpandedToolbarWidth - 30);
        JaliumCanvas.SetTop(_collapsedExpandButton, top + 6);
    }

    private void EnsureToolbarInsideBounds()
    {
        if (!IsLoaded)
        {
            return;
        }

        var left = JaliumCanvas.GetLeft(_floatingToolbar);
        var top = JaliumCanvas.GetTop(_floatingToolbar);
        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        var maxLeft = Math.Max(0, ActualWidth - _floatingToolbar.Width - 8);
        var maxTop = Math.Max(0, ActualHeight - ActualToolbarHeight - 8);
        left = Math.Clamp(left, 8, maxLeft);
        top = Math.Clamp(top, 8, maxTop);
        JaliumCanvas.SetLeft(_floatingToolbar, left);
        JaliumCanvas.SetTop(_floatingToolbar, top);
        JaliumCanvas.SetLeft(_collapsedExpandButton, left + Math.Max(0, _floatingToolbar.Width - 34));
        JaliumCanvas.SetTop(_collapsedExpandButton, top + 4);
        _lastToolbarLeft = left;
        _lastToolbarTop = top;
        _hasLastToolbarPosition = true;
        if (_isMouseMode)
        {
            ApplyMouseModeHitRegion();
        }
    }

    private double ActualToolbarHeight => Math.Max(42d, _floatingToolbar.ActualHeight > 0 ? _floatingToolbar.ActualHeight : 42d);

    private void SetActiveToolVisual(InkCanvasEditingMode currentTool, bool isMouseMode)
    {
        ApplyButtonNormal(_penButton);
        ApplyButtonNormal(_eraserButton);
        ApplyButtonNormal(_mouseModeButton);

        if (isMouseMode)
        {
            ApplyButtonActive(_mouseModeButton);
            return;
        }

        ApplyButtonActive(currentTool == InkCanvasEditingMode.Ink ? _penButton : _eraserButton);
    }

    private void ApplyButtonActive(JaliumButton button)
    {
        button.Background = _activeButtonBackground;
        button.BorderBrush = _activeButtonBorder;
        button.Foreground = _activeButtonForeground;
    }

    private void ApplyButtonNormal(JaliumButton button)
    {
        button.Background = _normalButtonBackground;
        button.BorderBrush = _normalButtonBorder;
        button.Foreground = _normalButtonForeground;
    }

    private void SetMouseMode(bool enabled)
    {
        _isMouseMode = enabled;
        _overlayInkCanvas.IsHitTestVisible = !enabled;
        _overlayInkCanvas.Cursor = enabled ? CursorType.Arrow : CursorType.Pen;
        SetActiveToolVisual(_overlayInkCanvas.EditingMode, _isMouseMode);
        ApplyMouseModeHitRegion();
    }

    private void ApplyMouseModeHitRegion()
    {
        var hwnd = Handle;
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

        var toolbarLeft = JaliumCanvas.GetLeft(_floatingToolbar);
        var toolbarTop = JaliumCanvas.GetTop(_floatingToolbar);
        if (double.IsNaN(toolbarLeft) || double.IsNaN(toolbarTop))
        {
            return;
        }

        var toolbarWidth = Math.Max(1d, _floatingToolbar.ActualWidth > 0 ? _floatingToolbar.ActualWidth : _floatingToolbar.Width);
        var toolbarHeight = Math.Max(1d, ActualToolbarHeight);
        var mainRect = DipRectToPixelRect(new Rect(toolbarLeft, toolbarTop, toolbarWidth, toolbarHeight));
        var region = CreateRectRgn(mainRect.left, mainRect.top, mainRect.right, mainRect.bottom);
        if (region == IntPtr.Zero)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (_collapsedExpandButton.Visibility == Visibility.Visible)
        {
            var cx = JaliumCanvas.GetLeft(_collapsedExpandButton);
            var cy = JaliumCanvas.GetTop(_collapsedExpandButton);
            var cw = Math.Max(1d, _collapsedExpandButton.ActualWidth > 0 ? _collapsedExpandButton.ActualWidth : _collapsedExpandButton.Width);
            var ch = Math.Max(1d, _collapsedExpandButton.ActualHeight > 0 ? _collapsedExpandButton.ActualHeight : _collapsedExpandButton.Height);
            var collapsedRect = DipRectToPixelRect(new Rect(cx, cy, cw, ch));
            var collapsedRegion = CreateRectRgn(collapsedRect.left, collapsedRect.top, collapsedRect.right, collapsedRect.bottom);
            if (collapsedRegion != IntPtr.Zero)
            {
                CombineRgn(region, region, collapsedRegion, RgnOr);
                DeleteObject(collapsedRegion);
            }
        }

        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
    }

    private (int left, int top, int right, int bottom) DipRectToPixelRect(Rect dipRect)
    {
        var scale = Handle == IntPtr.Zero ? 1d : Math.Max(1d, GetDpiForWindow(Handle) / 96d);
        var left = (int)Math.Floor(dipRect.Left * scale);
        var top = (int)Math.Floor(dipRect.Top * scale);
        var right = (int)Math.Ceiling((dipRect.Left + dipRect.Width) * scale);
        var bottom = (int)Math.Ceiling((dipRect.Top + dipRect.Height) * scale);
        return (left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private static Brush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    private static Brush Brush(byte a, byte r, byte g, byte b) => new SolidColorBrush(Color.FromArgb(a, r, g, b));
}
