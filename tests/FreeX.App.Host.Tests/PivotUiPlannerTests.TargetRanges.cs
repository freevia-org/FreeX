using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotUiPlannerTests
{
    [Fact]
    public void DefaultTargetRange_PlacesPivotTwoColumnsAfterSourceAndClampsToSheetEdges()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(new CellAddress(sheet.Id, 10, 16382), new CellAddress(sheet.Id, 20, 16384));

        var target = PivotUiPlanner.DefaultTargetRange(sheet, source);

        target.Start.Should().Be(new CellAddress(sheet.Id, 10, 16384));
        target.End.Should().Be(new CellAddress(sheet.Id, 23, 16384));
    }

    [Fact]
    public void GenerateUniquePivotTableName_SkipsExistingNamesCaseInsensitively()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.PivotTables.Add(CreatePivot("PivotTable1", sheetId: sheet.Id));
        sheet.PivotTables.Add(CreatePivot("pivottable2", sheetId: sheet.Id));

        PivotUiPlanner.GenerateUniquePivotTableName(sheet).Should().Be("PivotTable3");
    }

    [Fact]
    public void PivotTableNameAvailability_TrimsAndRejectsWorkbookDuplicates()
    {
        var workbook = new Workbook("PivotNamePlannerTest");
        var firstSheet = workbook.AddSheet("One");
        var secondSheet = workbook.AddSheet("Two");
        var pivot = CreatePivot("PivotTable1", sheetId: firstSheet.Id);
        firstSheet.PivotTables.Add(pivot);
        secondSheet.PivotTables.Add(CreatePivot("PivotTable2", sheetId: secondSheet.Id));

        PivotUiPlanner.NormalizePivotTableName("  Sales Pivot  ").Should().Be("Sales Pivot");
        PivotUiPlanner.IsPivotTableNameAvailable(workbook, pivot, "PivotTable2").Should().BeFalse();
        PivotUiPlanner.IsPivotTableNameAvailable(workbook, pivot, "  Sales Pivot  ").Should().BeTrue();
        PivotUiPlanner.IsPivotTableNameAvailable(workbook, pivot, " ").Should().BeFalse();
    }

    [Fact]
    public void PivotTableSelectionRange_UsesTargetRange()
    {
        var sheetId = SheetId.New();
        var pivot = CreatePivot("Pivot", 8, sheetId);

        PivotUiPlanner.ResolvePivotTableSelectionRange(pivot)
            .Should()
            .Be(new GridRange(new CellAddress(sheetId, 8, 1), new CellAddress(sheetId, 12, 4)));
    }

    [Fact]
    public void TryCreateMovedTargetRange_PreservesPivotFootprintAndRejectsWorksheetOverflow()
    {
        var sheetId = SheetId.New();
        var pivot = CreatePivot("Pivot", 5, sheetId);

        PivotUiPlanner.TryCreateMovedTargetRange(pivot, new CellAddress(sheetId, 20, 3), out var moved)
            .Should()
            .BeTrue();
        moved.Should().Be(new GridRange(new CellAddress(sheetId, 20, 3), new CellAddress(sheetId, 24, 6)));

        PivotUiPlanner.TryCreateMovedTargetRange(pivot, new CellAddress(sheetId, CellAddress.MaxRow, 3), out _)
            .Should()
            .BeFalse();
    }
}
