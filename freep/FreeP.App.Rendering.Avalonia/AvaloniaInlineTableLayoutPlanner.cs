using Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia;

internal readonly record struct AvaloniaInlineTableCellTextPlan(
    Rect Area,
    Point Origin);

internal static class AvaloniaInlineTableLayoutPlanner
{
    private const double PtToDip = 96.0 / 72.0;
    private const double DefaultCellInsetDip = 2;

    internal static AvaloniaInlineTableCellTextPlan PlanCellText(
        TableCell? cell,
        Rect bounds,
        double measuredTextHeight)
    {
        var area = GetTextArea(cell, bounds);
        return new(area, GetTextOrigin(cell, area, measuredTextHeight));
    }

    internal static Rect GetTextArea(TableCell? cell, Rect bounds)
    {
        double left = ResolveInset(cell?.InsetLeftPt);
        double top = ResolveInset(cell?.InsetTopPt);
        double right = ResolveInset(cell?.InsetRightPt);
        double bottom = ResolveInset(cell?.InsetBottomPt);
        var area = new Rect(
            bounds.X + left,
            bounds.Y + top,
            Math.Max(1, bounds.Width - left - right),
            Math.Max(1, bounds.Height - top - bottom));
        return area;
    }

    internal static double GetHorizontalOffset(
        TableRowHorizontalAlignment? alignment,
        double availableWidth,
        double rowWidth)
    {
        double extra = Math.Max(0, availableWidth - rowWidth);
        return alignment switch
        {
            TableRowHorizontalAlignment.Center => extra / 2,
            TableRowHorizontalAlignment.Right => extra,
            _ => 0,
        };
    }

    internal static Point GetTextOrigin(
        TableCell? cell,
        Rect area,
        double measuredTextHeight)
    {
        double extraHeight = Math.Max(0, area.Height - measuredTextHeight);
        double offset = cell?.Anchor switch
        {
            TableCellAnchor.Middle => extraHeight / 2,
            TableCellAnchor.Bottom => extraHeight,
            _ => 0,
        };
        return new(area.X, area.Y + offset);
    }

    private static double ResolveInset(double? points) =>
        points is { } value && value >= 0
            ? value * PtToDip
            : DefaultCellInsetDip;
}
