using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R69-formula-lookup-array-6-1: INDEX's direct-range fast path (TryEvaluateIndexDirectRange) must
// defer to the generic slow path when row_num or column_num itself EVALUATES to an array
// (RangeValue) -- e.g. INDEX(range, MATCH({"r3","r1"}, lookup, 0)) -- so the array can be broadcast
// across the table and spill, matching Excel. Previously the fast path coerced the array via
// CoerceToNumber, which returned a bare #VALUE! for the whole result instead of spilling.
public sealed class R69_IndexArrayRowColNumSpillsTests
{
    private readonly FormulaEvaluator _eval = new();

    // A single-ROW lookup layout (names in row 1, values in row 2) is used for the row_num-array
    // cases below so the generic Index() single-index-argument special case exercised is the
    // already-correct "table.RowCount == 1" branch (BuiltInFunctions.Lookup.Legacy.cs's
    // IndexScalar sets colNum = rowNum; rowNum = 1 there) rather than its sibling
    // "table.ColCount == 1" branch, which has its own separate, pre-existing bug unrelated to this
    // fix (see report).
    private static Sheet MakeRowLookupSheet()
    {
        // A1:C1 = "r1","r2","r3"; A2:C2 = 10,20,30
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("r1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("r2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("r3"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(30));
        return sheet;
    }

    [Fact]
    public void Index_RowNumFromMatchOverArrayLookupValue_Spills()
    {
        var sheet = MakeRowLookupSheet();

        var result = _eval.Evaluate("=INDEX(A2:C2,MATCH({\"r3\",\"r1\"},A1:C1,0))", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new NumberValue(30));
        result.At(1, 2).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Index_RowNumLiteralArray_Spills()
    {
        var sheet = MakeRowLookupSheet();

        var result = _eval.Evaluate("=INDEX(A2:C2,{3,1})", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new NumberValue(30));
        result.At(1, 2).Should().Be(new NumberValue(10));
    }

    private static Sheet MakeColumnLookupSheet()
    {
        // B1:B3 = 10,20,30
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        return sheet;
    }

    [Fact]
    public void Index_ColumnNumLiteralArray_Spills()
    {
        var sheet = MakeColumnLookupSheet();

        var result = _eval.Evaluate("=INDEX(B1:B3,1,{1;1})", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(10));
        result.At(2, 1).Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Index_ScalarRowNum_StillReturnsSingleCell_NoRegression()
    {
        // Sibling/no-regression: a plain scalar row_num must still use the fast path and return
        // a single scalar cell, not spill.
        var sheet = MakeColumnLookupSheet();

        _eval.Evaluate("=INDEX(B1:B3,2)", sheet).Should().Be(new NumberValue(20));
    }
}
