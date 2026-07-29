using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaRangeEntryPlannerDisjointAreaTests
{
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
