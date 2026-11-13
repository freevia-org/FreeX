using System.Windows;
using System.Windows.Media;
using Free.Shared.Drawing.Wpf;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.UI;

/// <summary>
/// Converts the portable <see cref="ShapeGeometry"/> produced by
/// <see cref="ShapeGeometryBuilder"/> into a WPF <see cref="Geometry"/> for the grid's drawing
/// layer. All shape math lives in the shared presentation layer; this adapter only maps the pure
/// contour/segment description onto <see cref="System.Windows.Media"/> primitives.
/// </summary>
public static class ShapeGeometryWpfAdapter
{
    /// <summary>
    /// Builds the WPF geometry for <paramref name="kind"/> within <paramref name="rect"/> by
    /// delegating the outline math to <see cref="ShapeGeometryBuilder"/>.
    /// </summary>
    public static Geometry Create(DrawingShapeKind kind, Rect rect)
    {
        var shape = ShapeGeometryBuilder.Build(
            kind,
            LayoutRect.FromCorners(rect.Left, rect.Top, rect.Right, rect.Bottom));
        return ToGeometry(shape);
    }

    private static Geometry ToGeometry(ShapeGeometry shape)
    {
        // Preset callouts and compound shapes use overlapping contours. The default even-odd
        // rule turns their overlap into a cutout (for example, a white callout tail); DrawingML
        // treats those contours as a filled union.
        return WpfShapeGeometryAdapter.ToGeometry(shape, FillRule.Nonzero);
    }
}
