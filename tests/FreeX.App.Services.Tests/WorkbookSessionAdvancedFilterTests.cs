using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionAdvancedFilterTests
{
    [Fact]
    public void ExecuteAdvancedFilterPlan_CopyToAnotherLocation_CopiesRowsSelectsCopyRangeAndSupportsUndoRedo()
    {
        var (session, sheet, plan) = CreateCopyToSession();
        var copyToRange = plan.CopyToRange!.Value;

        var result = session.ExecuteAdvancedFilterPlan(plan);

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().Equal(copyToRange.Start);
        AssertCopiedEastRows(sheet);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(copyToRange.Start);
        session.SelectedRange.Should().Be(copyToRange);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetCell(Address(sheet, 1, 8)).Should().BeNull();
        sheet.GetCell(Address(sheet, 3, 10)).Should().BeNull();
        session.CanRedo.Should().BeTrue();

        var redo = session.RedoLastEdit();

        redo.Success.Should().BeTrue();
        AssertCopiedEastRows(sheet);
    }

    [Fact]
    public void ExecuteAdvancedFilterPlan_FilterInPlace_HidesNonMatchingRowsAndSelectsListRange()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        var session = CreateSession(workbook);
        session.SelectCell(Address(sheet, 6, 6));
        var plan = AdvancedFilterPlanner.CreatePlan(
            sheet.Id,
            listRangeText: "A1:C5",
            criteriaRangeText: "F1:F2",
            copyToRangeText: "",
            AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly: false).Plan!;

        var result = session.ExecuteAdvancedFilterPlan(plan);

        result.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(plan.ListRange.Start);
        session.SelectedRange.Should().Be(plan.ListRange);
    }

    [Fact]
    public void ExecuteAdvancedFilterPlan_FailedCommandDoesNotDirtyOrMoveSelection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        SeedList(sheet);
        var selected = Address(sheet, 6, 6);
        var missingSheetId = SheetId.New();
        var session = CreateSession(workbook);
        session.SelectCell(selected);
        var plan = new AdvancedFilterPlan(
            Range(sheet.Id, 1, 1, 5, 3),
            new GridRange(
                new CellAddress(missingSheetId, 1, 1),
                new CellAddress(missingSheetId, 2, 1)),
            AdvancedFilterOutputMode.FilterInPlace,
            UniqueRecordsOnly: false);

        var result = session.ExecuteAdvancedFilterPlan(plan);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("criteria range");
        sheet.FilterHiddenRows.Should().BeEmpty();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        session.ActiveCell.Should().Be(selected);
        session.SelectedRange.Should().Be(new GridRange(selected, selected));
    }

    private static (WorkbookSession Session, Sheet Sheet, AdvancedFilterPlan Plan) CreateCopyToSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        SeedList(sheet);
        Set(sheet, 1, 6, "Region");
        Set(sheet, 2, 6, "East");
        var plan = AdvancedFilterPlanner.CreatePlan(
            sheet.Id,
            listRangeText: "A1:C5",
            criteriaRangeText: "F1:F2",
            copyToRangeText: "H1:J1",
            AdvancedFilterOutputMode.CopyToAnotherLocation,
            uniqueRecordsOnly: false).Plan!;

        return (CreateSession(workbook), sheet, plan);
    }

    private static void AssertCopiedEastRows(Sheet sheet)
    {
        sheet.GetValue(1, 8).Should().Be(new TextValue("Region"));
        sheet.GetValue(1, 9).Should().Be(new TextValue("Sales"));
        sheet.GetValue(1, 10).Should().Be(new TextValue("Rep"));
        sheet.GetValue(2, 8).Should().Be(new TextValue("East"));
        sheet.GetValue(2, 9).Should().Be(new NumberValue(90));
        sheet.GetValue(2, 10).Should().Be(new TextValue("Ana"));
        sheet.GetValue(3, 8).Should().Be(new TextValue("East"));
        sheet.GetValue(3, 9).Should().Be(new NumberValue(120));
        sheet.GetValue(3, 10).Should().Be(new TextValue("Ana"));
    }

    private static void SeedList(Sheet sheet)
    {
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Sales");
        Set(sheet, 1, 3, "Rep");
        Set(sheet, 2, 1, "East");
        Set(sheet, 2, 2, 90);
        Set(sheet, 2, 3, "Ana");
        Set(sheet, 3, 1, "West");
        Set(sheet, 3, 2, 130);
        Set(sheet, 3, 3, "Ben");
        Set(sheet, 4, 1, "East");
        Set(sheet, 4, 2, 120);
        Set(sheet, 4, 3, "Ana");
        Set(sheet, 5, 1, "West");
        Set(sheet, 5, 2, 80);
        Set(sheet, 5, 3, "Cy");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(Address(sheet, row, col), new TextValue(text));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(Address(sheet, row, col), new NumberValue(number));
}
