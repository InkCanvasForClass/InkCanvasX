using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Windows.Media;

namespace SkiaAnnotate.Controls;

public enum CanvasInkTool
{
    Pen,
    Eraser
}

public sealed class CanvasInkCanvas : InkCanvas
{
    private readonly RealtimeStrokeRenderer _realtimeRenderer;
    private const double RealtimeSampleDt = 1d / 120d;

    private static DrawingAttributes CreateSmoothedDrawingAttributes() =>
        new()
        {
            Color = Colors.Red,
            Width = 4,
            Height = 4,
            // Enable WPF built-in stroke smoothing.
            FitToCurve = true,
            IgnorePressure = false
        };

    public CanvasInkTool Tool
    {
        get => EditingMode == InkCanvasEditingMode.EraseByStroke ? CanvasInkTool.Eraser : CanvasInkTool.Pen;
        set => EditingMode = value == CanvasInkTool.Eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.Ink;
    }

    public Color PenColor
    {
        get => DefaultDrawingAttributes.Color;
        set
        {
            var attributes = DefaultDrawingAttributes.Clone();
            attributes.Color = value;
            DefaultDrawingAttributes = attributes;
        }
    }

    public float PenWidth
    {
        get => (float)DefaultDrawingAttributes.Width;
        set
        {
            var width = value <= 0 ? 1 : value;
            var attributes = DefaultDrawingAttributes.Clone();
            attributes.Width = width;
            attributes.Height = width;
            DefaultDrawingAttributes = attributes;
        }
    }

