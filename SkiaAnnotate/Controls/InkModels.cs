using System.Collections.Generic;
using System.Windows.Media;

namespace SkiaAnnotate.Controls;

public enum InkTool
{
    Pen,
    Eraser
}

public sealed class InkPoint
{
    public float X { get; init; }
    public float Y { get; init; }
    public float W { get; init; }
    public float P { get; init; }
    public long T { get; init; }
}

public sealed class InkStroke
{
    public Color Color { get; init; }
    public float Width { get; init; }
    public List<InkPoint> Points { get; init; } = new();
}

