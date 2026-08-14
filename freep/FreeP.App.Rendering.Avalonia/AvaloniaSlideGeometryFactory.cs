using Avalonia.Media;
using Free.Shared.Drawing;
using Free.Shared.Drawing.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Rendering.Avalonia;

/// <summary>
/// Converts a <see cref="ShapeGeometry"/> (portable contours of Line/CubicBezier/Arc)
/// to an Avalonia <see cref="StreamGeometry"/>.
///
/// Native contour translation is owned by the shared Avalonia drawing adapter.
/// </summary>
internal static class AvaloniaSlideGeometryFactory
{
    /// <summary>
    /// Converts all contours in <paramref name="shape"/> into a single Avalonia
    /// <see cref="StreamGeometry"/> ready for <see cref="DrawingContext.DrawGeometry"/>.
    /// Returns null when the shape has no contours.
    /// </summary>
    internal static StreamGeometry? ToGeometry(ShapeGeometry shape)
    {
        return AvaloniaShapeGeometryAdapter.ToGeometry(shape);
    }
}
