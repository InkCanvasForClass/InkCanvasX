using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    public float W { get; init; }
    public float P { get; init; }
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
    private SKPicture? _committedLayerPicture;
    private bool _committedLayerDirty = true;
    private bool _sawPressureVariation;

    // Reuse paints/paths to reduce per-frame allocations (especially for active strokes).
    private readonly SKPaint _paintDot;
    private readonly SKPaint _paintStroke;
    private readonly SKPaint _paintVar;
    private readonly SKPath _path;

    public SkiaInkTool Tool { get; set; } = SkiaInkTool.Pen;
    public Color PenColor { get; set; } = Colors.Red;
    public float PenWidth { get; set; } = 4f;
    public float EraserRadius { get; set; } = 18f;
    public bool MultiTouchEnabled { get; set; } = true;
    public bool StylusPressureEnabled { get; set; } = true;
    public bool SmoothingEnabled { get; set; } = true;

    public SkiaInkCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Pen;
        PaintSurface += OnPaintSurface;

        _paintDot = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        _paintStroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        _paintVar = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        _path = new SKPath();

        Unloaded += (_, _) =>
        {
            _committedLayerPicture?.Dispose();
            _committedLayerPicture = null;
            _path.Dispose();
            _paintDot.Dispose();
            _paintStroke.Dispose();
            _paintVar.Dispose();
        };

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

    public void BindStrokes(List<SkiaStroke> strokes)
    {
        _strokes = strokes;
        _activeStrokes.Clear();
        MarkCommittedLayerDirty();
        RequestRender();
    }

    public List<SkiaStroke> GetBoundStrokes() => _strokes;

    public void CommitActiveStroke()
    {
        foreach (var id in _activeStrokes.Keys.ToList()) CommitStroke(id);
        RequestRender();
    }

    public void Clear()
    {
        _strokes.Clear();
        _activeStrokes.Clear();
        MarkCommittedLayerDirty();
        RequestRender();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Stylus.CurrentStylusDevice != null) return;
        Focus();
        CaptureMouse();
        var p = e.GetPosition(this);
        if (Tool == SkiaInkTool.Eraser) { EraseAt((float)p.X, (float)p.Y); return; }
        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), true);
        RequestRender();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Stylus.CurrentStylusDevice != null) return;
        var p = e.GetPosition(this);
        if (Tool == SkiaInkTool.Eraser) { EraseAt((float)p.X, (float)p.Y); return; }
        StartOrUpdateStroke(-1, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), false);
        RequestRender();
    }

    private void OnMouseUp(object? sender, EventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (Tool == SkiaInkTool.Pen) CommitStroke(-1);
        RequestRender();
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (!MultiTouchEnabled && _activeStrokes.Count > 0) return;
        Focus();
        CaptureTouch(e.TouchDevice);
        var id = e.TouchDevice.Id;
        var p = e.GetTouchPoint(this).Position;
        if (Tool == SkiaInkTool.Eraser) { EraseAt((float)p.X, (float)p.Y); e.Handled = true; return; }
        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), true);
        RequestRender();
        e.Handled = true;
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        var id = e.TouchDevice.Id;
        if (!_activeStrokes.ContainsKey(id)) return;
        var p = e.GetTouchPoint(this).Position;
        if (Tool == SkiaInkTool.Eraser) { EraseAt((float)p.X, (float)p.Y); e.Handled = true; return; }
        StartOrUpdateStroke(id, new PointerSample((float)p.X, (float)p.Y, 0f, NowMs()), false);
        RequestRender();
        e.Handled = true;
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        var id = e.TouchDevice.Id;
        ReleaseTouchCapture(e.TouchDevice);
        if (Tool == SkiaInkTool.Pen) CommitStroke(id);
        RequestRender();
        e.Handled = true;
    }

    private void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        Focus();
        // Capture per device so multi-touch (often delivered as stylus) can work concurrently.
        e.StylusDevice?.Capture(this);
        var id = StylusId(e.StylusDevice);
        var sample = GetStylusSample(e, out var hasPressure);
        if (hasPressure) _sawPressureVariation = true;
        if (Tool == SkiaInkTool.Eraser) { EraseAt(sample.X, sample.Y); e.Handled = true; return; }
        StartOrUpdateStroke(id, sample, true);
        RequestRender();
        e.Handled = true;
    }

    private void OnStylusMove(object sender, StylusEventArgs e)
    {
        var id = StylusId(e.StylusDevice);
        if (!_activeStrokes.ContainsKey(id)) return;
        var sample = GetStylusSample(e, out var hasPressure);
        if (hasPressure) _sawPressureVariation = true;
        if (Tool == SkiaInkTool.Eraser) { EraseAt(sample.X, sample.Y); e.Handled = true; return; }
        StartOrUpdateStroke(id, sample, false);
        RequestRender();
        e.Handled = true;
    }

    private void OnStylusUp(object? sender, StylusEventArgs e)
    {
        // Release only this device capture.
        e.StylusDevice?.Capture(null);
        if (Tool == SkiaInkTool.Pen) CommitStroke(StylusId(e.StylusDevice));
        RequestRender();
        e.Handled = true;
    }

    private void EraseAt(float x, float y)
    {
        var rs = EraserRadius * EraserRadius;
        _strokes.RemoveAll(s => s.Points.Any(p =>
        {
            var dx = p.X - x;
            var dy = p.Y - y;
            return dx * dx + dy * dy <= rs;
        }));
        MarkCommittedLayerDirty();
        RequestRender();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        var widthDip = (float)Math.Max(1d, ActualWidth);
        var heightDip = (float)Math.Max(1d, ActualHeight);
        var sx = e.Info.Width / widthDip;
        var sy = e.Info.Height / heightDip;
        canvas.Scale(sx, sy);

        EnsureCommittedLayer();
        if (_committedLayerPicture is not null) canvas.DrawPicture(_committedLayerPicture);

        if (Tool == SkiaInkTool.Pen && _activeStrokes.Count > 0)
        {
            foreach (var active in _activeStrokes.Values)
            {
                DrawStrokePoints(canvas, active.Points, PenColor, PenWidth);
            }
        }
    }

    private void EnsureCommittedLayer()
    {
        if (!_committedLayerDirty) return;
        _committedLayerPicture?.Dispose();
        using var recorder = new SKPictureRecorder();
        var c = recorder.BeginRecording(new SKRect(0, 0, (float)Math.Max(1, ActualWidth), (float)Math.Max(1, ActualHeight)));
        foreach (var stroke in _strokes) DrawStroke(c, stroke);
        _committedLayerPicture = recorder.EndRecording();
        _committedLayerDirty = false;
    }

    private void MarkCommittedLayerDirty() => _committedLayerDirty = true;

    // Lower input latency: invalidate immediately (WPF will coalesce as needed).
    private void RequestRender() => InvalidateVisual();

    private void DrawStroke(SKCanvas canvas, SkiaStroke stroke) => DrawStrokePoints(canvas, stroke.Points, stroke.Color, stroke.Width);

    private void DrawStrokePoints(SKCanvas canvas, List<SkiaStrokePoint> points, Color color, float defaultWidth)
    {
        if (points.Count == 0) return;
        if (points.Count == 1)
        {
            var p = points[0];
            _paintDot.StrokeWidth = p.W > 0 ? p.W : defaultWidth;
            _paintDot.Color = new SKColor(color.R, color.G, color.B, color.A);
            canvas.DrawPoint(p.X, p.Y, _paintDot);
            return;
        }

        var variable = false;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].W > 0)
            {
                variable = true;
                break;
            }
        }
        if (!variable)
        {
            _paintStroke.StrokeWidth = defaultWidth;
            _paintStroke.Color = new SKColor(color.R, color.G, color.B, color.A);
            _path.Rewind();
            _path.MoveTo(points[0].X, points[0].Y);
            for (var i = 1; i < points.Count; i++) _path.LineTo(points[i].X, points[i].Y);
            canvas.DrawPath(_path, _paintStroke);
            return;
        }

        _paintVar.Color = new SKColor(color.R, color.G, color.B, color.A);
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var wA = a.W > 0 ? a.W : defaultWidth;
            var wB = b.W > 0 ? b.W : defaultWidth;
            _paintVar.StrokeWidth = (wA + wB) * 0.5f;
            canvas.DrawLine(a.X, a.Y, b.X, b.Y, _paintVar);
        }
    }

    private readonly struct PointerSample
    {
        public PointerSample(float x, float y, float pressure, long timestampMs) { X = x; Y = y; Pressure = pressure; TimestampMs = timestampMs; }
        public float X { get; }
        public float Y { get; }
        public float Pressure { get; }
        public long TimestampMs { get; }
    }

    private sealed class ActiveStroke
    {
        public ActiveStroke(float startX, float startY, long startMs, bool smoothing)
        {
            Points = new List<SkiaStrokePoint>(256);
            LastX = startX;
            LastY = startY;
            LastT = startMs;
            HasSmoothSeed = false;
            if (smoothing)
            {
                FilterX = new OneEuroFilter(1.2f, 0.015f, 1f);
                FilterY = new OneEuroFilter(1.2f, 0.015f, 1f);
                FilterW = new OneEuroFilter(1f, 0.02f, 1f);
            }
        }
        public List<SkiaStrokePoint> Points { get; }
        public float LastX { get; set; }
        public float LastY { get; set; }
        public long LastT { get; set; }
        public float SmoothedSampleRateHz { get; set; } = 120f;
        // Inkeys-style stabilization: build points via midpoints to reduce polyline jitter.
        public bool HasSmoothSeed { get; set; }
        public float LastSmoothX { get; set; }
        public float LastSmoothY { get; set; }
        public float LastSmoothW { get; set; }
        public float LastSmoothP { get; set; }
        public OneEuroFilter? FilterX { get; }
        public OneEuroFilter? FilterY { get; }
        public OneEuroFilter? FilterW { get; }
    }

    private sealed class OneEuroFilter
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _dCutoff;
        private bool _init;
        private float _xPrev;
        private float _dxPrev;
        public OneEuroFilter(float minCutoff, float beta, float dCutoff) { _minCutoff = minCutoff; _beta = beta; _dCutoff = dCutoff; }
        public float Filter(float x, float dt, float speed)
        {
            if (!_init) { _init = true; _xPrev = x; _dxPrev = 0f; return x; }
            var dx = (x - _xPrev) / Math.Max(1e-6f, dt);
            var aD = Alpha(_dCutoff, dt);
            var dxHat = Lerp(_dxPrev, dx, aD);
            var a = Alpha(_minCutoff + _beta * speed, dt);
            var xHat = Lerp(_xPrev, x, a);
            _xPrev = xHat;
            _dxPrev = dxHat;
            return xHat;
        }
        private static float Alpha(float cutoff, float dt)
        {
            var tau = 1f / (2f * (float)Math.PI * Math.Max(1e-3f, cutoff));
            return 1f / (1f + tau / Math.Max(1e-6f, dt));
        }
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    private static long NowMs() => Environment.TickCount64;
    private static int StylusId(StylusDevice? stylusDevice) => stylusDevice == null ? -2 : -1000 - stylusDevice.Id;

    private PointerSample GetStylusSample(StylusEventArgs e, out bool hasPressure)
    {
        hasPressure = false;
        var points = e.GetStylusPoints(this);
        if (points == null || points.Count == 0)
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

    private void StartOrUpdateStroke(int id, PointerSample sample, bool isStart)
    {
        if (Tool != SkiaInkTool.Pen) return;
        if (isStart) _activeStrokes[id] = new ActiveStroke(sample.X, sample.Y, sample.TimestampMs, SmoothingEnabled);
        if (!_activeStrokes.TryGetValue(id, out var active)) return;

        var dtMs = Math.Max(1L, sample.TimestampMs - active.LastT);
        var dt = dtMs / 1000f;
        var sampleRate = 1f / Math.Max(1e-4f, dt);
        active.SmoothedSampleRateHz = active.SmoothedSampleRateHz * 0.85f + sampleRate * 0.15f;
        var dx = sample.X - active.LastX;
        var dy = sample.Y - active.LastY;
        var dist = (float)Math.Sqrt(dx * dx + dy * dy);
        var speed = dist / dt;

        var x = sample.X;
        var y = sample.Y;
        if (SmoothingEnabled && active.FilterX != null && active.FilterY != null)
        {
            x = active.FilterX.Filter(x, dt, speed);
            y = active.FilterY.Filter(y, dt, speed);
        }

        var usePressure = StylusPressureEnabled && _sawPressureVariation && sample.Pressure > 0f;
        var p = usePressure ? Clamp(sample.Pressure, 0f, 1f) : 0f;
        var w = PenWidth;
        if (usePressure) w *= 0.25f + 0.75f * p;
        var speedNormalization = 1800f + active.SmoothedSampleRateHz * 3.5f;
        w *= Clamp(1.15f - (speed / speedNormalization), 0.45f, 1.25f);
        if (SmoothingEnabled && active.FilterW != null) w = active.FilterW.Filter(w, dt, speed);

        var minDist = active.SmoothedSampleRateHz > 160f ? 0.55f : active.SmoothedSampleRateHz > 90f ? 0.4f : 0.25f;
        if (!isStart && dist < minDist)
        {
            active.LastX = sample.X;
            active.LastY = sample.Y;
            active.LastT = sample.TimestampMs;
            return;
        }

        // Inkeys-style smoothing: use midpoint chain to reduce jaggies while staying real-time.
        // We push midpoints (between last smooth sample and new sample) as the "stroke points".
        if (SmoothingEnabled)
        {
            if (!active.HasSmoothSeed)
            {
                active.HasSmoothSeed = true;
                active.LastSmoothX = x;
                active.LastSmoothY = y;
                active.LastSmoothW = w;
                active.LastSmoothP = p;
                active.Points.Add(new SkiaStrokePoint { X = x, Y = y, W = w, P = p, T = sample.TimestampMs });
            }
            else
            {
                var mx = (active.LastSmoothX + x) * 0.5f;
                var my = (active.LastSmoothY + y) * 0.5f;
                var mw = (active.LastSmoothW + w) * 0.5f;
                active.Points.Add(new SkiaStrokePoint { X = mx, Y = my, W = mw, P = p, T = sample.TimestampMs });
                active.LastSmoothX = x;
                active.LastSmoothY = y;
                active.LastSmoothW = w;
                active.LastSmoothP = p;
            }
        }
        else
        {
            active.Points.Add(new SkiaStrokePoint { X = x, Y = y, W = w, P = p, T = sample.TimestampMs });
        }
        active.LastX = sample.X;
        active.LastY = sample.Y;
        active.LastT = sample.TimestampMs;
    }

    private void CommitStroke(int id)
    {
        if (!_activeStrokes.TryGetValue(id, out var active)) return;
        if (active.Points.Count > 0)
        {
            // Flush last sample so the stroke ends at the real pointer location (not the last midpoint).
            if (SmoothingEnabled && active.HasSmoothSeed)
            {
                active.Points.Add(new SkiaStrokePoint { X = active.LastSmoothX, Y = active.LastSmoothY, W = active.LastSmoothW, P = active.LastSmoothP, T = NowMs() });
            }
            _strokes.Add(new SkiaStroke { Color = PenColor, Width = PenWidth, Points = active.Points.ToList() });
            MarkCommittedLayerDirty();
        }
        _activeStrokes.Remove(id);
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
}
