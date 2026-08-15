using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Shared geometry and color contract for Data Validation circles across interactive, print-preview,
/// and PDF renderers. The ellipse proportions match the WPF grid and native print authority.
/// </summary>
public static class ValidationCircleLayoutPlanner
{
    public static readonly PresentationRgb StrokeColor = new(226, 28, 33);
    public const double StrokeThickness = 1.5;

    public static LayoutRect CalculateEllipseBounds(LayoutRect cellBounds)
    {
        var radiusX = Math.Max(2.0, cellBounds.Width * 0.38);
        var radiusY = Math.Max(2.0, cellBounds.Height * 0.32);
        var centerX = cellBounds.Left + (cellBounds.Width / 2.0);
        var centerY = cellBounds.Top + (cellBounds.Height / 2.0);
        return new LayoutRect(
            centerX - radiusX,
            centerY - radiusY,
            radiusX * 2.0,
            radiusY * 2.0);
    }
}
