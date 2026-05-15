namespace Freexcel.Core.Model;

public enum DrawingShapeKind
{
    Rectangle,
    Ellipse,
    Line
}

public sealed class DrawingShapeModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CellAddress Anchor { get; set; }
    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public string? AltText { get; set; }
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
}
