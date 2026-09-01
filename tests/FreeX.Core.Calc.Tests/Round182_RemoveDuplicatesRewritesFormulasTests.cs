using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round 182. Remove Duplicates compacts the surviving rows upward, which MOVES them -- and Excel
/// rewrites a moved cell's relative references by the distance it travelled. SortCommand already
/// does this for the permutation it performs (its N37 comment spells out the rule); the dedup
/// compaction cloned every surviving cell verbatim instead, so a formula in a row that moved up
/// still pointed at its old neighbour's row. That silently makes surviving rows read other rows'
/// data, or rows the operation just deleted.
/// </summary>
public sealed class Round182_RemoveDuplicatesRewritesFormulasTests
{
    [Fact]
    public void ARelativeFormulaInARowThatCompactsUpward_IsRewrittenByTheDistanceItMoved()
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();

        // Column A is the dedup key: rows 1 and 2 are duplicates, so row 3 survives and compacts
        // up into row 2. Column B carries a formula pointing at its own row in column C.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("C3"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be(
            "C2",
            "the row moved up one, so its relative reference must move with it -- leaving =C3 makes " +
            "the surviving row read a row that no longer holds what it used to");
    }

    [Fact]
    public void AnAbsoluteReferenceIsNotRewritten()
    {
        // Sibling no-regression: $ references are anchored and must survive the move unchanged,
        // matching the rule SortCommand applies.
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromFormula("$C$3"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("$C$3");
    }

    [Fact]
    public void ARowThatDidNotMoveKeepsItsFormulaUnchanged()
    {
        // Sibling no-regression: the first surviving row stays where it is, so nothing to rewrite.
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromFormula("C1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("dup"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        new RemoveDuplicateRowsCommand(sheet.Id, range).Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.FormulaText.Should().Be("C1");
    }
}
