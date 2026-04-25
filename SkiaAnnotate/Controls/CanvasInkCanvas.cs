using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace SkiaAnnotate.Controls;

public enum CanvasInkTool
{
    Pen,
    Eraser
}

public sealed class CanvasInkCanvas : InkCanvas
{
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
        Cursor = System.Windows.Input.Cursors.Pen;
        Background = Brushes.Transparent;
        EditingMode = InkCanvasEditingMode.Ink;
        DefaultDrawingAttributes = CreateSmoothedDrawingAttributes();
        StrokeCollected += (_, e) => EnsureStrokeUsesSmoothing(e.Stroke);
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
        stroke.DrawingAttributes = attributes;
    }
}
