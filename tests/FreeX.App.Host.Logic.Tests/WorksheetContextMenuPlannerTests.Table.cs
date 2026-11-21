using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    [Fact]
    public void ResolveWorksheetState_FlagsCellsInsideAStructuredTable()
    {
        var workbook = new Workbook("t");
        var sheet = workbook.AddSheet("S");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        });

        Assert.True(WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, new CellAddress(sheet.Id, 2, 2)).HasTableTarget);
        Assert.False(WorksheetContextMenuPlanner.ResolveWorksheetState(sheet, new CellAddress(sheet.Id, 9, 9)).HasTableTarget);
    }

    [Fact]
    public void TableCell_OffersATableSubmenuWithConvertToRange_AndOtherCellsDoNot()
    {
        var inTable = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, new WorksheetContextMenuState { HasTableTarget = true });
        var table = Assert.Single(inTable, c => c.Header == "Table");
        Assert.Contains(table.Children, c => c.Action == WorksheetContextMenuAction.ConvertTableToRange && c.IsEnabled);

        var outside = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, WorksheetContextMenuState.Default);
        Assert.DoesNotContain(outside, c => c.Header == "Table");
    }

    [Fact]
    public void ProtectedSheet_DisablesConvertToRange()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(
            WorksheetContextMenuTargetKind.Worksheet,
            new WorksheetContextMenuState { HasTableTarget = true, Protection = new HashSet<SheetProtectionPermission>() });
        var table = Assert.Single(commands, c => c.Header == "Table");
        Assert.All(table.Children, c => Assert.False(c.IsEnabled));
        Assert.False(table.IsEnabled);
    }
}
