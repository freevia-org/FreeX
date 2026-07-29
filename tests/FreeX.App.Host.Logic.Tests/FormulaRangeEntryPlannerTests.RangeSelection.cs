using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class FormulaRangeEntryPlannerTests
{
    [Fact]
    public void TryApplyRangeSelection_InsertsFirstRangeAtCaret()
    {
        var selected = Range("B2", "B8");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(B2:B8");
        edit.TextEdit.SelectionStart.Should().Be(10);
        edit.TextEdit.SelectionLength.Should().Be(0);
        edit.ReferenceStart.Should().Be(5);
        edit.ReferenceLength.Should().Be(5);
    }

    [Fact]
    public void TryApplyRangeSelection_ReplacesPreviousLiveReference()
    {
        var selected = Range("C3", "D4");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(B2:B8",
                caretIndex: 10,
                selectionLength: 0,
                previousReferenceStart: 5,
                previousReferenceLength: 5,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(C3:D4");
        edit.TextEdit.SelectionStart.Should().Be(10);
        edit.ReferenceStart.Should().Be(5);
        edit.ReferenceLength.Should().Be(5);
    }

    [Fact]
    public void TryApplyRangeSelection_ExtendsSingleCellToRange()
    {
        var selected = Range("A1", "B3");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(A1",
                caretIndex: 7,
                selectionLength: 0,
                previousReferenceStart: 5,
                previousReferenceLength: 2,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(A1:B3");
        edit.ReferenceStart.Should().Be(5);
        edit.ReferenceLength.Should().Be(5);
    }

    [Fact]
    public void TryApplyRangeSelection_FormatsRangeAsR1C1WhenEnabled()
    {
        var selected = Range("C8", "D9");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: true,
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(R8C3:R9C4");
        edit.ReferenceStart.Should().Be(5);
        edit.ReferenceLength.Should().Be(9);
    }

    [Fact]
    public void TryApplyRangeSelection_QualifiesCrossSheetReferenceAndQuotesSheetName()
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, 2, 2),
            new CellAddress(targetSheetId, 4, 3));

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit,
                selectedSheetName: "Revenue Data")
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM('Revenue Data'!B2:C4");
        edit.ReferenceStart.Should().Be(5);
        edit.ReferenceLength.Should().Be(20);
    }

    [Fact]
    public void TryApplyRangeSelection_FormatsThreeDSheetSpan()
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, 2, 2),
            new CellAddress(targetSheetId, 4, 3));
        var span = FormulaSheetSpanEntryPlanner.PlanTabSelection(
            FormulaSheetSpanEntryState.Empty,
            "Sheet1",
            "Sheet2",
            shiftHeld: false);
        span = FormulaSheetSpanEntryPlanner.PlanTabSelection(span, "Sheet2", "Sheet3", shiftHeld: true);

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit,
                selectedSheetName: "Sheet3",
                sheetSpan: span)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(Sheet2:Sheet3!B2:C4");
    }

    [Fact]
    public void TryApplyRangeSelection_FormatsThreeDSheetSpanAsR1C1()
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, 2, 2),
            new CellAddress(targetSheetId, 4, 3));
        var span = new FormulaSheetSpanEntryState("Sheet2", "Sheet3");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: true,
                out var edit,
                selectedSheetName: "Sheet3",
                sheetSpan: span)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(Sheet2:Sheet3!R2C2:R4C3");
    }

    [Fact]
    public void GeneratedThreeDSheetSpan_CyclesWithF4WithoutDroppingQualifier()
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, 2, 2),
            new CellAddress(targetSheetId, 4, 3));
        var span = new FormulaSheetSpanEntryState("Sheet2", "Sheet3");

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var generated,
                selectedSheetName: "Sheet3",
                sheetSpan: span)
            .Should()
            .BeTrue();

        ExcelTextEditorPlanner.TryCycleFormulaReference(
                generated.TextEdit.Text,
                generated.TextEdit.SelectionStart,
                out var cycled)
            .Should()
            .BeTrue();

        cycled.Text.Should().Be("=SUM(Sheet2:Sheet3!$B$2:$C$4");
    }

    [Theory]
    [InlineData(5u, 1u, 5u, CellAddress.MaxCol, "=SUM(Sheet2:Sheet3!5:5")]
    [InlineData(1u, 3u, CellAddress.MaxRow, 3u, "=SUM(Sheet2:Sheet3!C:C")]
    public void TryApplyRangeSelection_FormatsThreeDSheetSpanWholeAreas(
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol,
        string expected)
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, startRow, startCol),
            new CellAddress(targetSheetId, endRow, endCol));

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(",
                caretIndex: 5,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit,
                selectedSheetName: "Sheet3",
                sheetSpan: new FormulaSheetSpanEntryState("Sheet2", "Sheet3"))
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be(expected);
    }

    [Theory]
    [InlineData("Sheet1", "Revenue Data", "'Sheet1:Revenue Data'")]
    [InlineData("O'Brien", "Summary", "'O''Brien:Summary'")]
    public void SheetSpanQualifier_QuotesTheWholeSpanWhenNeeded(
        string startSheet,
        string endSheet,
        string expected)
    {
        var state = new FormulaSheetSpanEntryState(startSheet, endSheet);

        FormulaSheetSpanEntryPlanner.FormatSheetQualifier(state).Should().Be(expected);
    }

    [Fact]
    public void TryAppendDisjointRangeSelection_QualifiesOnlyTheAppendedCrossSheetArea()
    {
        var targetSheetId = SheetId.New();
        var selected = new GridRange(
            new CellAddress(targetSheetId, 3, 2),
            new CellAddress(targetSheetId, 3, 2));

        FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                "=SUM('Revenue Data'!A2",
                previousReferenceStart: 5,
                previousReferenceLength: 17,
                selected,
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit,
                selectedSheetName: "Summary Data")
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM('Revenue Data'!A2,'Summary Data'!B3");
        edit.ReferenceStart.Should().Be(23);
        edit.ReferenceLength.Should().Be(17);
    }

    [Fact]
    public void TryApplyRangeSelection_InsertsAtCaretWhenCaretMovedPastPreviousReference()
    {
        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=SUM(B2:B8,",
                caretIndex: 11,
                selectionLength: 0,
                previousReferenceStart: 5,
                previousReferenceLength: 5,
                Range("C1", "C3"),
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(B2:B8,C1:C3");
        edit.ReferenceStart.Should().Be(11);
        edit.ReferenceLength.Should().Be(5);
    }

    [Fact]
    public void TryApplyRangeSelection_IgnoresNonFormulaText()
    {
        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "SUM(",
                caretIndex: 4,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                Range("A1", "A2"),
                FormulaCell,
                useR1C1ReferenceStyle: false,
                out _)
            .Should()
            .BeFalse();
    }
}
