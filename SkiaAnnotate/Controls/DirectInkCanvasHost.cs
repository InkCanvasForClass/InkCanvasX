using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SkiaAnnotate.Services;
using Vortice.Direct2D1;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.DirectComposition;

namespace SkiaAnnotate.Controls;

public sealed class DirectInkCanvasHost : HwndHost, IDisposable
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WmPaint = 0x000F;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmSize = 0x0005;
    private const int SwpNoZOrder = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    private IntPtr _hwnd = IntPtr.Zero;

    private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
    private List<InkStroke> _strokes = new();
    private bool _sawPressureVariation;

    private ID2D1Factory? _factory;
    private ID2D1DCRenderTarget? _renderTarget;
    private IDCompositionDevice? _compositionDevice;
    private IDCompositionTarget? _compositionTarget;
    private IDCompositionVisual? _compositionVisual;
    private bool _disposed;

    public InkTool Tool { get; set; } = InkTool.Pen;
    public Color PenColor { get; set; } = Colors.Red;
    public float PenWidth { get; set; } = 4f;
    public float EraserRadius { get; set; } = 18f;

    public bool MultiTouchEnabled { get; set; } = true;
    public bool StylusPressureEnabled { get; set; } = true;
    public bool SmoothingEnabled { get; set; } = true;

    public DirectInkCanvasHost()
    {
        Focusable = true;
        Cursor = Cursors.Pen;
        IsHitTestVisible = true;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseLeave += OnMouseUp;

        TouchDown += OnTouchDown;
        TouchMove += OnTouchMove;
        TouchUp += OnTouchUp;
        TouchLeave += OnTouchUp;

        StylusDown += OnStylusDown;
        StylusMove += OnStylusMove;
        StylusUp += OnStylusUp;
        StylusLeave += OnStylusUp;
    }

    public void BindStrokes(List<InkStroke> strokes)
    {
        _strokes = strokes;
        _activeStrokes.Clear();
        RequestRedraw();
    }

    public List<InkStroke> GetBoundStrokes() => _strokes;

    public void CommitActiveStroke()
    {
        foreach (var id in _activeStrokes.Keys.ToList())
        {
            CommitStroke(id);
        }

        RequestRedraw();
    }

    public void Clear()
    {
        _strokes.Clear();
        _activeStrokes.Clear();
        RequestRedraw();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(
            0,
            "static",
            null,
            WsChild | WsVisible,
            0,
            0,
            (int)Math.Max(1d, ActualWidth),
            (int)Math.Max(1d, ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("创建 DirectInkCanvasHost 子窗口失败。");
        }

        EnsureRenderTarget();
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseRenderResources();
        if (hwnd.Handle != IntPtr.Zero)
        {
            DestroyWindow(hwnd.Handle);
        }
        _hwnd = IntPtr.Zero;
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEraseBkgnd:
                handled = true;
                return new IntPtr(1);
            case WmPaint:
                Paint(hwnd);
                handled = true;
                return IntPtr.Zero;
            case WmSize:
                RequestRedraw();
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, (int)Math.Max(1, finalSize.Width), (int)Math.Max(1, finalSize.Height), SwpNoZOrder);
        }

        return base.ArrangeOverride(finalSize);
    }

    private void EnsureRenderTarget()
    {
        if (_factory is null)
        {
            _factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.SingleThreaded);
        }

        if (_renderTarget is not null)
        {
            return;
        }

        var rtProps = new RenderTargetProperties
        {
            Type = RenderTargetType.Hardware,
            PixelFormat = new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.Unknown, Vortice.DCommon.AlphaMode.Premultiplied),
            Usage = RenderTargetUsage.None,
            MinLevel = FeatureLevel.Default
        };

        _renderTarget = _factory.CreateDCRenderTarget(rtProps);

        // Keep a DirectComposition device/visual chain alive for future swapchain path.
        // Current path still uses D2D DC render target to keep migration stable.
        if (_compositionDevice is null && _hwnd != IntPtr.Zero)
        {
            try
            {
                _compositionDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(null!);
                _compositionDevice.CreateTargetForHwnd(_hwnd, false, out _compositionTarget);
                _compositionDevice.CreateVisual(out _compositionVisual);
                _compositionTarget?.SetRoot(_compositionVisual);
                _compositionDevice.Commit();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"DirectComposition 初始化失败，回退 Direct2D DC 路径：{ex.Message}");
            }
        }
    }

    private void Paint(IntPtr hwnd)
    {
        if (_renderTarget is null)
        {
            EnsureRenderTarget();
        }

        if (_renderTarget is null)
        {
            return;
        }

        var hdc = BeginPaint(hwnd, out var paintStruct);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            GetClientRect(hwnd, out var rect);
            _renderTarget.BindDC(hdc, new Vortice.RawRect(rect.Left, rect.Top, rect.Right, rect.Bottom));
            _renderTarget.BeginDraw();
            _renderTarget.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0));

            foreach (var stroke in _strokes)
            {
                DrawStroke(stroke);
            }

            foreach (var active in _activeStrokes.Values)
            {
                DrawStroke(new InkStroke
                {
                    Color = PenColor,
                    Width = PenWidth,
                    Points = active.Points
                });
            }

            _renderTarget.EndDraw();
        }
        finally
        {
            EndPaint(hwnd, ref paintStruct);
        }
    }

    private void DrawStroke(InkStroke stroke)
    {
        if (_renderTarget is null || stroke.Points.Count == 0)
        {
            return;
        }

        using var brush = _renderTarget.CreateSolidColorBrush(new Vortice.Mathematics.Color4(
            stroke.Color.ScR,
            stroke.Color.ScG,
            stroke.Color.ScB,
            stroke.Color.ScA));

        if (stroke.Points.Count == 1)
        {
            var p = stroke.Points[0];
            var w = p.W > 0 ? p.W : stroke.Width;
            _renderTarget.DrawEllipse(new Ellipse(new Vector2(p.X, p.Y), w * 0.5f, w * 0.5f), brush, w);
            return;
        }

        var drawPoints = SmoothingEnabled && stroke.Points.Count >= 4
            ? BuildInkeysStyleSmoothPoints(stroke.Points)
            : stroke.Points;

        for (var i = 1; i < drawPoints.Count; i++)
        {
            var a = drawPoints[i - 1];
            var b = drawPoints[i];
            var wA = a.W > 0 ? a.W : stroke.Width;
            var wB = b.W > 0 ? b.W : stroke.Width;
            var w = (wA + wB) * 0.5f;
            _renderTarget.DrawLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), brush, w);
        }
    }

    private void RequestRedraw()
    {
        if (_hwnd != IntPtr.Zero)
        {
            InvalidateRect(_hwnd, IntPtr.Zero, false);
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Stylus.CurrentStylusDevice is not null)
        {
            return;
        }

        Focus();
        CaptureMouse();
        var p = e.GetPosition(this);
        if (Tool == InkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            return;
        }

        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), true);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Stylus.CurrentStylusDevice is not null)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Tool == InkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            return;
        }

        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), false);
    }

    private void OnMouseUp(object? sender, EventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (Tool == InkTool.Pen)
        {
            CommitStroke(-1);
        }
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (!MultiTouchEnabled && _activeStrokes.Count > 0)
        {
            return;
        }

        Focus();
        CaptureTouch(e.TouchDevice);
        var id = e.TouchDevice.Id;
        var p = e.GetTouchPoint(this).Position;
        if (Tool == InkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), true);
        e.Handled = true;
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        var id = e.TouchDevice.Id;
        if (!_activeStrokes.ContainsKey(id))
        {
            return;
        }

        var p = e.GetTouchPoint(this).Position;
        if (Tool == InkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), false);
        e.Handled = true;
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        ReleaseTouchCapture(e.TouchDevice);
        if (Tool == InkTool.Pen)
        {
            CommitStroke(e.TouchDevice.Id);
        }

        e.Handled = true;
    }

    private void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        Focus();
        CaptureStylus();

        var id = StylusId(e.StylusDevice);
        var sample = GetStylusSample(e, out var hasPressure);
        if (hasPressure)
        {
            _sawPressureVariation = true;
        }

        if (Tool == InkTool.Eraser)
        {
            EraseAt(sample.X, sample.Y);
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, sample, true);
        e.Handled = true;
    }

    private void OnStylusMove(object sender, StylusEventArgs e)
    {
        var id = StylusId(e.StylusDevice);
        if (!_activeStrokes.ContainsKey(id))
        {
            return;
        }

        var sample = GetStylusSample(e, out var hasPressure);
        if (hasPressure)
        {
            _sawPressureVariation = true;
        }

        if (Tool == InkTool.Eraser)
        {
            EraseAt(sample.X, sample.Y);
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, sample, false);
        e.Handled = true;
    }

    private void OnStylusUp(object sender, StylusEventArgs e)
    {
        if (IsStylusCaptured)
        {
            ReleaseStylusCapture();
        }

        if (Tool == InkTool.Pen)
        {
            CommitStroke(StylusId(e.StylusDevice));
        }

        e.Handled = true;
    }

    private void StartOrUpdateStroke(int id, PointerSample sample, bool isStart)
    {
        if (Tool != InkTool.Pen)
        {
            return;
        }

        if (isStart)
        {
            _activeStrokes[id] = new ActiveStroke(sample.X, sample.Y, sample.TimestampMs, SmoothingEnabled);
        }

        if (!_activeStrokes.TryGetValue(id, out var active))
        {
            return;
        }

        var dtMs = Math.Max(1L, sample.TimestampMs - active.LastT);
        var dt = dtMs / 1000f;
        var dx = sample.X - active.LastX;
        var dy = sample.Y - active.LastY;
        var dist = (float)Math.Sqrt(dx * dx + dy * dy);
        var speed = dist / dt;

        var x = sample.X;
        var y = sample.Y;

        if (SmoothingEnabled && active.FilterX is not null && active.FilterY is not null)
        {
            x = active.FilterX.Filter(x, dt, speed);
            y = active.FilterY.Filter(y, dt, speed);
        }

        var usePressure = StylusPressureEnabled && _sawPressureVariation && sample.Pressure > 0f;
        var p = usePressure ? Clamp(sample.Pressure, 0f, 1f) : 0f;
        var w = PenWidth;
        if (usePressure)
        {
            w *= 0.25f + 0.75f * p;
        }

        var speedFactor = Clamp(1.15f - (speed / 2200f), 0.45f, 1.25f);
        w *= speedFactor;
        if (SmoothingEnabled && active.FilterW is not null)
        {
            w = active.FilterW.Filter(w, dt, speed);
        }

        // Inkeys-like adaptive sampling:
        // low speed keeps denser points (smoother detail), high speed thins samples (less jitter/overdraw).
        var minDist = Clamp(0.20f + speed / 4800f, 0.20f, 1.20f);
        if (!isStart && dist < minDist)
        {
            active.LastX = sample.X;
            active.LastY = sample.Y;
            active.LastT = sample.TimestampMs;
            return;
        }

        active.Points.Add(new InkPoint
        {
            X = x,
            Y = y,
            W = w,
            P = p,
            T = sample.TimestampMs
        });

        active.LastX = sample.X;
        active.LastY = sample.Y;
        active.LastT = sample.TimestampMs;
        RequestRedraw();
    }

    private static List<InkPoint> BuildInkeysStyleSmoothPoints(List<InkPoint> source)
    {
        if (source.Count < 4)
        {
            return source;
        }

        // Catmull-Rom spline reconstruction for pen trajectories.
        // It preserves local shape and feels closer to natural handwriting than plain polyline.
        var result = new List<InkPoint>(source.Count * 3)
        {
            source[0]
        };

        for (var i = 0; i < source.Count - 1; i++)
        {
            var p0 = source[Math.Max(0, i - 1)];
            var p1 = source[i];
            var p2 = source[i + 1];
            var p3 = source[Math.Min(source.Count - 1, i + 2)];

            var segLen = Distance(p1, p2);
            var subdivisions = (int)Clamp(segLen / 1.8f, 1f, 6f);

            for (var step = 1; step <= subdivisions; step++)
            {
                var t = step / (float)subdivisions;
                result.Add(CatmullInterpolate(p0, p1, p2, p3, t));
            }
        }

        return result;
    }

    private static InkPoint CatmullInterpolate(InkPoint p0, InkPoint p1, InkPoint p2, InkPoint p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        var x = 0.5f * ((2f * p1.X) + (-p0.X + p2.X) * t + (2f * p0.X - 5f * p1.X + 4f * p2.X - p3.X) * t2 + (-p0.X + 3f * p1.X - 3f * p2.X + p3.X) * t3);
        var y = 0.5f * ((2f * p1.Y) + (-p0.Y + p2.Y) * t + (2f * p0.Y - 5f * p1.Y + 4f * p2.Y - p3.Y) * t2 + (-p0.Y + 3f * p1.Y - 3f * p2.Y + p3.Y) * t3);
        var w = 0.5f * ((2f * p1.W) + (-p0.W + p2.W) * t + (2f * p0.W - 5f * p1.W + 4f * p2.W - p3.W) * t2 + (-p0.W + 3f * p1.W - 3f * p2.W + p3.W) * t3);
        var pressure = 0.5f * ((2f * p1.P) + (-p0.P + p2.P) * t + (2f * p0.P - 5f * p1.P + 4f * p2.P - p3.P) * t2 + (-p0.P + 3f * p1.P - 3f * p2.P + p3.P) * t3);

        return new InkPoint
        {
            X = x,
            Y = y,
            W = Math.Max(0.1f, w),
            P = Clamp(pressure, 0f, 1f),
            T = p2.T
        };
    }

    private static float Distance(InkPoint a, InkPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private void CommitStroke(int id)
    {
        if (!_activeStrokes.TryGetValue(id, out var active))
        {
            return;
        }

        if (active.Points.Count > 0)
        {
            _strokes.Add(new InkStroke
            {
                Color = PenColor,
                Width = PenWidth,
                Points = active.Points.ToList()
            });
        }

        _activeStrokes.Remove(id);
        RequestRedraw();
    }

    private void EraseAt(float x, float y)
    {
        var radiusSquared = EraserRadius * EraserRadius;
        _strokes.RemoveAll(s => s.Points.Any(p =>
        {
            var dx = p.X - x;
            var dy = p.Y - y;
            return dx * dx + dy * dy <= radiusSquared;
        }));
        RequestRedraw();
    }

    private PointerSample GetStylusSample(StylusEventArgs e, out bool hasPressure)
    {
        hasPressure = false;
        var points = e.GetStylusPoints(this);
        if (points.Count == 0)
        {
            var pos = e.GetPosition(this);
            return new PointerSample((float)pos.X, (float)pos.Y, 0f, NowMs());
        }

        var sp = points[points.Count - 1];
        var pos2 = sp.ToPoint();
        var pressure = 0f;
        if (StylusPressureEnabled)
        {
            var pf = (float)sp.PressureFactor;
            pressure = pf;
            hasPressure = pf < 0.48f || pf > 0.52f;
        }

        return new PointerSample((float)pos2.X, (float)pos2.Y, pressure, NowMs());
    }

    private static int StylusId(StylusDevice? stylusDevice) => stylusDevice == null ? -2 : -1000 - stylusDevice.Id;

    private static long NowMs() => Environment.TickCount64;

    private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);

    private void ReleaseRenderResources()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
        _factory?.Dispose();
        _factory = null;
        _compositionVisual?.Dispose();
        _compositionVisual = null;
        _compositionTarget?.Dispose();
        _compositionTarget = null;
        _compositionDevice?.Dispose();
        _compositionDevice = null;
    }

    public new void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseRenderResources();
        _disposed = true;
    }

    private readonly struct PointerSample
    {
        public PointerSample(float x, float y, float pressure, long timestampMs)
        {
            X = x;
            Y = y;
            Pressure = pressure;
            TimestampMs = timestampMs;
        }

        public float X { get; }
        public float Y { get; }
        public float Pressure { get; }
        public long TimestampMs { get; }
    }

    private sealed class ActiveStroke
    {
        public ActiveStroke(float startX, float startY, long startTimeMs, bool smoothingEnabled)
        {
            Points = new List<InkPoint>(256);
            LastX = startX;
            LastY = startY;
            LastT = startTimeMs;

            if (smoothingEnabled)
            {
                FilterX = new OneEuroFilter(1.2f, 0.015f, 1.0f);
                FilterY = new OneEuroFilter(1.2f, 0.015f, 1.0f);
                FilterW = new OneEuroFilter(1.0f, 0.02f, 1.0f);
            }
        }

        public List<InkPoint> Points { get; }
        public float LastX { get; set; }
        public float LastY { get; set; }
        public long LastT { get; set; }
        public OneEuroFilter? FilterX { get; }
        public OneEuroFilter? FilterY { get; }
        public OneEuroFilter? FilterW { get; }
    }

    private sealed class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _dCutoff;
        private bool _initialized;
        private float _xPrev;
        private float _dxPrev;

        public OneEuroFilter(float minCutoff, float beta, float dCutoff)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        public float Filter(float x, float dtSeconds, float speed)
        {
            if (!_initialized)
            {
                _initialized = true;
                _xPrev = x;
                _dxPrev = 0;
                return x;
            }

            var dx = (x - _xPrev) / Math.Max(1e-6f, dtSeconds);
            var aD = Alpha(_dCutoff, dtSeconds);
            var dxHat = Lerp(_dxPrev, dx, aD);
            var cutoff = _minCutoff + _beta * speed;
            var a = Alpha(cutoff, dtSeconds);
            var xHat = Lerp(_xPrev, x, a);
            _xPrev = xHat;
            _dxPrev = dxHat;
            return xHat;
        }

        private static float Alpha(float cutoff, float dtSeconds)
        {
            var tau = 1f / (2f * (float)Math.PI * Math.Max(1e-3f, cutoff));
            return 1f / (1f + tau / Math.Max(1e-6f, dtSeconds));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}

