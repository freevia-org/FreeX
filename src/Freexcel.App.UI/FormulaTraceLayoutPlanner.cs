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
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        var useMetricLookups = arrows.Count > 1;
        Dictionary<uint, RowMetric>? rowLookup = null;
        Dictionary<uint, ColMetric>? colLookup = null;
        foreach (var arrow in arrows)
        {
            var fromOnCurrentSheet = arrow.From.Sheet.Equals(sheetId);
            var toOnCurrentSheet = arrow.To.Sheet.Equals(sheetId);
            var fromVisible = fromOnCurrentSheet && TryGetCellRect(viewport, arrow.From, rowHeaderWidth, useMetricLookups, ref rowLookup, ref colLookup, out var fromRect);
            var toVisible = toOnCurrentSheet && TryGetCellRect(viewport, arrow.To, rowHeaderWidth, useMetricLookups, ref rowLookup, ref colLookup, out var toRect);

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

    private static Dictionary<uint, RowMetric> BuildRowMetricLookup(ViewportModel viewport)
    {
        var lookup = new Dictionary<uint, RowMetric>(viewport.RowMetrics.Count);
        foreach (var row in viewport.RowMetrics)
        {
            if (!lookup.ContainsKey(row.Row))
                lookup.Add(row.Row, row);
        }

        return lookup;
    }

    private static Dictionary<uint, ColMetric> BuildColMetricLookup(ViewportModel viewport)
    {
        var lookup = new Dictionary<uint, ColMetric>(viewport.ColMetrics.Count);
        foreach (var col in viewport.ColMetrics)
        {
            if (!lookup.ContainsKey(col.Col))
                lookup.Add(col.Col, col);
        }

        return lookup;
    }

    private static bool TryGetCellRect(
        ViewportModel viewport,
        CellAddress address,
        double rowHeaderWidth,
        bool useMetricLookups,
        ref Dictionary<uint, RowMetric>? rowLookup,
        ref Dictionary<uint, ColMetric>? colLookup,
        out Rect rect)
    {
        var row = useMetricLookups
            ? GetRowMetric(viewport, address.Row, ref rowLookup)
            : viewport.RowMetrics.FirstOrDefault(r => r.Row == address.Row);
        var col = useMetricLookups
            ? GetColMetric(viewport, address.Col, ref colLookup)
            : viewport.ColMetrics.FirstOrDefault(c => c.Col == address.Col);
        if (row is null || col is null)
        {
            rect = Rect.Empty;
            return false;
        }

        rect = new Rect(
            col.LeftOffset + rowHeaderWidth,
            row.TopOffset + GridView.ColHeaderHeight,
            col.Width,
            row.Height);
        return true;
    }

    private static RowMetric? GetRowMetric(
        ViewportModel viewport,
        uint row,
        ref Dictionary<uint, RowMetric>? rowLookup)
    {
        rowLookup ??= BuildRowMetricLookup(viewport);
        return rowLookup.TryGetValue(row, out var metric) ? metric : null;
    }

    private static ColMetric? GetColMetric(
        ViewportModel viewport,
        uint col,
        ref Dictionary<uint, ColMetric>? colLookup)
    {
        colLookup ??= BuildColMetricLookup(viewport);
        return colLookup.TryGetValue(col, out var metric) ? metric : null;
    }
}
