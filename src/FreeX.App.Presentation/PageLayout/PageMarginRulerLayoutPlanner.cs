using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// The four draggable ruler handles drawn outside a page rectangle in the Page Layout view: the left
/// and right handles sit on the top ruler, the top and bottom handles sit on the left ruler. Each is a
/// small pixel-space rectangle a renderer paints and the host hit-tests against.
/// </summary>
public readonly record struct PageMarginRulerHandles(
    LayoutRect Left,
    LayoutRect Right,
    LayoutRect Top,
    LayoutRect Bottom);

/// <summary>
/// Pure ruler-handle geometry for the Page Layout view, shared by the desktop hosts. Places the four
/// margin handles on the rulers around a page rectangle using the paper-relative margin fractions, and
/// hit-tests a pointer against them.
/// </summary>
public static class PageMarginRulerLayoutPlanner
{
    private const double HandleLength = 12;
    private const double HandleThickness = 8;

    public static PageMarginRulerHandles CalculateHandles(
        LayoutRect pageBounds,
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        var guide = WorksheetPageLayout.GetMarginGuideFractions(paperSize, orientation, margins);
        var marginLeft = pageBounds.Left + pageBounds.Width * guide.Left;
        var marginRight = pageBounds.Left + pageBounds.Width * guide.Right;
        var marginTop = pageBounds.Top + pageBounds.Height * guide.Top;
        var marginBottom = pageBounds.Top + pageBounds.Height * guide.Bottom;

        return new PageMarginRulerHandles(
            new LayoutRect(
                marginLeft - HandleThickness / 2,
                pageBounds.Top - HandleLength - 2,
                HandleThickness,
                HandleLength),
            new LayoutRect(
                marginRight - HandleThickness / 2,
                pageBounds.Top - HandleLength - 2,
                HandleThickness,
                HandleLength),
            new LayoutRect(
                pageBounds.Left - HandleLength - 2,
                marginTop - HandleThickness / 2,
                HandleLength,
                HandleThickness),
            new LayoutRect(
                pageBounds.Left - HandleLength - 2,
                marginBottom - HandleThickness / 2,
                HandleLength,
                HandleThickness));
    }

    /// <summary>
    /// Returns the margin edge whose handle contains <paramref name="pointer"/> (boundary inclusive),
    /// or null when no handle is hit or the rulers are hidden.
    /// </summary>
    public static WorksheetPageMarginEdge? HitTestHandles(
        PageMarginRulerHandles handles,
        LayoutPoint pointer,
        bool showRulers = true)
    {
        if (!showRulers)
            return null;
        if (ContainsInclusive(handles.Left, pointer))
            return WorksheetPageMarginEdge.Left;
        if (ContainsInclusive(handles.Right, pointer))
            return WorksheetPageMarginEdge.Right;
        if (ContainsInclusive(handles.Top, pointer))
            return WorksheetPageMarginEdge.Top;
        if (ContainsInclusive(handles.Bottom, pointer))
            return WorksheetPageMarginEdge.Bottom;

        return null;
    }

    private static bool ContainsInclusive(LayoutRect rect, LayoutPoint point) =>
        point.X >= rect.Left &&
        point.X <= rect.Right &&
        point.Y >= rect.Top &&
        point.Y <= rect.Bottom;
}
