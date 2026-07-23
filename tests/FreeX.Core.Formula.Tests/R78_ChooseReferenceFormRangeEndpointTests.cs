using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R78-formula-reference-fns-5-1: CHOOSE(...) must be usable as one (or both) side(s) of the ':'
/// range operator, exactly like real Excel's CHOOSE "reference form" -- e.g.
/// =SUM(A1:CHOOSE(2,B5,C5)) must evaluate the range A1:C5, not throw a parse error. Before the
/// fix, Parser.ParsePostfix's ':'-fold (and ParseIndexRangeEndpoint's FunctionName branch) only
/// recognized INDEX(...) as a foldable reference-returning endpoint via
/// TryFoldIndexReferenceToCellRef, which unconditionally returned false for any other function
/// name -- so a CHOOSE(...) endpoint fell through to "Expected cell reference after ':'", which
/// FormulaEvaluator surfaced as #VALUE! for the whole formula instead of the intended range value.
/// </summary>
public sealed class R78_ChooseReferenceFormRangeEndpointTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeGridSheet()
    {
        // A1:C5 = rows 1..5, cols A..C, values 1..15 row-major.
        var sheet = new Sheet(SheetId.New(), "S");
        int n = 1;
        for (int r = 1; r <= 5; r++)
            for (int c = 1; c <= 3; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(n++));
        return sheet;
    }

    [Fact]
    public void Sum_OverPlainCellRefToChooseRange_ReturnsSumOfWholeRange()
    {
        // The exact failure scenario from the finding: CHOOSE(2,B5,C5) selects C5 (index 2 of
        // {B5,C5}), so A1:CHOOSE(2,B5,C5) is A1:C5 -- sum of 1..15 = 120.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:CHOOSE(2,B5,C5))", sheet)
            .Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Sum_OverChooseToPlainCellRefRange_ReturnsSumOfWholeRange()
    {
        // CHOOSE(...) used as the START endpoint instead: CHOOSE(1,A1,B1) selects A1, so
        // CHOOSE(1,A1,B1):C5 is A1:C5 -- same total.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(CHOOSE(1,A1,B1):C5)", sheet)
            .Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Sum_OverChooseToChooseRange_ReturnsSumOfWholeRange()
    {
        // Both endpoints are CHOOSE(...) calls.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(CHOOSE(1,A1,B1):CHOOSE(2,B5,C5))", sheet)
            .Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Sum_OverChooseRange_OutOfRangeIndex_ReturnsValueError()
    {
        // CHOOSE(3,B5,C5) has only 2 reference branches -- index 3 is out of CHOOSE's own valid
        // range, so it yields #VALUE! (matching EvaluateChoose's runtime out-of-range handling),
        // which SUM must surface for the whole formula.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(A1:CHOOSE(3,B5,C5))", sheet)
            .Should().Be(ErrorValue.Value);
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void Choose_UsedAsOrdinaryValue_StillReturnsScalar()
    {
        // Sibling already-working case: CHOOSE(...) NOT followed by ':' must still evaluate as a
        // plain scalar value exactly as before -- this fix must not change ordinary CHOOSE usage.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=CHOOSE(2,A1,B1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sum_OverIndexToIndexRange_StillWorks()
    {
        // Sibling: the pre-existing INDEX(...) ':'-fold path must be completely unaffected by
        // adding the CHOOSE dispatch.
        var sheet = MakeGridSheet();

        _eval.Evaluate("=SUM(INDEX(A1:C5,1,1):INDEX(A1:C5,5,3))", sheet)
            .Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Sum_OverDynamicChooseIndexRange_StillReturnsValueError()
    {
        // A non-literal index_num (e.g. computed via MATCH) can't be resolved at parse time --
        // remains unhandled exactly as before (still #VALUE!, not a crash, not a regression).
        var sheet = MakeGridSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(2));

        _eval.Evaluate("=SUM(A1:CHOOSE(MATCH(2,A6:A6,0),B5,C5))", sheet)
            .Should().Be(ErrorValue.Value);
    }
}
