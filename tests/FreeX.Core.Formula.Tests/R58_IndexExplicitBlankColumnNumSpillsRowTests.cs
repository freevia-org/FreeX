using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R58-formula-index-match-combo-6-1: INDEX(range, row_num,) with an EXPLICITLY-blank column_num
// (trailing comma, or a genuine blank-cell reference) must coerce to 0 and spill the whole
// selected row as a 1xN array -- the exact symmetric counterpart of INDEX(range,,col_num), which
// already spills the whole column. Previously a blank column_num was special-cased to 1.0 instead
// of going through the same plain ToNumber(BlankValue)==0.0 coercion row_num already uses,
// wrongly collapsing to a single cell in column 1.
public sealed class R58_IndexExplicitBlankColumnNumSpillsRowTests
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
    public void Index_ThreeArgForm_TrailingCommaBlankColumn_SpillsWholeRow()
    {
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=INDEX(A1:C3,2,)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new NumberValue(4));
        result.At(1, 2).Should().Be(new NumberValue(5));
        result.At(1, 3).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_ThreeArgForm_BlankCellRefColumn_SpillsWholeRow()
    {
        // A genuine blank-cell reference used as column_num must behave identically to the
        // trailing-comma omitted form (D1 is empty).
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=INDEX(A1:C3,2,D1)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new NumberValue(4));
        result.At(1, 2).Should().Be(new NumberValue(5));
        result.At(1, 3).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_ThreeArgForm_ExplicitZeroColumn_StillSpillsWholeRow_NoRegression()
    {
        // Sibling/no-regression: the already-correct explicit-literal-0 column_num form must
        // continue to spill the whole row identically.
        var sheet = MakeDataSheet();

        var result = _eval.Evaluate("=INDEX(A1:C3,2,0)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new NumberValue(4));
        result.At(1, 2).Should().Be(new NumberValue(5));
        result.At(1, 3).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Index_ThreeArgForm_BlankRowNum_StillSpillsWholeColumn_NoRegression()
    {
        // Sibling/no-regression: the symmetric blank ROW_num form (INDEX(range,,col)) must remain
        // unaffected by this column_num-focused fix.
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
    public void Index_ThreeArgForm_BothExplicit_StillReturnsSingleCell_NoRegression()
    {
        // Sibling/no-regression: when both row_num and column_num are given explicitly (neither
        // blank), INDEX must still return a single scalar cell.
        var sheet = MakeDataSheet();

        _eval.Evaluate("=INDEX(A1:C3,2,3)", sheet).Should().Be(new NumberValue(6));
    }
}
