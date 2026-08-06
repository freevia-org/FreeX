using FreeX.Core.Model;

namespace FreeX.App.Presentation.Rendering;

public enum CellBorderDashPattern
{
    Solid,
    Dash,
    Dot,
    DashDot,
    DashDotDot,
}

public readonly record struct CellBorderStrokePlan(
    double Thickness,
    CellBorderDashPattern DashPattern,
    IReadOnlyList<double>? DashArray,
    bool IsDouble);

public readonly record struct CellBorderLinePrimitive(double X1, double Y1, double X2, double Y2);

public readonly record struct CellBorderDoubleEdgePlan(
    CellBorderLinePrimitive First,
    CellBorderLinePrimitive Second,
    bool HasSecond);

/// <summary>
/// Portable numeric, dash, precedence, and double-edge policy for native cell-border adapters.
/// </summary>
public static class CellBorderVisualPlanner
{
    public const double DoubleEdgeGap = 1.0;

    private static readonly IReadOnlyList<double> Dash = Array.AsReadOnly([2.0, 2.0]);
    private static readonly IReadOnlyList<double> Dot = Array.AsReadOnly([1.0, 2.0]);
    private static readonly IReadOnlyList<double> DashDot = Array.AsReadOnly([2.0, 2.0, 1.0, 2.0]);
    private static readonly IReadOnlyList<double> DashDotDot = Array.AsReadOnly([2.0, 2.0, 1.0, 2.0, 1.0, 2.0]);

    private static readonly BorderStyle[] BorderEdgePrecedence =
    [
        BorderStyle.Double,
        BorderStyle.Thick,
        BorderStyle.Medium,
        BorderStyle.MediumDashDotDot,
        BorderStyle.MediumDashDot,
        BorderStyle.MediumDashed,
        BorderStyle.SlantDashDot,
        BorderStyle.Thin,
        BorderStyle.DashDotDot,
        BorderStyle.DashDot,
        BorderStyle.Dashed,
        BorderStyle.Dotted,
        BorderStyle.Hair,
        BorderStyle.None,
    ];

    public static CellBorderStrokePlan Plan(BorderStyle style)
    {
        var thickness = style switch
        {
            BorderStyle.Hair => 0.25,
            BorderStyle.Thin => 0.5,
            BorderStyle.Medium or BorderStyle.MediumDashed or BorderStyle.MediumDashDot or
                BorderStyle.MediumDashDotDot or BorderStyle.SlantDashDot => 1.5,
            BorderStyle.Thick => 2.5,
            _ => 0.5,
        };

        var dashPattern = style switch
        {
            BorderStyle.Dashed or BorderStyle.MediumDashed => CellBorderDashPattern.Dash,
            BorderStyle.Dotted => CellBorderDashPattern.Dot,
            BorderStyle.DashDot or BorderStyle.MediumDashDot or BorderStyle.SlantDashDot =>
                CellBorderDashPattern.DashDot,
            BorderStyle.DashDotDot or BorderStyle.MediumDashDotDot => CellBorderDashPattern.DashDotDot,
            _ => CellBorderDashPattern.Solid,
        };

        var dashArray = dashPattern switch
        {
            CellBorderDashPattern.Dash => Dash,
            CellBorderDashPattern.Dot => Dot,
            CellBorderDashPattern.DashDot => DashDot,
            CellBorderDashPattern.DashDotDot => DashDotDot,
            _ => null,
        };

        return new CellBorderStrokePlan(thickness, dashPattern, dashArray, style == BorderStyle.Double);
    }

    public static CellBorder ResolveEdgeWinner(CellBorder mine, CellBorder neighbor)
    {
        if (mine.Style == BorderStyle.None)
            return neighbor;
        if (neighbor.Style == BorderStyle.None)
            return mine;

        return GetPrecedenceRank(mine.Style) <= GetPrecedenceRank(neighbor.Style) ? mine : neighbor;
    }

    public static CellBorderDoubleEdgePlan PlanDoubleEdge(
        double x1,
        double y1,
        double x2,
        double y2,
        double lineThickness,
        double effectivePixelsPerDip)
    {
        var scale = BorderStrokePixelSnapper.NormalizePixelsPerDip(effectivePixelsPerDip);
        var linePixels = BorderStrokePixelSnapper.SnapThicknessToDevicePixels(lineThickness, scale);

        if (linePixels > 0 && Math.Abs(y1 - y2) < 0.0001)
        {
            var (offset, center) = GetAxisAlignedDoubleEdgeOffset(y1, linePixels, scale);
            return TwoLines(
                new CellBorderLinePrimitive(x1, center - offset, x2, center - offset),
                new CellBorderLinePrimitive(x1, center + offset, x2, center + offset));
        }

        if (linePixels > 0 && Math.Abs(x1 - x2) < 0.0001)
        {
            var (offset, center) = GetAxisAlignedDoubleEdgeOffset(x1, linePixels, scale);
            return TwoLines(
                new CellBorderLinePrimitive(center - offset, y1, center - offset, y2),
                new CellBorderLinePrimitive(center + offset, y1, center + offset, y2));
        }

        var deltaX = x2 - x1;
        var deltaY = y2 - y1;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length < 1e-6)
        {
            var line = new CellBorderLinePrimitive(x1, y1, x2, y2);
            return new CellBorderDoubleEdgePlan(line, default, HasSecond: false);
        }

        var offsetX = -deltaY / length * (DoubleEdgeGap / 2.0);
        var offsetY = deltaX / length * (DoubleEdgeGap / 2.0);
        return TwoLines(
            new CellBorderLinePrimitive(x1 + offsetX, y1 + offsetY, x2 + offsetX, y2 + offsetY),
            new CellBorderLinePrimitive(x1 - offsetX, y1 - offsetY, x2 - offsetX, y2 - offsetY));
    }

    private static (double Offset, double Center) GetAxisAlignedDoubleEdgeOffset(
        double coordinate,
        int linePixels,
        double scale)
    {
        var gapPixels = Math.Max(1, (int)Math.Round(DoubleEdgeGap * scale, MidpointRounding.AwayFromZero));
        var totalThickness = ((linePixels * 2.0) + gapPixels) / scale;
        var offset = (linePixels + gapPixels) / (2.0 * scale);
        var center = BorderStrokePixelSnapper.SnapCenter(coordinate, totalThickness, scale);
        return (offset, center);
    }

    private static CellBorderDoubleEdgePlan TwoLines(
        CellBorderLinePrimitive first,
        CellBorderLinePrimitive second) =>
        new(first, second, HasSecond: true);

    private static int GetPrecedenceRank(BorderStyle style)
    {
        var index = Array.IndexOf(BorderEdgePrecedence, style);
        return index < 0 ? BorderEdgePrecedence.Length : index;
    }
}
