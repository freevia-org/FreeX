using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Pdf;

namespace Free.Shared.Xps;

internal static class PortableXpsPatternBrush
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";

    internal static bool CanRender(PdfPatternFill pattern) =>
        IsRepresentableTileExtent(pattern.TileWidth) && IsRepresentableTileExtent(pattern.TileHeight);

    internal static XElement Build(PdfPatternFill pattern)
    {
        if (!CanRender(pattern))
            throw new ArgumentOutOfRangeException(nameof(pattern), "The pattern tile extents must be representable XPS coordinates.");

        var recipe = pattern.Recipe;
        // Recipe coordinates start at the tile's top-left, just like XPS. Keep the source
        // viewbox in recipe units and scale the viewport to points, so stroke widths scale too.
        // The brush clips visuals to the viewbox before repeating the tile inside the outer fill.
        return new XElement(
            Xps + "VisualBrush",
            new XAttribute("Viewbox", FormattableString.Invariant($"0,0,{recipe.TileWidth},{recipe.TileHeight}")),
            new XAttribute("Viewport", FormattableString.Invariant($"0,0,{pattern.TileWidth},{pattern.TileHeight}")),
            new XAttribute("ViewboxUnits", "Absolute"),
            new XAttribute("ViewportUnits", "Absolute"),
            new XAttribute("TileMode", "Tile"),
            new XElement(Xps + "VisualBrush.Visual", new XElement(
                Xps + "Canvas", recipe.Primitives.Select(primitive => BuildPrimitive(primitive, pattern)))));
    }

    private static XElement BuildPrimitive(DrawingMlPatternFillPrimitive primitive, PdfPatternFill pattern)
    {
        var color = primitive.ColorRole == DrawingMlPatternFillColorRole.Foreground
            ? pattern.Foreground
            : pattern.Background;
        return primitive switch
        {
            DrawingMlPatternFillRectangle rectangle => FilledPath(RectangleData(rectangle), color),
            DrawingMlPatternFillLine line => new XElement(
                Xps + "Path",
                new XAttribute("Data", FormattableString.Invariant(
                    $"M {line.Start.X},{line.Start.Y} L {line.End.X},{line.End.Y}")),
                new XAttribute("Stroke", Color(color)),
                new XAttribute("StrokeThickness", line.StrokeWidth)),
            DrawingMlPatternFillEllipse ellipse => FilledPath(EllipseData(ellipse), color),
            _ => throw new InvalidOperationException($"Unsupported pattern primitive {primitive.GetType().Name}."),
        };
    }

    private static XElement FilledPath(string data, PdfColor color) =>
        new(Xps + "Path", new XAttribute("Data", data), new XAttribute("Fill", Color(color)));

    private static string RectangleData(DrawingMlPatternFillRectangle rectangle)
    {
        var right = rectangle.X + rectangle.Width;
        var bottom = rectangle.Y + rectangle.Height;
        return FormattableString.Invariant(
            $"M {rectangle.X},{rectangle.Y} L {right},{rectangle.Y} L {right},{bottom} L {rectangle.X},{bottom} Z");
    }

    private static string EllipseData(DrawingMlPatternFillEllipse ellipse)
    {
        var left = ellipse.CenterX - ellipse.RadiusX;
        var right = ellipse.CenterX + ellipse.RadiusX;
        return FormattableString.Invariant(
            $"M {right},{ellipse.CenterY} A {ellipse.RadiusX},{ellipse.RadiusY} 0 1 1 {left},{ellipse.CenterY} A {ellipse.RadiusX},{ellipse.RadiusY} 0 1 1 {right},{ellipse.CenterY} Z");
    }

    private static string Color(PdfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // Coordinate limits from table 18-1 of the XPS 1.0 specification.
    private static bool IsRepresentableTileExtent(double extent) => extent is >= 1e-12 and <= 1e12;
}
