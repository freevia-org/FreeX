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
    /// KNOWN GAP: every command built here constructs an independent <see cref="FreeX.Core.Model.SparklineModel"/>
    /// whose <see cref="FreeX.Core.Model.SparklineModel.GroupId"/> defaults to 0, so on save
    /// (<c>XlsxSparklineMapper.Save</c>, which groups by <c>GroupId == 0 ? Id : GroupId</c>) each row becomes its
    /// own singleton sparkline group instead of one shared group spanning the whole selection, unlike Excel's
    /// Quick Analysis gesture. <see cref="AddSparklineCommand"/> (src/FreeX.Core.Commands/SparklineCommands.cs)
    /// has no constructor parameter or public surface to assign a shared <c>GroupId</c> to the sparklines it
    /// creates; giving this planner's rows a real shared group requires adding that there first.
    /// </remarks>
    public static IReadOnlyList<AddSparklineCommand> BuildCommands(
        SheetId sheetId,
        GridRange range,
        bool hasHeaderRow,
        SparklineKind kind)
    {
        if (range.ColCount < 2)
            return [];

        var targetCol = range.End.Col + 1;
        if (targetCol > CellAddress.MaxCol)
            return [];

        var firstDataRow = hasHeaderRow && range.RowCount > 1 ? range.Start.Row + 1 : range.Start.Row;
        if (firstDataRow > range.End.Row)
            return [];

        var commands = new List<AddSparklineCommand>((int)(range.End.Row - firstDataRow + 1));
        for (var row = firstDataRow; row <= range.End.Row; row++)
        {
            var dataRange = new GridRange(
                new CellAddress(sheetId, row, range.Start.Col),
                new CellAddress(sheetId, row, range.End.Col));
            var location = new CellAddress(sheetId, row, targetCol);
            commands.Add(new AddSparklineCommand(sheetId, dataRange, location, kind));
        }

        return commands;
    }
}
