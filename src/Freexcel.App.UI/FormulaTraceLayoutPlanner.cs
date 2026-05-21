using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class FormulaTraceLayoutPlanner
{
    public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId)
    {
        var layouts = new List<FormulaTraceArrowLayout>();
        foreach (var arrow in arrows)
        {
            var fromOnCurrentSheet = arrow.From.Sheet.Equals(sheetId);
            var toOnCurrentSheet = arrow.To.Sheet.Equals(sheetId);
            var fromVisible = fromOnCurrentSheet && TryGetCellRect(viewport, arrow.From, out var fromRect);
            var toVisible = toOnCurrentSheet && TryGetCellRect(viewport, arrow.To, out var toRect);

            if (fromVisible && toVisible)
            {
                layouts.Add(new FormulaTraceArrowLayout(
                    CenterOf(fromRect),
                    CenterOf(toRect)));
                continue;
            }

            var markerKind = fromOnCurrentSheet && toOnCurrentSheet
                ? FormulaTraceArrowLayoutKind.OffscreenMarker
                : FormulaTraceArrowLayoutKind.CrossSheetMarker;

            if (fromVisible)
                layouts.Add(new FormulaTraceArrowLayout(CenterOf(fromRect), CenterOf(fromRect), markerKind, arrow.To));
            else if (toVisible)
                layouts.Add(new FormulaTraceArrowLayout(CenterOf(toRect), CenterOf(toRect), markerKind, arrow.From));
        }

        return layouts;
    }

    public static CellAddress? HitTestMarker(
        ViewportModel viewport,
        IReadOnlyList<FormulaTraceArrow> arrows,
        SheetId sheetId,
        Point pos)
    {
        const double hitRadius = 8;
        foreach (var arrow in CalculateLayouts(viewport, arrows, sheetId))
        {
            if (arrow.Kind == FormulaTraceArrowLayoutKind.VisibleArrow ||
                arrow.NavigationTarget is null ||
                (arrow.Start - pos).Length > hitRadius)
            {
                continue;
            }

            return arrow.NavigationTarget.Value;
        }

        return null;
    }

    private static Point CenterOf(Rect rect) =>
        new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private static bool TryGetCellRect(ViewportModel viewport, CellAddress address, out Rect rect)
    {
        var row = viewport.RowMetrics.FirstOrDefault(r => r.Row == address.Row);
        var col = viewport.ColMetrics.FirstOrDefault(c => c.Col == address.Col);
        if (row is null || col is null)
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(
            col.LeftOffset + GridView.CalculateRowHeaderWidth(viewport),
            row.TopOffset + GridView.ColHeaderHeight,
            col.Width,
            row.Height);
        return true;
    }
}
