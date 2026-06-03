using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for short-circuit evaluation behaviour of IF, IFERROR, and IFNA,
/// and for edge-case argument validation.
/// </summary>
public class ShortCircuitEvaluationTests
{
    private readonly FormulaEvaluator _evaluator = new();

    // ── IF short-circuit ──────────────────────────────────────────────────

    [Fact]
    public void IF_ErrorInFalseBranch_DoesNotEvaluateFalseBranchWhenConditionIsTrue()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>0,\"yes\",1/0)", sheet, wb);
        result.Should().Be(new TextValue("yes"));
    }

    [Fact]
    public void IF_ErrorInTrueBranch_DoesNotEvaluateTrueBranchWhenConditionIsFalse()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>2,1/0,\"no\")", sheet, wb);
        result.Should().Be(new TextValue("no"));
    }

    [Fact]
    public void IF_TextCondition_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(\"TRUE\",\"yes\",\"no\")", sheet, wb);
        result.Should().Be(ErrorValue.Value, "text condition should produce #VALUE! as in Excel");
    }

    [Fact]
    public void IF_TwoArgs_FalseCondition_ReturnsFalse()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>2,\"yes\")", sheet, wb);
        result.Should().Be(new BoolValue(false), "IF with 2 args and false condition returns FALSE");
    }

    [Fact]
    public void IF_ScalarConditionReturnsSelectedRangeBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var trueResult = _evaluator.Evaluate("=IF(TRUE,A1:A2,B1:B2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;
        trueResult.RowCount.Should().Be(2);
        trueResult.Cells[0, 0].Should().Be(new NumberValue(1));
        trueResult.Cells[1, 0].Should().Be(new NumberValue(2));

        var falseResult = _evaluator.Evaluate("=IF(FALSE,A1:A2,B1:B2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;
        falseResult.RowCount.Should().Be(2);
        falseResult.Cells[0, 0].Should().Be(new NumberValue(10));
        falseResult.Cells[1, 0].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void IF_RangeConditionSelectsBranchElements()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new BoolValue(true));

        var result = _evaluator.Evaluate("=IF(C1:C3,A1:A3,B1:B3)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(20));
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void IF_ConditionIsError_PropagatesError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1/0,\"yes\",\"no\")", sheet, wb);
        result.Should().Be(ErrorValue.DivByZero, "error in condition propagates to IF result");
    }

    // ── IFERROR ───────────────────────────────────────────────────────────

    [Fact]
    public void IFERROR_DoesNotEvaluateFallback_WhenValueSucceeds()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFERROR(42,1/0)", sheet, wb);
        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IFERROR_ReturnsFallback_WhenValueErrors()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFERROR(1/0,\"err\")", sheet, wb);
        result.Should().Be(new TextValue("err"));
    }

    [Fact]
    public void IFERROR_ReplacesErrorsElementwiseInArrayValues()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(0));

        var result = _evaluator.Evaluate("=IFERROR(100/A1:A2,\"err\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(10));
        result.Cells[1, 0].Should().Be(new TextValue("err"));
    }

    // ── IFNA ──────────────────────────────────────────────────────────────

    [Fact]
    public void IFNA_ReturnsFallback_OnlyForNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFNA(NA(),\"caught\")", sheet, wb)
            .Should().Be(new TextValue("caught"));
        _evaluator.Evaluate("=IFNA(1/0,\"caught\")", sheet, wb)
            .Should().Be(ErrorValue.DivByZero, "IFNA should only catch #N/A, not other errors");
    }

    [Fact]
    public void IFNA_CleanValue_PassesThrough()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFNA(42,\"caught\")", sheet, wb);
        result.Should().Be(new NumberValue(42), "IFNA must not intercept non-error values");
    }

    [Fact]
    public void IFNA_ReplacesOnlyNaErrorsElementwiseInArrayValues()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));

        var result = _evaluator.Evaluate("=IFNA(A1:A3,\"na\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new TextValue("na"));
        result.Cells[1, 0].Should().Be(ErrorValue.DivByZero);
        result.Cells[2, 0].Should().Be(new NumberValue(7));
    }

    // ── CHOOSE short-circuit ──────────────────────────────────────────────

    [Fact]
    public void CHOOSE_ErrorInUnselectedBranch_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=CHOOSE(1,\"picked\",1/0)", sheet, wb);
        result.Should().Be(new TextValue("picked"), "CHOOSE must not evaluate untaken branches");
    }

    [Fact]
    public void CHOOSE_SelectsCorrectBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=CHOOSE(2,\"a\",\"b\",\"c\")", sheet, wb)
            .Should().Be(new TextValue("b"));
    }

    [Fact]
    public void CHOOSE_ScalarIndexReturnsSelectedRangeBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var result = _evaluator.Evaluate("=CHOOSE(2,A1:A2,B1:B2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(10));
        result.Cells[1, 0].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void CHOOSE_SpilledIndexReturnsSelectedValuesElementwise()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");

        var result = _evaluator.Evaluate("=CHOOSE(SEQUENCE(1,2),\"a\",\"b\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new TextValue("a"));
        result.Cells[0, 1].Should().Be(new TextValue("b"));
    }

    [Fact]
    public void CHOOSE_RangeIndexReturnsSelectedValuesElementwise()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=CHOOSE(A1:A3,\"a\",\"b\",\"c\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new TextValue("a"));
        result.Cells[1, 0].Should().Be(new TextValue("b"));
        result.Cells[2, 0].Should().Be(new TextValue("c"));
    }

    [Fact]
    public void CHOOSE_RangeIndexKeepsInvalidElementsAsValueErrors()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(99));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));

        var result = _evaluator.Evaluate("=CHOOSE(A1:A3,\"a\",\"b\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new TextValue("a"));
        result.Cells[1, 0].Should().Be(ErrorValue.Value);
        result.Cells[2, 0].Should().Be(new TextValue("b"));
    }

    [Fact]
    public void CHOOSE_RangeIndexPropagatesIndexElementErrors()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));

        var result = _evaluator.Evaluate("=CHOOSE(A1:A3,\"a\",\"b\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new TextValue("a"));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
        result.Cells[2, 0].Should().Be(new TextValue("b"));
    }

    [Fact]
    public void CHOOSE_OutOfRange_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=CHOOSE(5,\"a\",\"b\")", sheet, wb)
            .Should().Be(ErrorValue.Value);
    }

    // ── IFS short-circuit ─────────────────────────────────────────────────

    [Fact]
    public void IFS_ErrorInUnreachedPair_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFS(1>0,\"first\",1>0,1/0)", sheet, wb);
        result.Should().Be(new TextValue("first"), "IFS must not evaluate pairs after the first true condition");
    }

    [Fact]
    public void IFS_NoTrueCondition_ReturnsNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFS(1>2,\"no\")", sheet, wb)
            .Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void IFS_ReturnsSelectedRangeBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        var result = _evaluator.Evaluate("=IFS(FALSE,\"skip\",TRUE,A1:A2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void IFS_RangeConditionSelectsBranchElements()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new BoolValue(true));

        var result = _evaluator.Evaluate("=IFS(C1:C3,A1:A3,TRUE,B1:B3)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(20));
        result.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void IFS_ErrorCondition_Propagates()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFS(1/0,\"bad\")", sheet, wb)
            .Should().Be(ErrorValue.DivByZero, "error in a condition propagates");
    }

    // ── SWITCH short-circuit ──────────────────────────────────────────────

    [Fact]
    public void SWITCH_ErrorInUnmatchedBranch_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=SWITCH(1,1,\"one\",2,1/0)", sheet, wb);
        result.Should().Be(new TextValue("one"), "SWITCH must not evaluate unmatched branches");
    }

    [Fact]
    public void SWITCH_UsesDefault_WhenNoMatchFound()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=SWITCH(99,1,\"one\",2,\"two\",\"default\")", sheet, wb)
            .Should().Be(new TextValue("default"));
    }

    [Fact]
    public void SWITCH_ReturnsSelectedRangeBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var matched = _evaluator.Evaluate("=SWITCH(2,1,A1:A2,2,B1:B2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;
        matched.RowCount.Should().Be(2);
        matched.Cells[0, 0].Should().Be(new NumberValue(10));
        matched.Cells[1, 0].Should().Be(new NumberValue(20));

        var defaulted = _evaluator.Evaluate("=SWITCH(99,1,A1:A2,B1:B2)", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;
        defaulted.RowCount.Should().Be(2);
        defaulted.Cells[0, 0].Should().Be(new NumberValue(10));
        defaulted.Cells[1, 0].Should().Be(new NumberValue(20));
    }

    [Fact]
    public void SWITCH_RangeExpressionSelectsResultElements()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SWITCH(A1:A3,1,\"one\",2,\"two\",\"other\")", sheet, wb)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new TextValue("one"));
        result.Cells[1, 0].Should().Be(new TextValue("two"));
        result.Cells[2, 0].Should().Be(new TextValue("other"));
    }

    [Fact]
    public void SWITCH_ScalarExpressionKeepsImplicitIntersectionForRangeCaseValues()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));

        _evaluator.Evaluate("=SWITCH(1,A1:A2,\"hit\",\"miss\")", sheet, wb)
            .Should().Be(new TextValue("hit"));
    }

    [Fact]
    public void SWITCH_NoMatchNoDefault_ReturnsNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=SWITCH(99,1,\"one\",2,\"two\")", sheet, wb)
            .Should().Be(ErrorValue.NA);
    }

    // ── Argument-count validation ─────────────────────────────────────────

    [Fact]
    public void SUM_WithZeroArguments_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=SUM()", sheet, wb);
        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void SUM_RangeOnlyFastPath_PreservesLeftToRightErrorPrecedenceBeforeMissingSheet()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.NA);

        var result = _evaluator.Evaluate("=SUM(A1:A1,Missing!A1:A1)", sheet, wb);

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void AVERAGE_RangeOnlyFastPath_DoesNotLetFinalizationErrorOutrankMissingSheet()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        var result = _evaluator.Evaluate("=AVERAGE(A1:A1,Missing!A1:A1)", sheet, wb);

        result.Should().Be(ErrorValue.Ref);
    }

    // ── Parser row-bounds protection ──────────────────────────────────────

    [Fact]
    public void NonAggregateFunction_WithTooManyArguments_ReturnsValueBeforeRangeExpansion()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");

        var result = _evaluator.Evaluate("=ABS(1,A1:XFD1048576)", sheet, wb);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void CellRef_WithRowBeyondMaxRow_ReturnsNameError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=A2000000", sheet, wb);
        result.Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void CellRef_WithRowZero_ReturnsNameError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=A0", sheet, wb);
        result.Should().Be(ErrorValue.Name);
    }

    // ── Recursion depth guard (Issue E) ──

    [Fact]
    public void DeeplyNested_FunctionCall_ReturnsNumErrorInsteadOfStackOverflow()
    {
        // Construct a 300-level deep IF(TRUE, IF(TRUE, ... 1 ...)) AST directly
        // to avoid any parser stack-overflow and test only the evaluator depth guard.
        const int depth = 300;
        FormulaNode body = new NumberNode(1);
        for (int i = 0; i < depth; i++)
        {
            body = new FunctionCallNode("IF",
                [new BooleanNode(true), body]);
        }

        var sheet = new Sheet(SheetId.New(), "S");
        // Should return #NUM! (depth exceeded), not throw StackOverflowException
        var result = _evaluator.Evaluate(body, sheet);
        result.Should().Be(ErrorValue.Num,
            "a 300-level nested formula must return #NUM! rather than causing a stack overflow");
    }

    [Fact]
    public void ModeratelyNested_FunctionCall_EvaluatesNormally()
    {
        // 10 levels of nesting should work fine (well within the 256 depth limit)
        FormulaNode body = new NumberNode(42);
        for (int i = 0; i < 10; i++)
        {
            body = new FunctionCallNode("IF",
                [new BooleanNode(true), body]);
        }

        var sheet = new Sheet(SheetId.New(), "S");
        var result = _evaluator.Evaluate(body, sheet);
        result.Should().Be(new NumberValue(42),
            "10-level nesting is well within the recursion limit and should evaluate normally");
    }
}
