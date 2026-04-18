using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace SkiaAnnotate.Controls;

public enum SkiaInkTool
{
    Pen,
    Eraser
}

public sealed class SkiaStrokePoint
{
    public float X { get; init; }
    public float Y { get; init; }
    /// <summary>
    /// Optional: per-point width in DIPs. When 0, fallback to stroke.Width.
    /// </summary>
    public float W { get; init; }
    /// <summary>
    /// Optional: normalized pressure [0..1]. 0 means "unknown".
    /// </summary>
    public float P { get; init; }
    /// <summary>
    /// Timestamp in milliseconds from a monotonic clock (optional).
    /// </summary>
    public long T { get; init; }
}

public sealed class SkiaStroke
{
    public Color Color { get; init; }
    public float Width { get; init; }
    public List<SkiaStrokePoint> Points { get; init; } = new();
}

public sealed class SkiaInkCanvas : SKElement
{
    private List<SkiaStroke> _strokes = new();
    private readonly Dictionary<int, ActiveStroke> _activeStrokes = new();
    private bool _sawPressureVariation;

    public SkiaInkTool Tool { get; set; } = SkiaInkTool.Pen;
    public Color PenColor { get; set; } = Colors.Red;
    public float PenWidth { get; set; } = 4f;
    public float EraserRadius { get; set; } = 18f;

    // Defaults: multi-touch enabled, stylus pressure auto (on when detected), smoothing enabled.
    public bool MultiTouchEnabled { get; set; } = true;
    public bool StylusPressureEnabled { get; set; } = true;
    public bool SmoothingEnabled { get; set; } = true;

    public SkiaInkCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Pen;
        PaintSurface += OnPaintSurface;

        // Prefer stylus/touch for better input fidelity; keep mouse as fallback.
        StylusDown += OnStylusDown;
        StylusMove += OnStylusMove;
        StylusUp += OnStylusUp;
        StylusLeave += OnStylusUp;

