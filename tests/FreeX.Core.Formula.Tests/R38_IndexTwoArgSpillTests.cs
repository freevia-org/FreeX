using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R38-formula-lookup-modern-2-1: INDEX(array, row_num) with column_num omitted, over a genuine
// 2-D array (more than one row AND more than one column), must SPILL the whole selected row as a
// 1xN array in modern (dynamic-array) Excel -- not collapse to the single cell in column 1.
// Symmetric behavior for INDEX(array,,col_num) (row omitted) already spills the whole column via
// the existing rowNum==0 branch; this only fixes the column-omitted (single positional argument)
// case, which previously defaulted colNum to 1 instead of treating it as omitted for 2-D arrays.
public sealed class R38_IndexTwoArgSpillTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeDataSheet()
    {
        // A1:C3 = 1..9 row-major: {1,2,3; 4,5,6; 7,8,9}
        var sheet = new Sheet(SheetId.New(), "S");
        int n = 1;
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(n++));
        return sheet;
    }

    [Fact]
    public void Index_TwoArgForm_OverTwoDimensionalArray_SpillsWholeRow()
    {
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=INDEX(A1:C3,2)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new NumberValue(4));
        result.At(1, 2).Should().Be(new NumberValue(5));
        result.At(1, 3).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_RowOmitted_OverTwoDimensionalArray_SpillsWholeColumn()
    {
        // Sibling: row_num omitted (INDEX(array,,col)) already spilled the whole column before
        // this fix -- confirm it still does so unchanged.
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=INDEX(A1:C3,,2)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(2));
        result.At(2, 1).Should().Be(new NumberValue(5));
        result.At(3, 1).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Index_ThreeArgForm_WithBothRowAndColumn_StillReturnsSingleCell()
    {
        // Sibling no-regression: when both row_num and column_num are given explicitly, INDEX
        // must still return a single scalar cell, not spill.
        var sheet = MakeDataSheet();

        _eval.Evaluate("=INDEX(A1:C3,2,3)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_TwoArgForm_OverSingleRowArray_StillSelectsColumnAsBefore()
    {
        // Sibling no-regression: the pre-existing 1-D single-row special case (index selects
        // along the only dimension: column) must be unaffected.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        _eval.Evaluate("=INDEX(A1:C1,2)", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Index_TwoArgForm_OverSingleColumnArray_StillSelectsRowAsBefore()
    {
        // Sibling no-regression: the pre-existing 1-D single-column special case (index selects
        // along the only dimension: row) must be unaffected.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        _eval.Evaluate("=INDEX(A1:A3,2)", sheet).Should().Be(new NumberValue(20));
    }
}
