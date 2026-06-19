using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableNamePlannerTests
{
    private static (Workbook Workbook, Sheet Sheet, StructuredTableModel Table) BuildWorkbookWithTable(
        string name = "Table1")
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = name,
            DisplayName = name,
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
        };
        sheet.StructuredTables.Add(table);
        return (workbook, sheet, table);
    }

    [Fact]
    public void Capture_PrefersDisplayNameThenFallsBackToName()
    {
        var withDisplay = new StructuredTableModel { Id = 1, Name = "Internal", DisplayName = "Pretty" };
        TableNamePlanner.Capture(withDisplay).Should().Be("Pretty");

        var noDisplay = new StructuredTableModel { Id = 1, Name = "Internal", DisplayName = "" };
        TableNamePlanner.Capture(noDisplay).Should().Be("Internal");
    }

    [Fact]
    public void TryCreateRename_RejectsBlankName()
    {
        var (workbook, sheet, table) = BuildWorkbookWithTable();
        var ok = TableNamePlanner.TryCreateRename(workbook, sheet.Id, table.Id, "   ", out var values, out var error);
        ok.Should().BeFalse();
        values.Should().BeNull();
        error.Should().Be(TableNamePlanner.EmptyNameMessage);
    }

    [Fact]
    public void TryCreateRename_RejectsCellReferenceLikeName()
    {
        var (workbook, sheet, table) = BuildWorkbookWithTable();
        var ok = TableNamePlanner.TryCreateRename(workbook, sheet.Id, table.Id, "A1", out _, out var error);
        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreateRename_RejectsNameUsedByAnotherTable()
    {
        var (workbook, sheet, table) = BuildWorkbookWithTable();
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 2,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 14, 3)),
        });

        var ok = TableNamePlanner.TryCreateRename(workbook, sheet.Id, table.Id, "Sales", out _, out var error);
        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryCreateRename_AllowsRenamingToOwnNameAndTrims()
    {
        var (workbook, sheet, table) = BuildWorkbookWithTable("Table1");
        var ok = TableNamePlanner.TryCreateRename(workbook, sheet.Id, table.Id, "  Revenue_2026  ", out var values, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        values!.Name.Should().Be("Revenue_2026");
    }
}
