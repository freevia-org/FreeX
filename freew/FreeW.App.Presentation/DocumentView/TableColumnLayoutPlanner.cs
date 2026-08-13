using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public readonly record struct TableCellContentMeasurement(
    int RowIndex,
    int CellIndex,
    double ContentWidthDip);

/// <summary>Owns renderer-neutral table width, column allocation, and content-autofit geometry.</summary>
public static class TableColumnLayoutPlanner
{
    public const double DefaultMinimumUndeclaredWidthDip = 40;
    public const double DefaultContentAllowanceDip = 14;

    public static double ResolveTableWidthDip(Table table, double? measuredWidthDip = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        return measuredWidthDip is > 0
            ? measuredWidthDip.Value
            : table.PreferredWidthPt is > 0
                ? PageLayout.PointsToDip(table.PreferredWidthPt.Value)
                : table.ColumnWidthsPt.Count > 0
                    ? table.ColumnWidthsPt.Where(width => width > 0).Sum(PageLayout.PointsToDip)
                    : 0;
    }

    public static double[] AllocateColumnWidths(
        Table table,
        int columnCount,
        double availableWidthDip,
        double minimumUndeclaredWidthDip = DefaultMinimumUndeclaredWidthDip)
    {
        ArgumentNullException.ThrowIfNull(table);
        var count = Math.Max(0, columnCount);
        if (count == 0)
            return [];

        var availableWidth = Math.Max(0, availableWidthDip);
        var minimumWidth = Math.Max(0, minimumUndeclaredWidthDip);
        var widths = new double[count];
        var declaredWidth = 0d;
        var declaredCount = 0;
        for (var column = 0; column < count; column++)
        {
            var width = column < table.ColumnWidthsPt.Count
                ? PageLayout.PointsToDip(Math.Max(0, table.ColumnWidthsPt[column]))
                : 0;
            widths[column] = width;
            if (width <= 0)
                continue;
            declaredWidth += width;
            declaredCount++;
        }

        var undeclaredCount = count - declaredCount;
        var undeclaredWidth = undeclaredCount > 0
            ? Math.Max(minimumWidth, (availableWidth - declaredWidth) / undeclaredCount)
            : 0;
        for (var column = 0; column < count; column++)
        {
            if (widths[column] <= 0)
                widths[column] = undeclaredCount > 0 ? undeclaredWidth : availableWidth / count;
        }

        ScaleDownToFit(widths, availableWidth);
        return widths;
    }

    public static IReadOnlyList<double>? BuildContentAutoFitWidths(
        Table table,
        double availableWidthDip,
        IReadOnlyList<TableCellContentMeasurement> measurements,
        double contentAllowanceDip = DefaultContentAllowanceDip)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(measurements);
        var columnCount = table.ColumnCount;
        if (table.AutoFit != AutoFitMode.Contents
            || columnCount == 0
            || table.Rows.SelectMany(row => row.Cells).Any(cell =>
                cell.TextDirection != CellTextDirection.Horizontal))
        {
            return null;
        }

        var allowance = Math.Max(0, contentAllowanceDip);
        var widths = Enumerable.Repeat(allowance, columnCount).ToArray();
        var measuredByCell = measurements
            .Where(measurement => measurement.RowIndex >= 0 && measurement.CellIndex >= 0)
            .GroupBy(measurement => (measurement.RowIndex, measurement.CellIndex))
            .ToDictionary(
                group => group.Key,
                group => Math.Max(0, group.Last().ContentWidthDip));
        var spanningRequirements = new List<(int StartColumn, int Span, double RequiredWidth)>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            foreach (var projected in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
            {
                var span = TableGridProjection.SpanWithinWidth(projected, widths.Length);
                if (span <= 0)
                    break;
                var contentWidth = measuredByCell.GetValueOrDefault((rowIndex, projected.CellIndex));
                var requiredWidth = contentWidth + allowance * span;
                if (span == 1)
                    widths[projected.StartColumn] = Math.Max(widths[projected.StartColumn], requiredWidth);
                else
                    spanningRequirements.Add((projected.StartColumn, span, requiredWidth));
            }
        }

        foreach (var requirement in spanningRequirements)
        {
            var currentWidth = widths
                .Skip(requirement.StartColumn)
                .Take(requirement.Span)
                .Sum();
            if (currentWidth >= requirement.RequiredWidth)
                continue;

            var widestColumn = Enumerable.Range(requirement.StartColumn, requirement.Span)
                .MaxBy(column => widths[column]);
            widths[widestColumn] += requirement.RequiredWidth - currentWidth;
        }

        var availableWidth = Math.Max(0, availableWidthDip);
        var targetWidth = table.PreferredWidthPt is > 0
            ? Math.Min(availableWidth, PageLayout.PointsToDip(table.PreferredWidthPt.Value))
            : table.ColumnWidthsPt.Count == columnCount
                ? Math.Min(
                    availableWidth,
                    table.ColumnWidthsPt.Where(width => width > 0).Sum(PageLayout.PointsToDip))
                : 0;
        if (targetWidth > 0)
            ScaleToWidth(widths, targetWidth);
        else
            ScaleDownToFit(widths, availableWidth);

        return widths;
    }

    private static void ScaleDownToFit(double[] widths, double availableWidthDip)
    {
        var totalWidth = widths.Sum();
        if (totalWidth > availableWidthDip && totalWidth > 0)
            ScaleToWidth(widths, availableWidthDip);
    }

    private static void ScaleToWidth(double[] widths, double targetWidthDip)
    {
        var totalWidth = widths.Sum();
        if (totalWidth <= 0)
            return;

        var scale = Math.Max(0, targetWidthDip) / totalWidth;
        for (var index = 0; index < widths.Length; index++)
            widths[index] *= scale;
    }
}
