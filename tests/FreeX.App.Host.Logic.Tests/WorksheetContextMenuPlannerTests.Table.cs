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

    [Fact]
    public void TableCell_OffersTableScopedInsertAndDeleteEntries_AndOtherCellsDoNot()
    {
        var inTable = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, new WorksheetContextMenuState { HasTableTarget = true });
        var insertDelete = Assert.Single(inTable, c => c.Header == "Insert and Delete");
        foreach (var action in new[]
                 {
                     WorksheetContextMenuAction.InsertTableRowAbove, WorksheetContextMenuAction.InsertTableRowBelow,
                     WorksheetContextMenuAction.InsertTableColumnLeft, WorksheetContextMenuAction.InsertTableColumnRight,
                     WorksheetContextMenuAction.DeleteTableRows, WorksheetContextMenuAction.DeleteTableColumns,
                 })
        {
            Assert.Contains(insertDelete.Children, c => c.Action == action && c.IsEnabled);
        }

        Assert.Contains(insertDelete.Children, c => c.Header == "Table Rows Above");
        Assert.Contains(insertDelete.Children, c => c.Header == "Table Columns to the Left");
        Assert.Contains(insertDelete.Children, c => c.Header == "Delete Table Rows");

        var outside = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet, WorksheetContextMenuState.Default);
        var plain = Assert.Single(outside, c => c.Header == "Insert and Delete");
        Assert.DoesNotContain(plain.Children, c => c.Header.StartsWith("Table ", StringComparison.Ordinal));
        Assert.DoesNotContain(plain.Children, c => c.Header == "Delete Table Rows");
    }

    [Fact]
    public void ProtectedSheet_DisablesTableScopedInsertAndDeleteEntries()
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(
            WorksheetContextMenuTargetKind.Worksheet,
            new WorksheetContextMenuState
            {
                HasTableTarget = true,
                Protection = new HashSet<SheetProtectionPermission>
                {
                    SheetProtectionPermission.InsertRows, SheetProtectionPermission.InsertColumns,
                    SheetProtectionPermission.DeleteRows, SheetProtectionPermission.DeleteColumns,
                },
            });
        var insertDelete = Assert.Single(commands, c => c.Header == "Insert and Delete");
        Assert.All(
            insertDelete.Children.Where(c => c.Header.Contains("Table ", StringComparison.Ordinal)),
            c => Assert.False(c.IsEnabled));
        Assert.Contains(insertDelete.Children, c => c.Action == WorksheetContextMenuAction.InsertRowAbove && c.IsEnabled);
    }

    [Theory]
    [InlineData(WorksheetContextMenuAction.InsertTableRowAbove)]
    [InlineData(WorksheetContextMenuAction.InsertTableRowBelow)]
    [InlineData(WorksheetContextMenuAction.InsertTableColumnLeft)]
    [InlineData(WorksheetContextMenuAction.InsertTableColumnRight)]
    [InlineData(WorksheetContextMenuAction.DeleteTableRows)]
    [InlineData(WorksheetContextMenuAction.DeleteTableColumns)]
    public void TableScopedActions_MapToTheSharedTablePlanner(WorksheetContextMenuAction action) =>
        Assert.True(WorksheetContextMenuTableActions.TryGetTableStructureAction(action, out _));

    [Fact]
    public void OrdinaryActions_AreNotTableScoped() =>
        Assert.False(WorksheetContextMenuTableActions.TryGetTableStructureAction(WorksheetContextMenuAction.InsertRowAbove, out _));
}
