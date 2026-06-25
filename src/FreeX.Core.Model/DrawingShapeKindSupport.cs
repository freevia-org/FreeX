namespace FreeX.Core.Model;

public static class DrawingShapeKindSupport
{
    public static bool IsRenderable(DrawingShapeKind kind) =>
        kind switch
        {
            DrawingShapeKind.Rectangle or
            DrawingShapeKind.RoundedRectangle or
            DrawingShapeKind.Ellipse or
            DrawingShapeKind.Line or
            DrawingShapeKind.ElbowConnector or
            DrawingShapeKind.CurvedConnector or
            DrawingShapeKind.Triangle or
            DrawingShapeKind.RightTriangle or
            DrawingShapeKind.Diamond or
            DrawingShapeKind.Parallelogram or
            DrawingShapeKind.Trapezoid or
            DrawingShapeKind.Pentagon or
            DrawingShapeKind.Hexagon or
            DrawingShapeKind.Octagon or
            DrawingShapeKind.Cross or
            DrawingShapeKind.RightArrow or
            DrawingShapeKind.LeftArrow or
            DrawingShapeKind.UpArrow or
            DrawingShapeKind.DownArrow or
            DrawingShapeKind.LeftRightArrow or
            DrawingShapeKind.UpDownArrow or
            DrawingShapeKind.PlusSign or
            DrawingShapeKind.MinusSign or
            DrawingShapeKind.MultiplySign or
            DrawingShapeKind.DivideSign or
            DrawingShapeKind.EqualSign or
            DrawingShapeKind.NotEqualSign or
            DrawingShapeKind.FlowchartProcess or
            DrawingShapeKind.FlowchartDecision or
            DrawingShapeKind.FlowchartData or
            DrawingShapeKind.FlowchartPredefinedProcess or
            DrawingShapeKind.FlowchartDocument or
            DrawingShapeKind.FlowchartTerminator or
            DrawingShapeKind.Star5 or
            DrawingShapeKind.Star8 or
            DrawingShapeKind.Explosion or
            DrawingShapeKind.Ribbon or
            DrawingShapeKind.Wave or
            DrawingShapeKind.RectangularCallout or
            DrawingShapeKind.RoundedRectangularCallout or
            DrawingShapeKind.OvalCallout or
            DrawingShapeKind.LineCallout or
            DrawingShapeKind.Chevron or
            DrawingShapeKind.HomePlate => true,
            _ => false
        };

    public static bool IsLineLike(DrawingShapeKind kind) =>
        kind is DrawingShapeKind.Line or
            DrawingShapeKind.ElbowConnector or
            DrawingShapeKind.CurvedConnector;
}
