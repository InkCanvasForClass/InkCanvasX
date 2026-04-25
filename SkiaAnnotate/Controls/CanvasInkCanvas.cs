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

        var description = source.Description;
        var result = new StylusPointCollection(description, source.Count);
        var smoothedRate = 120d;
        var previous = source[0];
        result.Add(new StylusPoint(previous.X, previous.Y, Clamp01(previous.PressureFactor)));

        for (var i = 1; i < source.Count; i++)
        {
            var current = source[i];
            var dt = 1d / 120d;
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            var speed = dist / Math.Max(1e-4, dt);
            var sampleRate = 1d / Math.Max(1e-4, dt);
            smoothedRate = smoothedRate * 0.85 + sampleRate * 0.15;

            // ICC-style velocity mapping: faster -> thinner.
            var velocityFactor = Clamp(1.18 - speed / (1700 + smoothedRate * 3.2), 0.45, 1.25);
            var basePressure = Clamp01(current.PressureFactor <= 0 ? 0.5 : current.PressureFactor);
            var pressure = Clamp01(basePressure * velocityFactor);

            result.Add(new StylusPoint(current.X, current.Y, pressure));
            previous = current;
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
        private double _smoothedRate = 120d;
        private StylusPoint? _lastPoint;

        public RealtimeStrokeRenderer(CanvasInkCanvas owner)
        {
            DrawingAttributes = owner.DefaultDrawingAttributes.Clone();
        }

        protected override void OnStylusDown(RawStylusInput rawStylusInput)
        {
            base.OnStylusDown(rawStylusInput);
            _smoothedRate = 120d;
            _lastPoint = null;
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

            var color = (DrawingAttributes.Color);
            for (var i = 0; i < stylusPoints.Count; i++)
            {
                var point = stylusPoints[i];
                var previous = _lastPoint ?? point;
                var dt = 1d / 120d;
                var dx = point.X - previous.X;
                var dy = point.Y - previous.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var speed = dist / Math.Max(1e-4, dt);
                var sampleRate = 1d / Math.Max(1e-4, dt);
                _smoothedRate = _smoothedRate * 0.85 + sampleRate * 0.15;
                var velocityFactor = Clamp(1.18 - speed / (1700 + _smoothedRate * 3.2), 0.45, 1.25);
                var basePressure = Clamp01(point.PressureFactor <= 0 ? 0.5 : point.PressureFactor);
                var width = Math.Max(0.8, DrawingAttributes.Width * basePressure * velocityFactor);
                var pen = new Pen(new SolidColorBrush(color), width)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };

                drawingContext.DrawLine(pen, new System.Windows.Point(previous.X, previous.Y), new System.Windows.Point(point.X, point.Y));
                _lastPoint = point;
            }
        }
    }
}
