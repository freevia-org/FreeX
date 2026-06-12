using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    private static PivotTableModel CreatePivot(string name = "Pivot", uint targetRow = 5, SheetId? sheetId = null)
    {
        sheetId ??= SheetId.New();
        return new PivotTableModel
        {
            Name = name,
            SourceRange = new GridRange(new CellAddress(sheetId.Value, 1, 1), new CellAddress(sheetId.Value, 4, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId.Value, targetRow, 1), new CellAddress(sheetId.Value, targetRow + 4, 4))
        };
    }
}
