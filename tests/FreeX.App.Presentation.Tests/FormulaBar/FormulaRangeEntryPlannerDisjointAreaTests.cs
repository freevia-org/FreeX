using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaRangeEntryPlannerDisjointAreaTests
{
    [Theory]
    [InlineData(FormulaEditorKey.F8, FormulaEditorModifiers.None, ExcelSelectionMode.Normal, ExcelSelectionMode.Extend)]
    [InlineData(FormulaEditorKey.F8, FormulaEditorModifiers.Shift, ExcelSelectionMode.Normal, ExcelSelectionMode.Add)]
    [InlineData(FormulaEditorKey.F8, FormulaEditorModifiers.Shift, ExcelSelectionMode.Add, ExcelSelectionMode.Normal)]
    public void KeyboardSelectionMode_UsesExcelF8AndShiftF8Semantics(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        ExcelSelectionMode current,
        ExcelSelectionMode expected)
    {
        FormulaRangeEntryPlanner.TryToggleKeyboardSelectionMode(key, modifiers, current, out var next)
            .Should().BeTrue();

        next.Should().Be(expected);
    }

    [Fact]
    public void AppendKeyboardRangeSelection_AddsSingleCellThenKeepsReferenceSpanScoped()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var formulaCell = new CellAddress(sheet, 10, 5);

        FormulaRangeEntryPlanner.TryAppendKeyboardRangeSelection(
                "=SUM(A1",
                previousReferenceStart: 5,
                previousReferenceLength: 2,
                current: new CellAddress(sheet, 1, 1),
                target: new CellAddress(sheet, 1, 3),
                extendSelection: false,
                formulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should().BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(A1,C1");
        edit.ReferenceStart.Should().Be(8);
        edit.ReferenceLength.Should().Be(2);
    }

    [Fact]
    public void AppendKeyboardRangeSelection_ShiftExtendsOnlyTheNewAreaAndPreservesR1C1()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var formulaCell = new CellAddress(sheet, 10, 5);

        FormulaRangeEntryPlanner.TryAppendKeyboardRangeSelection(
                "=SUM(R1C1",
                previousReferenceStart: 5,
                previousReferenceLength: 4,
                current: new CellAddress(sheet, 1, 1),
                target: new CellAddress(sheet, 3, 2),
                extendSelection: true,
                formulaCell,
                useR1C1ReferenceStyle: true,
                out var edit)
            .Should().BeTrue();

        edit.TextEdit.Text.Should().Be("=SUM(R1C1,R1C1:R3C2");
        edit.ReferenceStart.Should().Be(10);
        edit.ReferenceLength.Should().Be(9);
    }

    [Fact]
    public void AppendWholeColumnArea_UsesExcelShorthandAndTracksOnlyNewArea()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var formulaCell = new CellAddress(sheet, 1, 1);
        var selectedRange = new GridRange(
            new CellAddress(sheet, 1, 2),
            new CellAddress(sheet, CellAddress.MaxRow, 2));

        FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                "=A1",
                previousReferenceStart: 1,
                previousReferenceLength: 2,
                selectedRange,
                formulaCell,
                useR1C1ReferenceStyle: false,
                out var edit)
            .Should().BeTrue();

        edit.TextEdit.Text.Should().Be("=A1,B:B");
        edit.ReferenceStart.Should().Be(4);
        edit.ReferenceLength.Should().Be(3);
        edit.TextEdit.SelectionStart.Should().Be("=A1,B:B".Length);
    }

    [Fact]
    public void AppendWholeRowOnAnotherSheet_QualifiesShorthandReference()
    {
        var sourceSheet = new SheetId(Guid.NewGuid());
        var targetSheet = new SheetId(Guid.NewGuid());
        var selectedRange = new GridRange(
            new CellAddress(targetSheet, 3, 1),
            new CellAddress(targetSheet, 3, CellAddress.MaxCol));

        FormulaRangeEntryPlanner.TryAppendDisjointRangeSelection(
                "=A1",
                previousReferenceStart: 1,
                previousReferenceLength: 2,
                selectedRange,
                new CellAddress(sourceSheet, 1, 1),
                useR1C1ReferenceStyle: false,
                out var edit,
                selectedSheetName: "Revenue Data")
            .Should().BeTrue();

        edit.TextEdit.Text.Should().Be("=A1,'Revenue Data'!3:3");
        edit.ReferenceStart.Should().Be(4);
        edit.ReferenceLength.Should().Be("'Revenue Data'!3:3".Length);
    }

    [Fact]
    public void WholeRowAndColumnShorthandIsNotUsedForR1C1()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var selectedRange = new GridRange(
            new CellAddress(sheet, 1, 2),
            new CellAddress(sheet, CellAddress.MaxRow, 2));

        FormulaRangeEntryPlanner.TryApplyRangeSelection(
                "=",
                caretIndex: 1,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                selectedRange,
                new CellAddress(sheet, 1, 1),
                useR1C1ReferenceStyle: true,
                out var edit)
            .Should().BeTrue();

        edit.TextEdit.Text.Should().Be("=R1C2:R1048576C2");
    }
}
