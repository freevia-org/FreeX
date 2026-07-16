using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R44-formula-index-match-array-3-1: INDEX(...) must be usable as one (or both) side(s) of the
/// ':' range operator, exactly like real Excel's INDEX "reference form" -- e.g. the classic
/// technique =SUM(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3)) must evaluate the range A1:C3, not throw a
/// parse error. Before the fix, Parser.ParsePostfix only recognized ':' immediately after a raw
/// CellRef token (in ParsePrimary's TokenType.CellRef case); a FunctionCallNode result like
/// INDEX(...) left the trailing ':' unconsumed, so Parser.Parse() rejected the whole formula as
/// "Unexpected token ':'", which FormulaEvaluator surfaced as #VALUE! instead of the intended
/// range value.
/// </summary>
public sealed class R44_IndexReferenceFormRangeEndpointTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static Sheet MakeGridSheet()
    {
        // A1:C3 = {1,2,3;4,5,6;7,8,9}
        var sheet = new Sheet(SheetId.New(), "S");
        int n = 1;
        for (int r = 1; r <= 3; r++)
            for (int c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(n++));
        return sheet;
    }

    [Fact]
    public void Sum_OverIndexToIndexRange_ReturnsSumOfWholeRange()
    {
        // The exact failure scenario from the finding: INDEX(A1:C3,1,1) is A1, INDEX(A1:C3,3,3) is
        // C3, and ':' combines them into A1:C3 (sum = 1+2+...+9 = 45).
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3))", sheet)
            .Should().Be(new NumberValue(45));
    }

    [Fact]
    public void Sum_OverIndexToPlainCellRef_ReturnsSumOfRange()
    {
        // One side of ':' is INDEX(...), the other a plain cell reference -- also a common shape.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(INDEX(A1:C3,1,1):C3)", sheet)
            .Should().Be(new NumberValue(45));

        _eval.Evaluate("=SUM(A1:INDEX(A1:C3,3,3))", sheet)
            .Should().Be(new NumberValue(45));
    }

    [Fact]
    public void Sum_OverIndexRange_WithSingleColumnOmittedColumnNum_SelectsCorrectCells()
    {
        // Two-arg INDEX over a single-column range: INDEX(A1:A5,2) is A2, INDEX(A1:A5,4) is A4.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)),
            (4, 1, new NumberValue(40)), (5, 1, new NumberValue(50)));

        _eval.Evaluate("=SUM(INDEX(A1:A5,2):INDEX(A1:A5,4))", sheet)
            .Should().Be(new NumberValue(90)); // 20+30+40
    }

    [Fact]
    public void Sum_OverNestedIndexRange_FoldsRecursively()
    {
        // INDEX(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3),1,1) - the outer INDEX's range argument is
        // itself an INDEX-anchored range, requiring the fold to recurse.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=INDEX(INDEX(A1:C3,1,1):INDEX(A1:C3,3,3),2,2)", sheet)
            .Should().Be(new NumberValue(5)); // center cell of A1:C3
    }

    [Fact]
    public void Sum_OverIndexRange_OutOfBoundsIndex_ReturnsRefError()
    {
        // INDEX(A1:C3,5,5) is out of the 3x3 range's bounds -> #REF!, matching Excel; the whole
        // range expression (and therefore SUM) must surface that #REF!.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(INDEX(A1:C3,1,1):INDEX(A1:C3,5,5))", sheet)
            .Should().Be(ErrorValue.Ref);
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void Index_UsedAsOrdinaryValue_StillReturnsScalar()
    {
        // Sibling already-working case: INDEX(...) NOT followed by ':' must still evaluate as a
        // plain scalar value exactly as before -- this fix must not change ordinary INDEX usage.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=INDEX(A1:C3,2,2)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sum_OverPlainCellRefRange_StillWorks()
    {
        // Sibling: the pre-existing plain CellRef ':' range path (no INDEX involved at all) must
        // be completely unaffected.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:C3)", sheet).Should().Be(new NumberValue(45));
    }

    [Fact]
    public void Sum_OverDynamicIndexMatchRange_StillReturnsValueError()
    {
        // The classic dynamic named-range pattern (INDEX(range, MATCH(...))) has a row_num that
        // isn't a parse-time literal, so it cannot be statically folded -- this remains unresolved
        // exactly as before the fix (still #VALUE!, not a regression, not a crash).
        var sheet = MakeSheet(
            (1, 1, new TextValue("start")), (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)), (4, 1, new TextValue("end")));

        _eval.Evaluate(
                "=SUM(INDEX(A1:A4,MATCH(\"start\",A1:A4,0)):INDEX(A1:A4,MATCH(\"end\",A1:A4,0)))",
                sheet)
            .Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sum_OverNonIndexFunctionCallColonRange_StillReturnsValueError()
    {
        // Sibling: a non-INDEX function call on the left of ':' must remain unhandled exactly as
        // before (the fold only ever applies to FunctionName == "INDEX").
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(SUM(A1:A1):C3)", sheet).Should().Be(ErrorValue.Value);
    }
}
