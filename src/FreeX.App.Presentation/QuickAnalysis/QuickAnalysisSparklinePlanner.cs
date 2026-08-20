using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// Plans the sparkline inserts a Quick Analysis Sparklines suggestion performs over a selection: one
/// sparkline per data row, drawn from that row's selected cells, placed in the column immediately to the
/// right of the selection. UI-free so the placement is unit testable.
/// </summary>
public static class QuickAnalysisSparklinePlanner
{
    /// <summary>
    /// Builds an <see cref="AddSparklineCommand"/> for each data row in <paramref name="range"/>. When the
    /// range has a header row the header is skipped so the sparkline spans only the row's values. Returns an
    /// empty list when the selection is degenerate (single column, or no data rows), since a sparkline needs
    /// at least two points and a target column inside the sheet.
    /// </summary>
    /// <remarks>
    /// When more than one row produces a sparkline, every command shares one nonzero
    /// <see cref="FreeX.Core.Model.SparklineModel.GroupId"/> (allocated via
    /// <see cref="FreeX.Core.Model.SparklineGroupIdAllocator"/> against <paramref name="existingSparklines"/>),
    /// matching how Excel's own Quick Analysis Sparklines gesture inserts one shared group across the
    /// selection -- and how <see cref="FreeX.App.Presentation.SparklineUI.SparklinePlanner.BuildInsertCommand"/>
    /// groups a multi-cell "Insert Sparklines" Location Range. A lone sparkline (single data row) stays
    /// ungrouped (<c>GroupId == 0</c>), matching Excel and <c>BuildInsertCommand</c>'s single-member case.
    /// </remarks>
    public static IReadOnlyList<AddSparklineCommand> BuildCommands(
        SheetId sheetId,
        GridRange range,
        bool hasHeaderRow,
        SparklineKind kind,
        IEnumerable<SparklineModel>? existingSparklines = null)
    {
        if (range.ColCount < 2)
            return [];

        var targetCol = range.End.Col + 1;
        if (targetCol > CellAddress.MaxCol)
            return [];

        var firstDataRow = hasHeaderRow && range.RowCount > 1 ? range.Start.Row + 1 : range.Start.Row;
        if (firstDataRow > range.End.Row)
            return [];

        var rowCount = (int)(range.End.Row - firstDataRow + 1);
        var groupId = rowCount > 1
            ? SparklineGroupIdAllocator.NextGroupId(existingSparklines ?? [])
            : 0;

        var commands = new List<AddSparklineCommand>(rowCount);
        for (var row = firstDataRow; row <= range.End.Row; row++)
        {
            var dataRange = new GridRange(
                new CellAddress(sheetId, row, range.Start.Col),
                new CellAddress(sheetId, row, range.End.Col));
            var location = new CellAddress(sheetId, row, targetCol);
            commands.Add(groupId == 0
                ? new AddSparklineCommand(sheetId, dataRange, location, kind)
                : new AddSparklineCommand(sheetId, dataRange, location, kind, groupId));
        }

        return commands;
    }
}
