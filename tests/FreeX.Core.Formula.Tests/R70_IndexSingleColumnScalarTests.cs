using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

// R70-formula-lookup-index-singlecolumn-scalar: IndexScalar's singleIndexArgument special case
// (BuiltInFunctions.Lookup.Legacy.cs) left colNum at its blank-coerced 0 for a single-COLUMN table
// (table.ColCount == 1) -- the branch only commented the intent ("rowNum already correct, colNum
// = 1") but never actually assigned colNum. That made the colNum==0 "return the whole row" branch
// wrongly fire, wrapping the single selected value in a positioned 1x1 RangeValue instead of
// returning a bare scalar. The sibling table.RowCount == 1 branch already correctly reassigns its
// own indices, and TryEvaluateIndexDirectRange (FormulaEvaluator.References.cs) -- the fast-path
// twin of this same special case -- already sets columnIndex = 1 for ColCount==1, so the bug is
// only observable when INDEX's slow generic path (IndexScalar) is reached instead of the fast
// path: either because the table argument isn't a directly-recognized range reference (defeats
// TryAsRangeRef), or because row_num itself evaluates to an array (MATCH with an array
// lookup_value forces the fast path to defer, per R69_IndexArrayRowColNumSpillsTests.cs).
public sealed class R70_IndexSingleColumnScalarTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeColumnLookupSheet()
    {
        // A1:A3 = "r1","r2","r3" (lookup names); B1:B3 = 10,20,30 (single-column return table)
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("r1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("r2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("r3"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        return sheet;
    }

    [Fact]
    public void Index_TwoArgForm_SingleColumnTable_ViaGenericPath_ReturnsBareScalar()
    {
        // The table argument is wrapped in IF(TRUE, ...) so its AST node is a FunctionCallNode
        // rather than a plain RangeRefNode -- TryEvaluateIndexDirectRange's leading TryAsRangeRef
        // check bails immediately, forcing this 2-arg (plain scalar row_num) INDEX through the
        // generic Index()/IndexScalar slow path even though row_num itself is an ordinary scalar.
        // Before the fix this returned a positioned 1x1 RangeValue (Cells={{20}}); after, a bare
        // NumberValue.
        var sheet = MakeColumnLookupSheet();

        var result = _eval.Evaluate("=INDEX(IF(TRUE,B1:B3),2)", sheet);

        result.Should().BeOfType<NumberValue>();
        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Index_RowNumFromMatchOverArrayLookupValue_SingleColumnTable_SpillsBareScalars()
    {
        // MATCH's lookup_value is itself an array, so MATCH returns an array of row numbers
        // ({3,1}), forcing INDEX's fast path to defer to the generic path (the R69 fix) -- this
        // time over a single-COLUMN return table, which exercises the ColCount==1 branch this
        // backlog item fixes. Before the fix each broadcast element wrongly wrapped its value in
        // a positioned 1x1 RangeValue instead of spilling bare scalars.
        var sheet = MakeColumnLookupSheet();

        var result = _eval.Evaluate("=INDEX(B1:B3,MATCH({\"r3\",\"r1\"},A1:A3,0))", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().BeOfType<NumberValue>().Which.Should().Be(new NumberValue(30));
        result.Cells[0, 1].Should().BeOfType<NumberValue>().Which.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Index_TwoArgForm_SingleRowTable_StillSelectsColumn_NoRegression()
    {
        // Sibling no-regression: the RowCount==1 branch (which already correctly reassigns its
        // own indices) must be unaffected by this fix.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(30));

        _eval.Evaluate("=INDEX(A1:C1,2)", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Index_ThreeArgForm_TwoDimensionalTable_Unchanged_NoRegression()
    {
        // Sibling no-regression: an explicit row+col 2-D INDEX must still return a single scalar,
        // untouched by the singleIndexArgument special case entirely.
        var sheet = new Sheet(SheetId.New(), "S");
        int n = 1;
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(n++));

        _eval.Evaluate("=INDEX(A1:C3,2,3)", sheet).Should().Be(new NumberValue(6));
    }
}
