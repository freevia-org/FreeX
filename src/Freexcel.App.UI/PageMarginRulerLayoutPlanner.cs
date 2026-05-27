using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class PageMarginRulerLayoutPlanner
{
    private const double HandleLength = 12;
    private const double HandleThickness = 8;

    public static PageMarginRulerHandles CalculateHandles(
        Rect pageBounds,
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
            new Rect(
                marginLeft - HandleThickness / 2,
                pageBounds.Top - HandleLength - 2,
                HandleThickness,
                HandleLength),
            new Rect(
                marginRight - HandleThickness / 2,
                pageBounds.Top - HandleLength - 2,
                HandleThickness,
                HandleLength),
            new Rect(
                pageBounds.Left - HandleLength - 2,
                marginTop - HandleThickness / 2,
                HandleLength,
                HandleThickness),
            new Rect(
                pageBounds.Left - HandleLength - 2,
                marginBottom - HandleThickness / 2,
                HandleLength,
                HandleThickness));
    }

    public static WorksheetPageMarginEdge? HitTestHandles(
        PageMarginRulerHandles handles,
        Point pos,
        bool showRulers = true)
    {
        if (!showRulers) return null;
        if (ContainsInclusive(handles.Left, pos))
            return WorksheetPageMarginEdge.Left;
        if (ContainsInclusive(handles.Right, pos))
            return WorksheetPageMarginEdge.Right;
        if (ContainsInclusive(handles.Top, pos))
            return WorksheetPageMarginEdge.Top;
        if (ContainsInclusive(handles.Bottom, pos))
            return WorksheetPageMarginEdge.Bottom;

        return null;
    }

    private static bool ContainsInclusive(Rect rect, Point pos) =>
        pos.X >= rect.Left &&
        pos.X <= rect.Right &&
        pos.Y >= rect.Top &&
        pos.Y <= rect.Bottom;
}
