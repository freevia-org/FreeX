using Free.Shared.Drawing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Converts between a chart element's pixel rectangle and the OOXML <c>c:manualLayout</c> fractions the
/// model stores (<see cref="ChartManualLayoutModel"/>). Excel writes dragged titles, legends and plot
/// areas with "edge" modes (fractions of the chart space measured from its top-left) and individually
/// dragged data labels with "factor" modes (a fractional offset from the label's automatic position).
/// Pure geometry shared by both hosts so a drag, the renderers and the file round-trip all agree.
/// </summary>
public static class ChartManualLayoutGeometry
{
    public const string EdgeMode = "edge";
    public const string FactorMode = "factor";
    public const string InnerTarget = "inner";

    /// <summary>True when <paramref name="layout"/> pins an element's position (an X and a Y are present).</summary>
    public static bool HasPosition(ChartManualLayoutModel? layout) =>
        layout is { X: { } x, Y: { } y } && double.IsFinite(x) && double.IsFinite(y);

    /// <summary>
    /// Resolves the top-left corner a manual layout puts an element at, in chart pixels. Returns false
    /// when the layout carries no position or the chart size is unknown. <paramref name="automatic"/> is
    /// the element's automatic origin, used by "factor" (offset) layouts.
    /// </summary>
    public static bool TryResolveOrigin(
        ChartManualLayoutModel? layout,
        double chartWidth,
        double chartHeight,
        LayoutPoint automatic,
        out LayoutPoint origin)
    {
        origin = automatic;
        if (!HasPosition(layout) || !(chartWidth > 0) || !(chartHeight > 0))
            return false;

        var x = IsEdge(layout!.XMode) ? layout.X!.Value * chartWidth : automatic.X + (layout.X!.Value * chartWidth);
        var y = IsEdge(layout.YMode) ? layout.Y!.Value * chartHeight : automatic.Y + (layout.Y!.Value * chartHeight);
        origin = new LayoutPoint(x, y);
        return true;
    }

    /// <summary>Resolves position and (when present) size of a manual layout in chart pixels.</summary>
    public static bool TryResolveRect(
        ChartManualLayoutModel? layout,
        double chartWidth,
        double chartHeight,
        LayoutRect automatic,
        out LayoutRect rect)
    {
        rect = automatic;
        if (!TryResolveOrigin(layout, chartWidth, chartHeight, new LayoutPoint(automatic.Left, automatic.Top), out var origin))
            return false;

        var width = automatic.Width;
        var height = automatic.Height;
        if (layout!.Width is { } w && double.IsFinite(w) && w > 0)
            width = IsEdge(layout.WidthMode) ? (w * chartWidth) - origin.X : w * chartWidth;
        if (layout.Height is { } h && double.IsFinite(h) && h > 0)
            height = IsEdge(layout.HeightMode) ? (h * chartHeight) - origin.Y : h * chartHeight;

        rect = new LayoutRect(origin.X, origin.Y, Math.Max(0, width), Math.Max(0, height));
        return true;
    }

    /// <summary>The "edge" layout Excel writes for a dragged title / legend / axis title (position only).</summary>
    public static ChartManualLayoutModel FromOrigin(LayoutPoint origin, double chartWidth, double chartHeight) =>
        new()
        {
            XMode = EdgeMode,
            YMode = EdgeMode,
            X = Round(origin.X / chartWidth),
            Y = Round(origin.Y / chartHeight),
        };

    /// <summary>The layout Excel writes for a dragged legend: position plus size.</summary>
    public static ChartManualLayoutModel FromRect(LayoutRect rect, double chartWidth, double chartHeight) =>
        new()
        {
            XMode = EdgeMode,
            YMode = EdgeMode,
            X = Round(rect.Left / chartWidth),
            Y = Round(rect.Top / chartHeight),
            Width = Round(rect.Width / chartWidth),
            Height = Round(rect.Height / chartHeight),
        };

    /// <summary>The layout Excel writes for a resized/dragged plot area: inner target, edge position, factor size.</summary>
    public static ChartManualLayoutModel FromPlotArea(LayoutRect inner, double chartWidth, double chartHeight)
    {
        var layout = FromRect(inner, chartWidth, chartHeight);
        layout.LayoutTarget = InnerTarget;
        return layout;
    }

    /// <summary>The "factor" layout Excel writes for a dragged data label: an offset from its automatic position.</summary>
    public static ChartManualLayoutModel FromOffset(double dx, double dy, double chartWidth, double chartHeight) =>
        new()
        {
            XMode = FactorMode,
            YMode = FactorMode,
            X = Round(dx / chartWidth),
            Y = Round(dy / chartHeight),
        };

    /// <summary>Keeps <paramref name="rect"/> fully inside a chart of the given size (shifting, never resizing).</summary>
    public static LayoutRect ClampInside(LayoutRect rect, double chartWidth, double chartHeight)
    {
        var x = Math.Clamp(rect.Left, 0, Math.Max(0, chartWidth - rect.Width));
        var y = Math.Clamp(rect.Top, 0, Math.Max(0, chartHeight - rect.Height));
        return new LayoutRect(x, y, rect.Width, rect.Height);
    }

    private static bool IsEdge(string? mode) =>
        string.Equals(mode, EdgeMode, StringComparison.OrdinalIgnoreCase);

    private static double Round(double value) => Math.Round(value, 6);
}
