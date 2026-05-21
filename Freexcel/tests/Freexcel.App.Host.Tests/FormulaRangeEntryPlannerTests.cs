using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaRangeEntryPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly CellAddress FormulaCell = new(SheetId, 10, 5);

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
    public void GetKeyboardCursor_UsesMovingSelectionCursorWhenFormulaRangeIsAlreadyExtended()
    {
        var cursor = CellAddress.Parse("B1", SheetId);

        FormulaRangeEntryPlanner.GetKeyboardCursor(Range("A1", "B1"), cursor)
            .Should()
            .Be(cursor);
    }

    [Fact]
    public void GetKeyboardCursor_FallsBackToRangeStartWhenNoSelectionCursorExists()
    {
        FormulaRangeEntryPlanner.GetKeyboardCursor(Range("A1", "B1"), selectionCursor: null)
            .Should()
            .Be(CellAddress.Parse("A1", SheetId));
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

    private static GridRange Range(string start, string end) =>
        new(CellAddress.Parse(start, SheetId), CellAddress.Parse(end, SheetId));
}
