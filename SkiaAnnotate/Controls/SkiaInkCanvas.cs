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
    private readonly List<SkiaStrokePoint> _activePoints = new();

    public SkiaInkTool Tool { get; set; } = SkiaInkTool.Pen;
    public Color PenColor { get; set; } = Colors.Red;
    public float PenWidth { get; set; } = 4f;
    public float EraserRadius { get; set; } = 18f;

    public SkiaInkCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Pen;
        PaintSurface += OnPaintSurface;
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
        _activePoints.Clear();
        InvalidateVisual();
    }

    public List<SkiaStroke> GetBoundStrokes() => _strokes;

    public void CommitActiveStroke()
    {
        if (_activePoints.Count == 0)
        {
            return;
        }

        _strokes.Add(new SkiaStroke
        {
            Color = PenColor,
            Width = PenWidth,
            Points = _activePoints.Select(p => new SkiaStrokePoint { X = p.X, Y = p.Y }).ToList()
        });
        _activePoints.Clear();
        InvalidateVisual();
    }

    public void Clear()
    {
        _strokes.Clear();
        _activePoints.Clear();
        InvalidateVisual();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
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

        _activePoints.Clear();
        _activePoints.Add(new SkiaStrokePoint { X = (float)p.X, Y = (float)p.Y });
        InvalidateVisual();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (Tool == SkiaInkTool.Eraser)
        {
            EraseAt((float)p.X, (float)p.Y);
            return;
        }

        _activePoints.Add(new SkiaStrokePoint { X = (float)p.X, Y = (float)p.Y });
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
            CommitActiveStroke();
        }
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

        if (Tool == SkiaInkTool.Pen && _activePoints.Count > 0)
        {
            var activeStroke = new SkiaStroke
            {
                Color = PenColor,
                Width = PenWidth,
                Points = _activePoints
            };
            DrawStroke(canvas, activeStroke);
        }
    }

    private static void DrawStroke(SKCanvas canvas, SkiaStroke stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke.Width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, stroke.Color.A)
        };

        if (stroke.Points.Count == 1)
        {
            var p = stroke.Points[0];
            canvas.DrawPoint(p.X, p.Y, paint);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(stroke.Points[0].X, stroke.Points[0].Y);
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            path.LineTo(stroke.Points[i].X, stroke.Points[i].Y);
        }

        canvas.DrawPath(path, paint);
    }

    // Intentionally no deep-clone here for performance (Inkeys style binding).
}
