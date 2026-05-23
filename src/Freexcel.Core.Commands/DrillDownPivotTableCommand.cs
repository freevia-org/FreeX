using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class DrillDownPivotTableCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _pivotTableName;
    private readonly CellAddress _pivotCell;
    private SheetId? _detailSheetId;

    public DrillDownPivotTableCommand(SheetId sheetId, string pivotTableName, CellAddress pivotCell)
    {
        _sheetId = sheetId;
        _pivotTableName = pivotTableName;
        _pivotCell = pivotCell;
    }

    public string Label => "Show PivotTable Details";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, _pivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return new CommandOutcome(false, "PivotTable was not found.");

        var details = PivotTableRefreshService.ExtractDetailRows(ctx.Workbook, sheet, pivotTable, _pivotCell);
        if (details.Headers.Count == 0 || details.Rows.Count == 0)
            return new CommandOutcome(false, "No detail rows were found for this PivotTable cell.");

        var detailSheet = ctx.Workbook.AddSheet(GenerateDetailSheetName(ctx.Workbook));
        _detailSheetId = detailSheet.Id;
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, 1, (uint)col + 1), new TextValue(details.Headers[col]));
        for (var row = 0; row < details.Rows.Count; row++)
        for (var col = 0; col < details.Headers.Count; col++)
            detailSheet.SetCell(new CellAddress(detailSheet.Id, (uint)row + 2, (uint)col + 1), Cell.FromValue(details.Rows[row][col]));

        return new CommandOutcome(true, AffectedCells: [new CellAddress(detailSheet.Id, 1, 1)]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_detailSheetId is { } detailSheetId)
            ctx.Workbook.RemoveSheet(detailSheetId);
        _detailSheetId = null;
    }

    private static string GenerateDetailSheetName(Workbook workbook)
    {
        for (var index = 1; index <= 10000; index++)
        {
            var name = index == 1 ? "Detail" : $"Detail{index}";
            if (workbook.ValidateSheetName(name) is null)
                return name;
        }

        return $"Detail{Guid.NewGuid():N}"[..31];
    }
}
