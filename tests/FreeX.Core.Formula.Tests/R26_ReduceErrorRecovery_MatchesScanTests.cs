using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R26-error-handling-array-deep-1: REDUCE previously hard-stopped the fold the moment the
/// accumulator became an ErrorValue (`if (acc is ErrorValue accError) return accError;`),
/// never invoking the lambda again for the remaining array elements. SCAN has no analogous
/// guard: it keeps invoking the lambda with an error-valued accumulator on every subsequent
/// element, letting the lambda inspect/recover from it via ISERROR/IFERROR. Excel documents
/// REDUCE's result as always equal to SCAN's last output for the same array/lambda/initial
/// value, so a lambda that recovers from an intermediate error via ISERROR(acc) must produce
/// the SAME final value from both REDUCE and SCAN. Removing REDUCE's early-return restores
/// that parity while still surfacing genuine (unrecovered) errors, since FreeX's binary
/// operators already propagate an ErrorValue operand automatically.
/// </summary>
public class R26_ReduceErrorRecovery_MatchesScanTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    private static RangeValue Rv(ScalarValue value) => (RangeValue)value;

    // Bug case: a lambda that recovers from an intermediate error via ISERROR(acc) must let
    // REDUCE keep folding the remaining elements, matching SCAN's last value (999), instead of
    // hard-stopping on the #DIV/0! produced at v=2.
    [Fact]
    public void Reduce_LambdaRecoversFromIntermediateError_MatchesScanLastValue()
    {
        const string lambda = "LAMBDA(acc,val, IF(ISERROR(acc), 999, IF(val=2, 1/0, acc+val)))";

        var reduced = Eval($"=REDUCE(0, SEQUENCE(3), {lambda})");
        var scanned = Rv(Eval($"=SCAN(0, SEQUENCE(3), {lambda})"));

        Assert.Equal(999.0, Num(reduced));
        Assert.Equal(999.0, Num(scanned.At(3, 1)));
        Assert.Equal(Num(scanned.At(3, 1)), Num(reduced));
    }

    // Sibling already-working case: a lambda with NO error-recovery logic must still let a
    // genuine error propagate all the way through to REDUCE's final result (via ordinary
    // arithmetic error propagation, not an early-return short-circuit), preserving the P75
    // regression this finding must not undo.
    [Fact]
    public void Reduce_LambdaWithoutRecovery_StillPropagatesGenuineErrorToFinalResult()
    {
        var result = Eval("=REDUCE(0, SEQUENCE(3), LAMBDA(a,v, IF(v=2, 1/0, a+v)))");

        Assert.IsType<ErrorValue>(result);
        Assert.Equal(ErrorValue.DivByZero, result);
    }
}
