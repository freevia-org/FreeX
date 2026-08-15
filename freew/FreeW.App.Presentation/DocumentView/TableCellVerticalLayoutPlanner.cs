using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Owns the renderer-neutral geometry for vertically positioning content in a table cell.
/// Renderers supply their measured row and content heights; the shared planner resolves the
/// logical vertical-merge region and the Word-compatible top/center/bottom offset.
/// </summary>
public static class TableCellVerticalLayoutPlanner
{
    /// <summary>
    /// Returns the measured height of the logical cell region. A vertical-merge restart includes
    /// every consecutive continuation row at the same logical grid column; all other cells use
    /// only their own row.
    /// </summary>
    public static double ResolveRegionHeight(
        Table table,
        IReadOnlyList<double> rowHeightsDip,
        int rowIndex,
        int gridColumn)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rowHeightsDip);
        if (rowHeightsDip.Count < table.Rows.Count)
        {
            throw new ArgumentException(
                "A measured height is required for every table row.",
                nameof(rowHeightsDip));
        }

        if (rowIndex < 0 || rowIndex >= table.Rows.Count || gridColumn < 0)
            return 0;

        var projected = TableGridProjection.StartingAt(table.Rows[rowIndex], gridColumn);
        if (projected is null)
            return 0;

        var regionHeight = NonNegativeFinite(rowHeightsDip[rowIndex]);
        if (projected.Value.Cell.VerticalMerge != VerticalMergeState.Restart)
            return regionHeight;

        for (var continuationRow = rowIndex + 1;
             continuationRow < table.Rows.Count;
             continuationRow++)
        {
            var continuation = TableGridProjection.StartingAt(
                table.Rows[continuationRow],
                gridColumn);
            if (continuation?.Cell.VerticalMerge != VerticalMergeState.Continue)
                break;

            regionHeight += NonNegativeFinite(rowHeightsDip[continuationRow]);
        }

        return regionHeight;
    }

    /// <summary>
    /// Returns the additional offset after the cell's leading padding. Content that is taller than
    /// the available interior remains top-aligned instead of receiving a negative offset.
    /// </summary>
    public static double ResolveContentOffset(
        TableCellVerticalAlignment alignment,
        double regionHeightDip,
        double contentHeightDip,
        double verticalPaddingDip)
    {
        var regionHeight = NonNegativeFinite(regionHeightDip);
        var contentHeight = NonNegativeFinite(contentHeightDip);
        var padding = NonNegativeFinite(verticalPaddingDip);
        var freeSpace = Math.Max(0, regionHeight - (2 * padding) - contentHeight);

        return alignment switch
        {
            TableCellVerticalAlignment.Center => freeSpace / 2,
            TableCellVerticalAlignment.Bottom => freeSpace,
            _ => 0,
        };
    }

    private static double NonNegativeFinite(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
