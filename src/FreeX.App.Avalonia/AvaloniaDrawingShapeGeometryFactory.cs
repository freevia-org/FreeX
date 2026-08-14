using Avalonia.Media;
using Free.Shared.Drawing.Avalonia;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds Avalonia <see cref="Geometry"/> outlines for <see cref="DrawingShapeKind"/> values so the
/// Avalonia shell can render real shape silhouettes (triangles, arrows, stars, flowchart symbols,
/// callouts, signs, etc.) instead of falling back to a plain rectangle. The shape math lives in the
/// portable <see cref="ShapeGeometryBuilder"/>; the shared Avalonia drawing adapter translates the
/// resulting contours. Geometry is authored inside a (0,0,width,height) box.
/// Returns <c>null</c> for kinds best handled by the existing Ellipse / Line / Rectangle render path.
/// </summary>
internal static class AvaloniaDrawingShapeGeometryFactory
{
    public static Geometry? CreateGeometry(DrawingShapeKind kind, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return null;

        // Ellipse and plain Rectangle stay on the dedicated control path. The portable builder
        // emits real geometry for these too, so the adapter must opt out explicitly to preserve the
        // call site's null-means-use-the-dedicated-control contract.
        // Line previously lived here too, but is now routed through the geometry path so that it
        // renders at the correct angle and length (the stub Border approach had no angle/length).
        switch (kind)
        {
            case DrawingShapeKind.Rectangle:
            case DrawingShapeKind.Ellipse:
                return null;
        }

        var shape = ShapeGeometryBuilder.Build(kind, new LayoutRect(0, 0, width, height));
        return AvaloniaShapeGeometryAdapter.ToGeometry(shape);
    }
}
