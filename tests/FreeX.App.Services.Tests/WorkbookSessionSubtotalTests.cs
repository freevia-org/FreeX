using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionSubtotalTests
{
    [Fact]
    public void ExecuteSubtotalOptions_InsertsSubtotalsAndExpandsSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedSubtotalRows(sheet);
        var range = Range(sheet, 1, 1, 5, 2);
        var session = CreateSession(workbook);
        session.SelectRange(range);

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.AffectedCells.Should().Contain(Address(sheet, 4, 2));
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(Address(sheet, 4, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Address(sheet, 8, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B2:B7)");
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.SelectedRange.Should().Be(Range(sheet, 1, 1, 8, 2));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(25));
        session.CanRedo.Should().BeTrue();

    }

    [Fact]
    public void RemoveSelectedRangeSubtotals_RemovesSubtotalRowsAndUndoRestores()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedSubtotalRows(sheet);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet, 1, 1, 5, 2));
        session.ExecuteSubtotalOptions(CreateSumOptions()).Success.Should().BeTrue();
        session.SelectRange(Range(sheet, 1, 1, 8, 2));

        var result = session.RemoveSelectedRangeSubtotals();

        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        sheet.GetValue(4, 1).Should().Be(new TextValue("West"));
        sheet.GetValue(5, 2).Should().Be(new NumberValue(25));
        sheet.GetValue(6, 1).Should().BeOfType<BlankValue>();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(Address(sheet, 4, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");

    }

    [Fact]
    public void ExecuteSubtotalOptions_RepeatLastAppliesSameOptionsToNewActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        SeedSubtotalRows(summary);
        SeedSubtotalRows(details);
        var session = CreateSession(workbook);
        session.SelectRange(Range(summary, 1, 1, 5, 2));

        session.ExecuteSubtotalOptions(CreateSumOptions()).Success.Should().BeTrue();
        session.CanRepeatLastAction.Should().BeTrue();
        session.SelectSheet(details.Id).Should().BeTrue();
        session.SelectRange(Range(details, 1, 1, 5, 2));

        var repeat = session.RepeatLastAction();

        repeat.Success.Should().BeTrue();
        summary.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        details.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        details.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));
    }

    [Fact]
    public void RemoveSelectedRangeSubtotals_RepeatLastRemovesFromNewActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        SeedSubtotalRows(summary);
        SeedSubtotalRows(details);
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(Range(summary, 1, 1, 5, 2));
        session.ExecuteSubtotalOptions(CreateSumOptions()).Success.Should().BeTrue();
        session.SelectSheet(summary.Id).Should().BeTrue();
        session.SelectRange(Range(summary, 1, 1, 8, 2));

        session.RemoveSelectedRangeSubtotals().Success.Should().BeTrue();
        session.CanRepeatLastAction.Should().BeTrue();
        session.SelectSheet(details.Id).Should().BeTrue();
        session.SelectRange(Range(details, 1, 1, 8, 2));

        var repeat = session.RepeatLastAction();

        repeat.Success.Should().BeTrue();
        summary.GetValue(4, 1).Should().Be(new TextValue("West"));
        details.GetValue(4, 1).Should().Be(new TextValue("West"));
        details.GetValue(6, 1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void ExecuteSubtotalOptions_WithReplaceExistingReplacesCurrentSubtotalRows()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedSubtotalRows(sheet);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet, 1, 1, 5, 2));
        session.ExecuteSubtotalOptions(CreateSumOptions()).Success.Should().BeTrue();
        session.SelectRange(Range(sheet, 1, 1, 8, 2));

        var result = session.ExecuteSubtotalOptions(CreateSumOptions(replaceExisting: true));

        result.Success.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(Address(sheet, 9, 2)).Should().BeNull();
    }

    [Fact]
    public void ExecuteSubtotalOptions_PropagatesAcrossGroupedVisibleSheetsOnly()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        SeedSubtotalRows(summary);
        SeedSubtotalRows(details);
        SeedSubtotalRows(hidden);
        var range = Range(summary, 1, 1, 5, 2);
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(range);

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeTrue();
        summary.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        details.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        hidden.GetValue(4, 1).Should().Be(new TextValue("West"));
        session.IsWorkbookGrouped.Should().BeTrue();
        session.SelectedRange.Should().Be(Range(summary, 1, 1, 8, 2));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        summary.GetValue(4, 1).Should().Be(new TextValue("West"));
        details.GetValue(4, 1).Should().Be(new TextValue("West"));
        hidden.GetValue(4, 1).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void ExecuteSubtotalOptions_RejectsProtectedGroupedTargetsWithoutMutation()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        SeedSubtotalRows(summary);
        SeedSubtotalRows(details);
        details.IsProtected = true;
        var range = Range(summary, 1, 1, 5, 2);
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(range);

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("protected");
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        summary.GetValue(4, 1).Should().Be(new TextValue("West"));
        details.GetValue(4, 1).Should().Be(new TextValue("West"));
    }

    [Fact]
    public void ExecuteSubtotalOptions_TrimsWholeColumnSelectionBeforeApplying()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedSubtotalRows(sheet);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet, 1, 1, CellAddress.MaxRow, 2));

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeTrue();
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(Address(sheet, 4, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B2:B3)");
        sheet.GetValue(8, 1).Should().Be(new TextValue("Grand Total"));
        session.SelectedRange.Should().Be(Range(sheet, 1, 1, 8, 2));
    }

    [Fact]
    public void ExecuteSubtotalOptions_WithMergedLabelsInGroupColumn_ExpandsSelectionToVisibleSubtotalResult()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SetText(sheet, 1, 1, "Project");
        SetText(sheet, 1, 2, "Hours");
        SetText(sheet, 2, 1, "Boohoo");
        sheet.AddMergedRegion(Range(sheet, 2, 1, 5, 1));
        SetText(sheet, 6, 1, "Optimize");
        sheet.AddMergedRegion(Range(sheet, 6, 1, 8, 1));
        for (uint row = 2; row <= 8; row++)
            SetNumber(sheet, row, 2, row);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet, 1, 1, 8, 2));

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeTrue();
        sheet.GetValue(6, 1).Should().Be(new TextValue("Boohoo Total"));
        sheet.GetCell(Address(sheet, 6, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B2:B5)");
        sheet.GetValue(10, 1).Should().Be(new TextValue("Optimize Total"));
        sheet.GetCell(Address(sheet, 10, 2))!.FormulaText.Should().Be("SUBTOTAL(9,B7:B9)");
        sheet.GetValue(11, 1).Should().Be(new TextValue("Grand Total"));
        session.SelectedRange.Should().Be(Range(sheet, 1, 1, 11, 2));
    }

    [Fact]
    public void ExecuteSubtotalOptions_ReturnsBoundedValidationForEmptyWholeSheetSelection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheet, 1, 1, CellAddress.MaxRow, CellAddress.MaxCol));

        var result = session.ExecuteSubtotalOptions(CreateSumOptions());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(SubtotalPlanner.NoOccupiedDataMessage);
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static SubtotalInputOptions CreateSumOptions(bool replaceExisting = false) =>
        new(
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [1],
            FunctionNumber: 9,
            ReplaceExisting: replaceExisting,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true);

    private static void SeedSubtotalRows(Sheet sheet)
    {
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 1, 2, "Sales");
        SetText(sheet, 2, 1, "East");
        SetNumber(sheet, 2, 2, 10);
        SetText(sheet, 3, 1, "East");
        SetNumber(sheet, 3, 2, 15);
        SetText(sheet, 4, 1, "West");
        SetNumber(sheet, 4, 2, 20);
        SetText(sheet, 5, 1, "West");
        SetNumber(sheet, 5, 2, 25);
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(Address(sheet, startRow, startColumn), Address(sheet, endRow, endColumn));

    private static void SetText(Sheet sheet, uint row, uint column, string value) =>
        sheet.SetCell(Address(sheet, row, column), new TextValue(value));

    private static void SetNumber(Sheet sheet, uint row, uint column, double value) =>
        sheet.SetCell(Address(sheet, row, column), new NumberValue(value));

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
