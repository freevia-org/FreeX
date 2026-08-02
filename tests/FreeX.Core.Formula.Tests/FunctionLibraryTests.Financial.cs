using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact] public void Pmt_MonthlyPayment_ReturnsNegative()
    {
        // PMT(5%/12, 60, 10000) ≈ -188.71
        ((NumberValue)_eval.Evaluate("=PMT(0.05/12,60,10000)", MakeSheet())).Value
            .Should().BeApproximately(-188.71, 0.01);
    }

    [Fact] public void Pmt_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=PMT(0.05/12,60,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void CoreFinancialFunctions_RangeRateArgument_SpillElementwise()
    {
        var rates = MakeSheet((1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A2,60,10000)", rates),
            ((NumberValue)_eval.Evaluate("=PMT(A1,60,10000)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A2,60,10000)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=PV(A1:A2,60,188.71)", rates),
            ((NumberValue)_eval.Evaluate("=PV(A1,60,188.71)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=PV(A2,60,188.71)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=FV(A1:A2,12,-100)", rates),
            ((NumberValue)_eval.Evaluate("=FV(A1,12,-100)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=FV(A2,12,-100)", rates)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=NPER(A1:A2,-188.71,10000)", rates),
            ((NumberValue)_eval.Evaluate("=NPER(A1,-188.71,10000)", rates)).Value,
            ((NumberValue)_eval.Evaluate("=NPER(A2,-188.71,10000)", rates)).Value);
    }

    [Fact]
    public void Pmt_OneCellControlRanges_BroadcastAcrossRateArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)),
            (2, 1, new NumberValue(0.02)),
            (1, 2, new NumberValue(12)),
            (1, 3, new NumberValue(1000)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A2,B1:B1,C1:C1)", sheet),
            ((NumberValue)_eval.Evaluate("=PMT(A1,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A2,B1,C1)", sheet)).Value);
    }

    [Fact]
    public void Pmt_LeadingOneCellRateRange_BroadcastsAcrossLaterArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)),
            (1, 2, new NumberValue(12)),
            (2, 2, new NumberValue(24)),
            (1, 3, new NumberValue(1000)));

        AssertApproxColumn(
            _eval.Evaluate("=PMT(A1:A1,B1:B2,C1:C1)", sheet),
            ((NumberValue)_eval.Evaluate("=PMT(A1,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PMT(A1,B2,C1)", sheet)).Value);
    }

    [Fact]
    public void IpmtAndPpmt_SameShapeRateAndPeriodRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)),
            (1, 2, new NumberValue(1)),         (2, 2, new NumberValue(2)));

        AssertApproxColumn(
            _eval.Evaluate("=IPMT(A1:A2,B1:B2,60,10000)", sheet),
            ((NumberValue)_eval.Evaluate("=IPMT(A1,B1,60,10000)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=IPMT(A2,B2,60,10000)", sheet)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=PPMT(A1:A2,B1:B2,60,10000)", sheet),
            ((NumberValue)_eval.Evaluate("=PPMT(A1,B1,60,10000)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=PPMT(A2,B2,60,10000)", sheet)).Value);
    }

    [Fact]
    public void IpmtAndPpmt_MismatchedRateAndPeriodRangeShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)), (2, 1, new NumberValue(0.06 / 12)),
            (1, 2, new NumberValue(1)),         (1, 3, new NumberValue(2)));

        // A row-vector (1x2) crossed with a column-vector (2x1) is now a valid cross-broadcast
        // (R118-formula-arity3plus-cross-broadcast), so this uses B1:B3 (a same-axis, differently
        // sized column) to keep testing a genuine shape mismatch.
        _eval.Evaluate("=IPMT(A1:A2,B1:B3,60,10000)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=PPMT(A1:A2,B1:B3,60,10000)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void CumipmtAndCumprinc_LeadingOneCellRateRange_BroadcastsAcrossStartArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05 / 12)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertApproxColumn(
            _eval.Evaluate("=CUMIPMT(A1:A1,60,10000,B1:B2,12,0)", sheet),
            ((NumberValue)_eval.Evaluate("=CUMIPMT(A1,60,10000,B1,12,0)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=CUMIPMT(A1,60,10000,B2,12,0)", sheet)).Value);
        AssertApproxColumn(
            _eval.Evaluate("=CUMPRINC(A1:A1,60,10000,B1:B2,12,0)", sheet),
            ((NumberValue)_eval.Evaluate("=CUMPRINC(A1,60,10000,B1,12,0)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=CUMPRINC(A1,60,10000,B2,12,0)", sheet)).Value);
    }

    [Fact]
    public void Xnpv_RateRange_SpillsElementwiseAgainstValueAndDateArrays()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.05)), (2, 1, new NumberValue(0.10)),
            (1, 2, new NumberValue(-1000)), (2, 2, new NumberValue(600)), (3, 2, new NumberValue(600)),
            (1, 3, new NumberValue(new DateTime(2026, 1, 1).ToOADate())),
            (2, 3, new NumberValue(new DateTime(2026, 7, 1).ToOADate())),
            (3, 3, new NumberValue(new DateTime(2027, 1, 1).ToOADate())));

        AssertApproxColumn(
            _eval.Evaluate("=XNPV(A1:A2,B1:B3,C1:C3)", sheet),
            ((NumberValue)_eval.Evaluate("=XNPV(A1,B1:B3,C1:C3)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=XNPV(A2,B1:B3,C1:C3)", sheet)).Value);
    }

    [Fact]
    public void Mirr_FinanceAndReinvestRateRanges_SpillElementwiseAgainstCashFlowArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)), (2, 1, new NumberValue(400)), (3, 1, new NumberValue(700)),
            (1, 2, new NumberValue(0.05)), (2, 2, new NumberValue(0.06)),
            (1, 3, new NumberValue(0.07)), (2, 3, new NumberValue(0.08)));

        AssertApproxColumn(
            _eval.Evaluate("=MIRR(A1:A3,B1:B2,C1:C2)", sheet),
            ((NumberValue)_eval.Evaluate("=MIRR(A1:A3,B1,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=MIRR(A1:A3,B2,C2)", sheet)).Value);
    }

    [Fact]
    public void Xirr_GuessRange_SpillsElementwiseAgainstValueAndDateArrays()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)), (2, 1, new NumberValue(600)), (3, 1, new NumberValue(600)),
            (1, 2, new NumberValue(new DateTime(2026, 1, 1).ToOADate())),
            (2, 2, new NumberValue(new DateTime(2026, 7, 1).ToOADate())),
            (3, 2, new NumberValue(new DateTime(2027, 1, 1).ToOADate())),
            (1, 3, new NumberValue(0.05)), (2, 3, new NumberValue(0.15)));

        AssertApproxColumn(
            _eval.Evaluate("=XIRR(A1:A3,B1:B3,C1:C2)", sheet),
            ((NumberValue)_eval.Evaluate("=XIRR(A1:A3,B1:B3,C1)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=XIRR(A1:A3,B1:B3,C2)", sheet)).Value);
    }

    [Fact]
    public void Fvschedule_PrincipalRange_SpillsElementwiseAgainstScheduleArray()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(100)), (2, 1, new NumberValue(200)),
            (1, 2, new NumberValue(0.10)), (2, 2, new NumberValue(0.05)));

        AssertApproxColumn(
            _eval.Evaluate("=FVSCHEDULE(A1:A2,B1:B2)", sheet),
            ((NumberValue)_eval.Evaluate("=FVSCHEDULE(A1,B1:B2)", sheet)).Value,
            ((NumberValue)_eval.Evaluate("=FVSCHEDULE(A2,B1:B2)", sheet)).Value);
    }

    [Fact] public void Pmt_TypeError_PropagatesError() =>
        _eval.Evaluate("=PMT(0.05/12,60,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Pmt_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=PMT(A1,60,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Pmt_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=PMT(0.05/12,60,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Pv_FutureValue_ReturnsPresent()
    {
        // PV(5%/12, 60, 188.71) ≈ -10000
        ((NumberValue)_eval.Evaluate("=PV(0.05/12,60,188.71)", MakeSheet())).Value
            .Should().BeApproximately(-10000, 1.0);
    }

    [Fact] public void Pv_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=PV(0.05/12,60,188.71,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Pv_TypeError_PropagatesError() =>
        _eval.Evaluate("=PV(0.05/12,60,188.71,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Pv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=PV(A1,60,188.71)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Pv_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=PV(0.05/12,60,188.71,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Fv_Savings_ReturnsAccumulated()
    {
        // FV(5%/12, 12, -100) ≈ 1227.89
        ((NumberValue)_eval.Evaluate("=FV(0.05/12,12,-100)", MakeSheet())).Value
            .Should().BeApproximately(1227.89, 0.1);
    }

    [Fact] public void Fv_PresentValueError_PropagatesError() =>
        _eval.Evaluate("=FV(0.05/12,12,-100,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Fv_TypeError_PropagatesError() =>
        _eval.Evaluate("=FV(0.05/12,12,-100,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Fv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=FV(A1,12,-100)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Fv_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=FV(0.05/12,12,-100,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Nper_CountPeriods_Returns60()
    {
        ((NumberValue)_eval.Evaluate("=NPER(0.05/12,-188.71,10000)", MakeSheet())).Value
            .Should().BeApproximately(60, 0.1);
    }

    [Fact] public void Nper_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Nper_TypeError_PropagatesError() =>
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Nper_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=NPER(A1,-188.71,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Nper_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=NPER(0.05/12,-188.71,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Rate_FindsInterestRate()
    {
        // RATE(60, -188.71, 10000) ≈ 0.05/12
        ((NumberValue)_eval.Evaluate("=RATE(60,-188.71,10000)", MakeSheet())).Value
            .Should().BeApproximately(0.05 / 12, 1e-5);
    }

    [Fact] public void Rate_FutureValueError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Rate_TypeError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact] public void Rate_GuessError_PropagatesError() =>
        _eval.Evaluate("=RATE(60,-188.71,10000,0,0,NA())", MakeSheet()).Should().Be(ErrorValue.NA);

    [Fact]
    public void Rate_NonFiniteNper_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RATE(A1,-188.71,10000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rate_NonFiniteGuess_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RATE(60,-188.71,10000,0,0,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Rate_InvalidType_ReturnsNumError()
    {
        _eval.Evaluate("=RATE(60,-188.71,10000,0,2)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Npv_BasicCashflow_ReturnsNpv()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(400)),
            (3,1,new NumberValue(400)),
            (4,1,new NumberValue(400)));
        ((NumberValue)_eval.Evaluate("=NPV(0.1,A1:A4)", sheet)).Value
            .Should().BeApproximately(-1000.0/1.1 + 400.0/1.21 + 400.0/1.331 + 400.0/1.4641, 0.01);
    }

    [Fact] public void Npv_DirectLogical_IncludesValueArgument()
    {
        _eval.Evaluate("=NPV(0,TRUE,3)", MakeSheet()).Should().Be(new NumberValue(4));
    }

    [Fact] public void Npv_DirectNumericText_IncludesValueArgument()
    {
        _eval.Evaluate("=NPV(0,\"1\",3)", MakeSheet()).Should().Be(new NumberValue(4));
    }

    [Fact] public void Npv_ReferencedLogical_IgnoresValue()
    {
        var sheet = MakeSheet((1,1,new BoolValue(true)),(2,1,new NumberValue(3)));
        _eval.Evaluate("=NPV(0,A1:A2)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact] public void Npv_DirectRangeFastPath_PreservesLiteralAndReferenceCoercion()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("2")),
            (3, 1, new BoolValue(true)),
            (4, 1, new NumberValue(3)),
            (5, 1, new BoolValue(true)));

        _eval.Evaluate("=NPV(0,A1:A3,\"4\",A4,A5)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Npv_NonFiniteRate_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=NPV(A1,100)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Npv_RateExactlyNegativeOne_ReturnsDivByZeroError()
    {
        // R79-formula-financial-5-3: rate=-1 makes (1+rate)^1 = 0, so 100/0 is a literal
        // division by zero. Excel's standard x/0 propagation rule surfaces #DIV/0! here (a
        // different error than XNPV/XIRR's documented #NUM! for their own rate<=-1 boundary).
        // Before the fix, Npv only guarded against non-finite rate, let the division produce
        // +Infinity, and then NumberResult collapsed any non-finite sum to the generic #NUM!.
        _eval.Evaluate("=NPV(-1,100)", MakeSheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Npv_RateExactlyNegativeOne_MultipleValues_ReturnsDivByZeroError()
    {
        // Same as above but with multiple cash flows, confirming the error fires on the first
        // zero-denominator term rather than only when there is exactly one value.
        _eval.Evaluate("=NPV(-1,100,200,300)", MakeSheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Npv_RateExactlyNegativeOne_DirectRangeFastPath_ReturnsDivByZeroError()
    {
        // Same rate=-1 boundary but routed through the direct-range fast path
        // (TryEvaluateNpvDirectRanges in FormulaEvaluator.FinancialFastPaths.cs), which shares
        // the identical division and must surface the same #DIV/0! rather than #NUM!.
        var sheet = MakeSheet((1, 1, new NumberValue(100)), (2, 1, new NumberValue(200)));
        _eval.Evaluate("=NPV(-1,A1:A2)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Npv_RateLessThanNegativeOne_NoRegression()
    {
        // No-regression sibling: rate < -1 (e.g. -2) never zeroes the denominator for an integer
        // exponent -- Math.Pow(-1, n) is +/-1, not 0 -- so NPV must still compute a normal finite
        // result rather than erroring, confirming the new denom==0 guard only fires exactly at
        // the true division-by-zero boundary.
        ((NumberValue)_eval.Evaluate("=NPV(-2,100)", MakeSheet())).Value
            .Should().BeApproximately(100.0 / -1.0, 1e-9);
    }

    [Fact] public void Irr_CashflowSeries_ReturnsRate()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(300)),
            (3,1,new NumberValue(400)),
            (4,1,new NumberValue(500)));
        ((NumberValue)_eval.Evaluate("=IRR(A1:A4)", sheet)).Value
            .Should().BeApproximately(0.0890, 0.001);
    }

    [Fact] public void Irr_GuessError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Irr_RangeError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(-1000)),
            (2,1,ErrorValue.NA),
            (3,1,new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Irr_RangeArgumentError_PropagatesError()
    {
        _eval.Evaluate("=IRR(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Irr_NonFiniteGuess_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(1100)),
            (3, 1, new TextValue("1E309")));

        _eval.Evaluate("=IRR(A1:A2,A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_AllPositiveCashflows_ReturnsNumError()
    {
        // No sign change — IRR equation has no real solution above -1; Excel returns #NUM!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(100)),
            (2, 1, new NumberValue(200)),
            (3, 1, new NumberValue(300)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_AllNegativeCashflows_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-100)),
            (2, 1, new NumberValue(-200)),
            (3, 1, new NumberValue(-300)));
        _eval.Evaluate("=IRR(A1:A3)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_GuessAtOrBelowMinusOne_ReturnsNumError()
    {
        // 1 + guess must be > 0 for the IRR Newton iteration to make sense.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(1100)));
        _eval.Evaluate("=IRR(A1:A2,-1)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=IRR(A1:A2,-2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Sln_StraightLine_ReturnsAnnualDep()
    {
        // SLN(10000, 1000, 9) = 1000
        _eval.Evaluate("=SLN(10000,1000,9)", MakeSheet()).Should().Be(new NumberValue(1000));
    }


    [Fact]
    public void Sln_NonFiniteCost_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=SLN(A1,1000,9)", sheet).Should().Be(ErrorValue.Num);
    }
}
