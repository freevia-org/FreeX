using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R87-formula-array-spill-atsign: the @ (implicit intersection) operator's AST-shape whitelist in
/// FormulaEvaluator.Operators.cs's ImplicitIntersectionOp(FormulaNode, ScalarValue, IEvalContext)
/// only recognized RangeRefNode/FullColumnRangeRefNode/FullRowRangeRefNode/StructuredReferenceNode/
/// StructuredCurrentRowReferenceNode/ANCHORARRAY (plus NamedRangeNode via its resolved
/// RangeValue.IsSheetReference flag) as "reference-like". OFFSET/INDEX/INDIRECT/CHOOSE all fell
/// through to the `_ => false` default and were treated as computed/dynamic-array results, so
/// =@OFFSET(...) etc. always returned the array's TOP-LEFT element regardless of the formula cell's
/// own row/column -- instead of positionally intersecting against it, as Excel does (and as the
/// sibling SINGLE() function already did, since its ImplicitIntersectionOp(ScalarValue, ...) overload
/// branches on the resolved RangeValue.IsSheetReference flag directly rather than AST shape). The fix
/// adds a FunctionCallNode { FunctionName: "OFFSET" or "INDEX" or "INDIRECT" or "CHOOSE" } arm that
/// also consults range.IsSheetReference, mirroring the NamedRangeNode arm already present.
/// </summary>
public sealed class R87_AtOperatorReferenceFunctionIntersectionTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void AtOperator_OnOffsetReference_PositionallyIntersectsByFormulaCellRow()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        // OFFSET($A$1,1,0,3,1) returns the genuine worksheet reference A2:A4. The formula cell
        // itself sits at row 3 (same row as A3), so @ must positionally intersect and return A3 =
        // 20 -- not the old buggy behaviour of always returning the top-left element (A2 = 10)
        // regardless of the formula cell's row.
        var result = _evaluator.Evaluate(
            "=@OFFSET($A$1,1,0,3,1)", sheet, currentCell: new CellAddress(sheet.Id, 3, 2));

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void AtOperator_OnIndexReference_PositionallyIntersectsByFormulaCellRow()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));

        // INDEX($A$1:$A$4,0) (row_num 0) returns the whole-column reference A1:A4. Sibling
        // reference-returning function to OFFSET above -- must also positionally intersect.
        var result = _evaluator.Evaluate(
            "=@INDEX($A$1:$A$4,0)", sheet, currentCell: new CellAddress(sheet.Id, 3, 2));

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void AtOperator_OnChooseSelectingReferenceBranch_PositionallyIntersects()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        // CHOOSE(2, SEQUENCE(3), A1:A3) selects its second branch, the genuine worksheet reference
        // A1:A3 -- must positionally intersect against the formula cell's row (2) => A2 = 20.
        var result = _evaluator.Evaluate(
            "=@CHOOSE(2,SEQUENCE(3),A1:A3)", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 5));

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void AtOperator_OnChooseSelectingComputedArrayBranch_StillReturnsTopLeftElement()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(30));

        // No-regression sibling: CHOOSE(1, SEQUENCE(3), A1:A3) selects the first branch, the
        // computed/dynamic array SEQUENCE(3) (not anchored to any worksheet cell) -- @ must still
        // return its top-left element (1), regardless of the formula cell's own row (2), exactly as
        // before this fix.
        var result = _evaluator.Evaluate(
            "=@CHOOSE(1,SEQUENCE(3),A1:A3)", sheet, wb, currentCell: new CellAddress(sheet.Id, 2, 5));

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void AtOperator_OnBareRangeRef_Unchanged_StillPositionallyIntersects()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));

        // Sibling no-regression check: the pre-existing RangeRefNode path (already correct before
        // this fix) must remain unaffected.
        var result = _evaluator.Evaluate(
            "=@A2:A4", sheet, currentCell: new CellAddress(sheet.Id, 3, 2));

        result.Should().Be(new NumberValue(20));
    }
}