    public CanvasInkCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Pen;
        Background = Brushes.Transparent;
        EditingMode = InkCanvasEditingMode.Ink;
        DefaultDrawingAttributes = CreateSmoothedDrawingAttributes();
        _realtimeRenderer = new RealtimeStrokeRenderer(this);
        DynamicRenderer = _realtimeRenderer;
        StrokeCollected += OnStrokeCollected;
    }

    public void BindStrokes(StrokeCollection strokes)
    {
        var next = strokes ?? new StrokeCollection();
        foreach (var stroke in next)
        {
            EnsureStrokeUsesSmoothing(stroke);
        }

        Strokes = next;
    }

    public StrokeCollection GetBoundStrokes() => Strokes;

    public void CommitActiveStroke()
    {
        // InkCanvas handles stroke commit internally.
    }

    public void Clear()
    {
        Strokes.Clear();
    }

    private static void EnsureStrokeUsesSmoothing(Stroke stroke)
    {
        var attributes = stroke.DrawingAttributes.Clone();
        attributes.FitToCurve = true;
        attributes.IgnorePressure = false;
        stroke.DrawingAttributes = attributes;

        // Recompute pressure factors from speed+pressure so final stroke keeps
        // the same "realtime nib" feeling after commit.
        var transformed = RebuildStylusPointsWithRealtimeNib(stroke.StylusPoints);
        stroke.StylusPoints = transformed;
    }

    private void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (Tool != CanvasInkTool.Pen)
        {
            return;
        }

        EnsureStrokeUsesSmoothing(e.Stroke);
    }

    private static StylusPointCollection RebuildStylusPointsWithRealtimeNib(StylusPointCollection source)
    {
        if (source.Count <= 1)
        {
            return source.Clone();
        }

        var result = new StylusPointCollection(source.Count * 2);
        var smoothedRate = 120d;
        var previous = source[0];
        var filterX = new OneEuroFilter(1.2, 0.015, 1);
        var filterY = new OneEuroFilter(1.2, 0.015, 1);
        var filterW = new OneEuroFilter(1.0, 0.02, 1);
        var hasSmoothSeed = false;
        var lastSmoothX = 0d;
        var lastSmoothY = 0d;
        var lastSmoothW = 0d;

        for (var i = 1; i < source.Count; i++)
        {
            var current = source[i];
            var dt = RealtimeSampleDt;
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var speed = dist / Math.Max(1e-4, dt);
            var sampleRate = 1d / Math.Max(1e-4, dt);
            smoothedRate = smoothedRate * 0.85 + sampleRate * 0.15;

            var x = filterX.Filter(current.X, dt, speed);
            var y = filterY.Filter(current.Y, dt, speed);
            var pressure = current.PressureFactor <= 0 ? 0.5 : current.PressureFactor;
            var baseWidth = Clamp(0.25 + 0.75 * pressure, 0.2, 1.25);
            var speedNormalization = 1800d + smoothedRate * 3.5;
            var velocityFactor = Clamp(1.15 - speed / speedNormalization, 0.45, 1.25);
            var width = filterW.Filter(baseWidth * velocityFactor, dt, speed);

            var minDist = smoothedRate > 160 ? 0.55 : smoothedRate > 90 ? 0.4 : 0.25;
            if (dist < minDist)
            {
                previous = current;
                continue;
            }

            if (!hasSmoothSeed)
            {
                hasSmoothSeed = true;
                lastSmoothX = x;
                lastSmoothY = y;
                lastSmoothW = width;
                result.Add(new StylusPoint(x, y, Clamp01(width)));
            }
            else
            {
                var mx = (lastSmoothX + x) * 0.5;
                var my = (lastSmoothY + y) * 0.5;
                var mw = (lastSmoothW + width) * 0.5;
                result.Add(new StylusPoint(mx, my, Clamp01(mw)));
                lastSmoothX = x;
                lastSmoothY = y;
                lastSmoothW = width;
            }

            previous = current;
        }

        if (hasSmoothSeed)
        {
            result.Add(new StylusPoint(lastSmoothX, lastSmoothY, Clamp01(lastSmoothW)));
        }

        if (result.Count == 0)
        {
            return source.Clone();
        }

        return result;
    }

    private static float Clamp01(double value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 1)
        {
            return 1;
        }

        return (float)value;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private sealed class RealtimeStrokeRenderer : DynamicRenderer
    {
        private readonly CanvasInkCanvas _owner;
        private double _smoothedRate = 120d;
        private StylusPoint? _lastPoint;
        private OneEuroFilter? _filterX;
        private OneEuroFilter? _filterY;
        private OneEuroFilter? _filterW;
        private bool _hasSmoothSeed;
        private double _lastSmoothX;
        private double _lastSmoothY;
        private double _lastSmoothW;

        public RealtimeStrokeRenderer(CanvasInkCanvas owner)
        {
            _owner = owner;
            DrawingAttributes = owner.DefaultDrawingAttributes.Clone();
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            base.OnStylusDown(rawStylusInput);
            _smoothedRate = 120d;
            _lastPoint = null;
            _filterX = new OneEuroFilter(1.2, 0.015, 1);
            _filterY = new OneEuroFilter(1.2, 0.015, 1);
            _filterW = new OneEuroFilter(1.0, 0.02, 1);
            _hasSmoothSeed = false;
            _lastSmoothX = 0;
            _lastSmoothY = 0;
            _lastSmoothW = 0;
        }

        protected override void OnDraw(DrawingContext drawingContext, StylusPointCollection stylusPoints, Geometry geometry, Brush fillBrush)
        {
            if (stylusPoints.Count == 0)
            {
                return;
            }

            if (_lastPoint is null)
            {
                _lastPoint = stylusPoints[0];
            }

            DrawingAttributes = _owner.DefaultDrawingAttributes.Clone();
            var color = DrawingAttributes.Color;
            for (var i = 0; i < stylusPoints.Count; i++)
            {
                var point = stylusPoints[i];
                var previous = _lastPoint ?? point;
                var dt = RealtimeSampleDt;
                var dx = point.X - previous.X;
                var dy = point.Y - previous.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var speed = dist / Math.Max(1e-4, dt);
                var sampleRate = 1d / Math.Max(1e-4, dt);
                _smoothedRate = _smoothedRate * 0.85 + sampleRate * 0.15;

                var x = _filterX?.Filter(point.X, dt, speed) ?? point.X;
                var y = _filterY?.Filter(point.Y, dt, speed) ?? point.Y;
                var pressure = point.PressureFactor <= 0 ? 0.5 : point.PressureFactor;
                var widthScale = Clamp(0.25 + 0.75 * pressure, 0.2, 1.25);
                var speedNormalization = 1800d + _smoothedRate * 3.5;
                var velocityFactor = Clamp(1.15 - speed / speedNormalization, 0.45, 1.25);
                var width = (_filterW?.Filter(widthScale * velocityFactor, dt, speed) ?? widthScale) * DrawingAttributes.Width;
                width = Math.Max(0.8, width);

                double fromX;
                double fromY;
                double toX;
                double toY;
                if (!_hasSmoothSeed)
                {
                    _hasSmoothSeed = true;
                    _lastSmoothX = x;
                    _lastSmoothY = y;
                    _lastSmoothW = width;
                    _lastPoint = point;
                    continue;
                }

                var mx = (_lastSmoothX + x) * 0.5;
                var my = (_lastSmoothY + y) * 0.5;
                fromX = _lastSmoothX;
                fromY = _lastSmoothY;
                toX = mx;
                toY = my;
                _lastSmoothX = x;
                _lastSmoothY = y;
                _lastSmoothW = width;

                var pen = new Pen(new SolidColorBrush(color), width)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                drawingContext.DrawLine(pen, new System.Windows.Point(fromX, fromY), new System.Windows.Point(toX, toY));
                _lastPoint = point;
            }
        }
    }

    private sealed class OneEuroFilter
    {
        private readonly double _minCutoff;
        private readonly double _beta;
        private readonly double _dCutoff;
        private bool _initialized;
        private double _xPrev;
        private double _dxPrev;

        public OneEuroFilter(double minCutoff, double beta, double dCutoff)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _dCutoff = dCutoff;
        }

        public double Filter(double x, double dt, double speed)
        {
            if (!_initialized)
            {
                _initialized = true;
                _xPrev = x;
                _dxPrev = 0;
                return x;
            }

            var dx = (x - _xPrev) / Math.Max(1e-6, dt);
            var aD = Alpha(_dCutoff, dt);
            var dxHat = Lerp(_dxPrev, dx, aD);
            var a = Alpha(_minCutoff + _beta * speed * 0.001, dt);
            var xHat = Lerp(_xPrev, x, a);
            _xPrev = xHat;
            _dxPrev = dxHat;
            return xHat;
        }

        private static double Alpha(double cutoff, double dt)
        {
            var tau = 1d / (2d * Math.PI * Math.Max(1e-3, cutoff));
            return 1d / (1d + tau / Math.Max(1e-6, dt));
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