        TouchDown += OnTouchDown;
        TouchMove += OnTouchMove;
        TouchUp += OnTouchUp;
        TouchLeave += OnTouchUp;

        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseLeave += OnMouseUp;
    }

    /// <summary>
    /// Bind strokes list by reference (no cloning).
    /// Caller owns the list lifecycle; canvas mutates the list in-place.
    /// </summary>
    public void BindStrokes(List<SkiaStroke> strokes)
    {
        _strokes = strokes;
        _activeStrokes.Clear();
        InvalidateVisual();
    }

    public List<SkiaStroke> GetBoundStrokes() => _strokes;

    public void CommitActiveStroke()
    {
        // Commit all active pen strokes (multi-touch / stylus).
        foreach (var id in _activeStrokes.Keys.ToList())
        {
            CommitStroke(id);
        }
        InvalidateVisual();
    }

    public void Clear()
    {
        _strokes.Clear();
        _activeStrokes.Clear();
        InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // If stylus is in range, mouse events can be promoted/duplicated; prefer stylus pipeline.
        if (Stylus.CurrentStylusDevice != null)
        {
            return;
        }

        Focus();
        CaptureMouse();
        var p = e.GetPosition(this);
        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            return;
        }

        // Mouse is treated as a single-pointer id = -1
        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, pressure: 0f, timestampMs: NowMs()), isStart: true);
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (Stylus.CurrentStylusDevice != null)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            return;
        }

        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, pressure: 0f, timestampMs: NowMs()), isStart: false);
        InvalidateVisual();
    }

    private void OnMouseUp(object? sender, EventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (Tool == SkiaInkTool.Pen)
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
        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, pressure: 0f, timestampMs: NowMs()), isStart: true);
        InvalidateVisual();
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
        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, pressure: 0f, timestampMs: NowMs()), isStart: false);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        var id = e.TouchDevice.Id;
        ReleaseTouchCapture(e.TouchDevice);

        if (Tool == SkiaInkTool.Pen)
        {
            CommitStroke(id);
        }

        InvalidateVisual();
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

        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt(sample.X, sample.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, sample, isStart: true);
        InvalidateVisual();
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

        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt(sample.X, sample.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        StartOrUpdateStroke(id, sample, isStart: false);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnStylusUp(object? sender, StylusEventArgs e)
    {
        if (IsStylusCaptured)
        {
            ReleaseStylusCapture();
        }

        if (Tool == SkiaInkTool.Pen)
        {
            CommitStroke(StylusId(e.StylusDevice));
        }

        InvalidateVisual();
        e.Handled = true;
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
        InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var widthDip = (float)Math.Max(1d, ActualWidth);
        var heightDip = (float)Math.Max(1d, ActualHeight);
        var scaleX = e.Info.Width / widthDip;
        var scaleY = e.Info.Height / heightDip;
        canvas.Scale(scaleX, scaleY);

        foreach (var stroke in _strokes)
        {
            DrawStroke(canvas, stroke);
        }

        if (Tool == SkiaInkTool.Pen && _activeStrokes.Count > 0)
        {
            foreach (var active in _activeStrokes.Values)
            {
                var activeStroke = new SkiaStroke
                {
                    Color = PenColor,
                    Width = PenWidth,
                    Points = active.Points
                };
                DrawStroke(canvas, activeStroke);
            }
        }
    }

    private static void DrawStroke(SKCanvas canvas, SkiaStroke stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        if (stroke.Points.Count == 1)
        {
            var p = stroke.Points[0];
            using var paintDot = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = p.W > 0 ? p.W : stroke.Width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A)
            };
            canvas.DrawPoint(p.X, p.Y, paintDot);
            return;
        }

        // Variable-width rendering (pressure/velocity) if per-point width is present.
        // Fallback to constant width when W is not set.
        var anyVariableWidth = stroke.Points.Any(p => p.W > 0);
        if (!anyVariableWidth)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke.Width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A)
            };
            using var path = new SKPath();
            path.MoveTo(stroke.Points[0].X, stroke.Points[0].Y);
            for (var i = 1; i < stroke.Points.Count; i++)
            {
                path.LineTo(stroke.Points[i].X, stroke.Points[i].Y);
            }
            canvas.DrawPath(path, paint);
            return;
        }

        using var paintVar = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke.Width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A)
        };

        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var a = stroke.Points[i - 1];
            var b = stroke.Points[i];
            var wA = a.W > 0 ? a.W : stroke.Width;
            var wB = b.W > 0 ? b.W : stroke.Width;
            paintVar.StrokeWidth = (wA + wB) * 0.5f;
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, paintVar);
        }
    }

    // Intentionally no deep-clone here for performance (Inkeys style binding).

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
        public float Pressure { get; } // [0..1], 0 means unknown
        public long TimestampMs { get; }
    }

    private sealed class ActiveStroke
    {
        public ActiveStroke(float startX, float startY, long startTimeMs, bool smoothingEnabled)
        {
            Points = new List<SkiaStrokePoint>(256);
            LastX = startX;
            LastY = startY;
            LastT = startTimeMs;

            if (smoothingEnabled)
            {
                FilterX = new OneEuroFilter(minCutoff: 1.2f, beta: 0.015f, dCutoff: 1.0f);
                FilterY = new OneEuroFilter(minCutoff: 1.2f, beta: 0.015f, dCutoff: 1.0f);
                FilterW = new OneEuroFilter(minCutoff: 1.0f, beta: 0.02f, dCutoff: 1.0f);
            }
        }

        public List<SkiaStrokePoint> Points { get; }
        public float LastX { get; set; }
        public float LastY { get; set; }
        public long LastT { get; set; }
        public float LastVelocity { get; set; }

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
                _dxPrev = 0f;
                return x;
            }

            // derivative of the signal
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
            // alpha = 1 / (1 + tau / dt) , tau = 1/(2*pi*cutoff)
            var tau = 1f / (2f * (float)Math.PI * Math.Max(1e-3f, cutoff));
            return 1f / (1f + tau / Math.Max(1e-6f, dtSeconds));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    private static long NowMs() => Environment.TickCount64;

    private static int StylusId(StylusDevice? stylusDevice)
    {
        // StylusDevice.Id is stable per device contact; make it negative to avoid colliding with TouchDevice.Id.
        return stylusDevice == null ? -2 : -1000 - stylusDevice.Id;
    }

    private PointerSample GetStylusSample(StylusEventArgs e, out bool hasPressure)
    {
        hasPressure = false;
        var points = e.GetStylusPoints(this);
        if (points == null || points.Count == 0)
        {
            var pos = e.GetPosition(this);
            return new PointerSample((float)pos.X, (float)pos.Y, pressure: 0f, timestampMs: NowMs());
        }

        var sp = points[points.Count - 1];
        var pos2 = sp.ToPoint();
        var pressure = 0f;
        if (StylusPressureEnabled)
        {
            var pf = (float)sp.PressureFactor; // 0..1, often 0.5 on non-pressure devices
            pressure = pf;
            // treat as "real pressure" only if it deviates meaningfully from the common default
            hasPressure = pf < 0.48f || pf > 0.52f;
        }

        return new PointerSample((float)pos2.X, (float)pos2.Y, pressure, timestampMs: NowMs());
    }

    private void StartOrUpdateStroke(int id, PointerSample sample, bool isStart)
    {
        if (Tool != SkiaInkTool.Pen)
        {
            return;
        }

        if (isStart)
        {
            _activeStrokes[id] = new ActiveStroke(sample.X, sample.Y, sample.TimestampMs, smoothingEnabled: SmoothingEnabled);
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
        var speed = dist / dt; // DIPs per second
        active.LastVelocity = speed;

        var x = sample.X;
        var y = sample.Y;

        if (SmoothingEnabled && active.FilterX != null && active.FilterY != null)
        {
            x = active.FilterX.Filter(x, dt, speed);
            y = active.FilterY.Filter(y, dt, speed);
        }

        // pressure: auto-enable only when we observe meaningful variation from default
        var usePressure = StylusPressureEnabled && _sawPressureVariation && sample.Pressure > 0f;
        var p = usePressure ? Clamp(sample.Pressure, 0f, 1f) : 0f;

        // width: based on base width * pressure (if available) * speed factor
        var w = PenWidth;
        if (usePressure)
        {
            // Keep a minimum visible width even at low pressure
            w *= 0.25f + 0.75f * p;
        }

        // Speed-based taper: faster -> thinner, slower -> thicker (bounded)
        var speedFactor = Clamp(1.15f - (speed / 2200f), 0.45f, 1.25f);
        w *= speedFactor;

        if (SmoothingEnabled && active.FilterW != null)
        {
            w = active.FilterW.Filter(w, dt, speed);
        }

        // Resampling / point budget: avoid too many points when stationary.
        // Always accept the first point; then require a minimum movement.
        var minDist = 0.35f;
        if (!isStart && dist < minDist)
        {
            active.LastX = sample.X;
            active.LastY = sample.Y;
            active.LastT = sample.TimestampMs;
            return;
        }

        active.Points.Add(new SkiaStrokePoint
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
    }

    private void CommitStroke(int id)
    {
        if (!_activeStrokes.TryGetValue(id, out var active))
        {
            return;
        }

        if (active.Points.Count > 0)
        {
            _strokes.Add(new SkiaStroke
            {
                Color = PenColor,
                Width = PenWidth,
                Points = active.Points.ToList()
            });
        }

        _activeStrokes.Remove(id);
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
}
