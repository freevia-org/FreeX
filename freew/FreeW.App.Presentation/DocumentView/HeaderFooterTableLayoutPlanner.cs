using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>Measured paragraph advance, including paragraph spacing but excluding cell margins.</summary>
public readonly record struct HeaderFooterTableCellMeasurement(int RowIndex, int CellIndex, double HeightDip);

public sealed record HeaderFooterTableCellLayout(
    int RowIndex,
    int CellIndex,
    int GridColumn,
    int GridSpan,
    int RowSpan,
    TableCell SourceCell,
    DocumentFloatRect Bounds,
    DocumentFloatRect ContentBounds,
    double ContentOffsetDip,
    DocumentTableCellEffectiveFillPlan Fill,
    TableCellBorderVisualPlan Borders);

public sealed record HeaderFooterTableLayoutPlan(
    double LeftOffsetDip,
    double WidthDip,
    double HeightDip,
    IReadOnlyList<double> ColumnWidthsDip,
    IReadOnlyList<double> RowHeightsDip,
    IReadOnlyList<HeaderFooterTableCellLayout> Cells);

/// <summary>
/// Geometry for a preserved header/footer layout table. Text is measured by the real renderer;
/// this planner never estimates glyph widths, clones paragraphs, or assigns editing offsets.
/// </summary>
public static class HeaderFooterTableLayoutPlanner
{
    public static HeaderFooterTableLayoutPlan Build(
        Table table,
        double availableWidthDip,
        bool usesLegacyCompatibilityLayout,
        IReadOnlyList<HeaderFooterTableCellMeasurement>? measurements = null,
        double dipPerPoint = 96d / 72)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!double.IsFinite(availableWidthDip) || availableWidthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidthDip));
        if (!double.IsFinite(dipPerPoint) || dipPerPoint <= 0)
            throw new ArgumentOutOfRangeException(nameof(dipPerPoint));

        var columns = Math.Clamp(Math.Max(table.ColumnWidthsPt.Count, TableGridProjection.TableWidth(table)),
            0, TableColumnLayoutPlanner.MaximumLaidOutColumns);
        if (columns == 0 || table.Rows.Count == 0)
            return new(0, 0, 0, [], [], []);

        var completeGrid = table.ColumnWidthsPt.Count >= columns
            && table.ColumnWidthsPt.Take(columns).All(width => double.IsFinite(width) && width > 0);
        var gridWidth = completeGrid ? table.ColumnWidthsPt.Take(columns).Sum() * dipPerPoint : 0;
        var width = ResolveWidth(table, availableWidthDip, usesLegacyCompatibilityLayout, gridWidth, dipPerPoint);
        // The shared allocator uses document DIPs (96dpi). Scale its source and result when a caller
        // requests another unit scale, without mutating the original table/grid.
        const double documentDipPerPoint = 96d / 72;
        var scale = dipPerPoint / documentDipPerPoint;
        var columnWidths = TableColumnLayoutPlanner.AllocateColumnWidths(table, columns, width / scale)
            .Select(value => value * scale).ToArray();
        width = columnWidths.Sum();
        var indent = double.IsFinite(table.IndentFromLeftPt ?? 0) ? (table.IndentFromLeftPt ?? 0) * dipPerPoint : 0;
        var outdent = FiniteNonNegative(TableCompatibilityIndentPlanner.ResolveLeftOutdentPt(usesLegacyCompatibilityLayout, table)) * dipPerPoint;
        var slack = Math.Max(0, availableWidthDip - width - indent);
        var left = table.Alignment switch
        {
            TableAlignment.Center => indent + slack / 2,
            TableAlignment.Right => indent + slack,
            _ => indent - outdent,
        };
        var heights = new double[table.Rows.Count];
        var measured = (measurements ?? []).GroupBy(item => (item.RowIndex, item.CellIndex))
            .ToDictionary(group => group.Key, group => FiniteNonNegative(group.Last().HeightDip));
        var projected = table.Rows.Select((row, index) => (row, index))
            .SelectMany(pair => TableGridProjection.ProjectRow(pair.row)
                .Where(cell => cell.StartColumn < columns)
                .Select(cell => (Row: pair.index, Cell: cell))).ToArray();

        foreach (var (rowIndex, cell) in projected)
        {
            if (cell.Cell.VerticalMerge == VerticalMergeState.Continue)
                continue;
            var span = MergeRowSpan(table, rowIndex, cell.StartColumn);
            if (span > 1)
                continue; // A merged cell reserves the sum of its rows, below.
            var margins = Margins(table, cell.Cell);
            var content = measured.GetValueOrDefault((rowIndex, cell.CellIndex));
            heights[rowIndex] = Math.Max(heights[rowIndex], content + (margins.TopPt + margins.BottomPt) * dipPerPoint);
        }
        for (var rowIndex = 0; rowIndex < heights.Length; rowIndex++)
            heights[rowIndex] = RowHeight(table.Rows[rowIndex], heights[rowIndex], dipPerPoint);

        foreach (var (rowIndex, cell) in projected)
        {
            if (cell.Cell.VerticalMerge != VerticalMergeState.Restart)
                continue;
            var span = MergeRowSpan(table, rowIndex, cell.StartColumn);
            var margins = Margins(table, cell.Cell);
            var required = measured.GetValueOrDefault((rowIndex, cell.CellIndex))
                + (margins.TopPt + margins.BottomPt) * dipPerPoint;
            var allocated = heights.Skip(rowIndex).Take(span).Sum();
            if (required <= allocated)
                continue;
            // An exact row must keep its authored height. Grow the last non-exact row in the merge;
            // an all-exact region deliberately clips overflowing content instead of moving its edges.
            for (var end = rowIndex + span - 1; end >= rowIndex; end--)
            {
                if (table.Rows[end].HeightRule == TableRowHeightRule.Exact && table.Rows[end].HeightPt is > 0)
                    continue;
                heights[end] += required - allocated;
                break;
            }
        }

        var columnOffsets = PrefixSums(columnWidths);
        var rowOffsets = PrefixSums(heights);
        var cells = new List<HeaderFooterTableCellLayout>();
        foreach (var (rowIndex, cell) in projected)
        {
            if (cell.Cell.VerticalMerge == VerticalMergeState.Continue)
                continue;
            var gridSpan = TableGridProjection.SpanWithinWidth(cell, columns);
            var rowSpan = MergeRowSpan(table, rowIndex, cell.StartColumn);
            var margins = Margins(table, cell.Cell);
            var cellWidth = columnOffsets[cell.StartColumn + gridSpan] - columnOffsets[cell.StartColumn];
            var cellHeight = TableCellVerticalLayoutPlanner.ResolveRegionHeight(table, heights, rowIndex, cell.StartColumn);
            var leftPadding = margins.LeftPt * dipPerPoint;
            var topPadding = margins.TopPt * dipPerPoint;
            var bottomPadding = margins.BottomPt * dipPerPoint;
            var contentOffset = TableCellVerticalLayoutPlanner.ResolveContentOffset(cell.Cell.VerticalAlignment,
                cellHeight, measured.GetValueOrDefault((rowIndex, cell.CellIndex)), (topPadding + bottomPadding) / 2);
            var x = left + (table.RightToLeft
                ? width - columnOffsets[cell.StartColumn + gridSpan]
                : columnOffsets[cell.StartColumn]);
            var y = rowOffsets[rowIndex];
            cells.Add(new HeaderFooterTableCellLayout(rowIndex, cell.CellIndex, cell.StartColumn, gridSpan, rowSpan,
                cell.Cell,
                new DocumentFloatRect(x, y, cellWidth, cellHeight),
                new DocumentFloatRect(x + leftPadding, y + topPadding,
                    Math.Max(0, cellWidth - leftPadding - margins.RightPt * dipPerPoint),
                    Math.Max(0, cellHeight - topPadding - bottomPadding)),
                contentOffset,
                DocumentViewLayoutPlanner.BuildTableCellEffectiveFillPlan(table, rowIndex, cell.CellIndex,
                    cell.StartColumn, gridSpan, columns),
                TableCellBorderVisualPlanner.Build(ResolveBorders(table, cell.Cell, rowIndex, rowSpan,
                    cell.StartColumn, gridSpan, columns), dipPerPoint)));
        }
        return new(left, width, rowOffsets[^1], columnWidths, heights, cells);
    }

    private static double ResolveWidth(Table table, double available, bool legacy, double completeGridWidth, double scale)
    {
        // Legacy layout tables carry the complete physical grid, including the outdented cell padding.
        // Re-scaling it to the text width loses both that padding and the authored cell proportions.
        if (legacy && completeGridWidth > 0
            && (table.AutoFit == AutoFitMode.Window || table.PreferredWidthPt is not > 0))
            return completeGridWidth;
        if (table.AutoFit == AutoFitMode.Window)
            return available * FinitePositiveOr(table.PreferredWidthPercent, 100) / 100;
        if (table.PreferredWidthPt is > 0 and { } preferred && double.IsFinite(preferred))
            return preferred * scale;
        return completeGridWidth > 0 ? completeGridWidth : available;
    }

    private static CellBorders ResolveBorders(Table table, TableCell cell, int row, int rowSpan,
        int column, int span, int columns)
    {
        var catalog = table.TableStyleId is { Length: > 0 } id ? DocumentTableStyle.FindById(id) : null;
        var fallback = table.Borders is null && (table.Formatting.Borders || catalog?.Borders is true)
            ? new CellBorderEdge(ColorHex: catalog?.BorderColorHex ?? "#9A9A9A") : null;
        CellBorderEdge? Convert(TableBorderEdge? edge) => edge is null ? null : new(edge.Style,
            string.Equals(edge.ColorToken, "auto", StringComparison.OrdinalIgnoreCase) ? "#000000" : edge.ColorToken, edge.WidthPt);
        var own = cell.Borders;
        return new CellBorders
        {
            Top = own?.NoTop is true ? null : own?.Top ?? Convert(row == 0 ? table.Borders?.Top : table.Borders?.InsideHorizontal) ?? fallback,
            Bottom = own?.NoBottom is true ? null : own?.Bottom ?? Convert(row + rowSpan == table.Rows.Count ? table.Borders?.Bottom : table.Borders?.InsideHorizontal) ?? fallback,
            Left = own?.NoLeft is true ? null : own?.Left ?? Convert((table.RightToLeft ? column + span == columns : column == 0) ? table.Borders?.Left : table.Borders?.InsideVertical) ?? fallback,
            Right = own?.NoRight is true ? null : own?.Right ?? Convert((table.RightToLeft ? column == 0 : column + span == columns) ? table.Borders?.Right : table.Borders?.InsideVertical) ?? fallback,
        };
    }

    private static TableCellMargins Margins(Table table, TableCell cell)
    {
        var margins = cell.Margins ?? table.DefaultCellMargins ?? TableCellMargins.Default;
        return new(FiniteNonNegative(margins.TopPt), FiniteNonNegative(margins.LeftPt),
            FiniteNonNegative(margins.BottomPt), FiniteNonNegative(margins.RightPt));
    }

    private static int MergeRowSpan(Table table, int rowIndex, int column)
    {
        if (TableGridProjection.StartingAt(table.Rows[rowIndex], column)?.Cell.VerticalMerge != VerticalMergeState.Restart)
            return 1;
        var end = rowIndex + 1;
        while (end < table.Rows.Count
            && TableGridProjection.StartingAt(table.Rows[end], column)?.Cell.VerticalMerge == VerticalMergeState.Continue)
            end++;
        return end - rowIndex;
    }

    private static double RowHeight(TableRow row, double measured, double scale) =>
        row.HeightRule == TableRowHeightRule.Exact && row.HeightPt is > 0 and { } exact
            ? FiniteNonNegative(exact) * scale
            : Math.Max(measured, FiniteNonNegative(row.HeightPt ?? 0) * scale);

    private static double[] PrefixSums(IReadOnlyList<double> values)
    {
        var offsets = new double[values.Count + 1];
        for (var index = 0; index < values.Count; index++)
            offsets[index + 1] = offsets[index] + values[index];
        return offsets;
    }

    private static double FiniteNonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    private static double FinitePositiveOr(double? value, double fallback) =>
        value is > 0 and { } number && double.IsFinite(number) ? number : fallback;
}
