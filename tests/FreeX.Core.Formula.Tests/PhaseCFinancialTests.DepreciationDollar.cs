using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    // ── SYD ───────────────────────────────────────────────────────────────

    [Fact]
    public void Syd_ExcelDocExample()
    {
        // SYD(30000, 7500, 10, 1) = (30000-7500)*10/(10*11/2) = 22500*10/55 ≈ 4090.91
        double result = Calc("SYD(30000,7500,10,1)");
        result.Should().BeApproximately(4090.909, 0.01);
    }

    [Fact]
    public void Syd_LastPeriod_ReturnsSmallest()
    {
        double last = Calc("SYD(30000,7500,10,10)");
        double first = Calc("SYD(30000,7500,10,1)");
        last.Should().BeLessThan(first);
        last.Should().BeApproximately(4090.909 / 10, 0.01);
    }

    // ── DDB ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ddb_ExcelDocExample()
    {
        // DDB(2400, 300, 10, 1) = min(2400-300, 2400*2/10) = min(2100, 480) = 480
        double result = Calc("DDB(2400,300,10,1)");
        result.Should().BeApproximately(480.0, 0.001);
    }

    [Fact]
    public void Ddb_Period2_DecreasesFromPeriod1()
    {
        double p1 = Calc("DDB(2400,300,10,1)");
        double p2 = Calc("DDB(2400,300,10,2)");
        p2.Should().BeLessThan(p1);
    }

    // ── DB ────────────────────────────────────────────────────────────────

    [Fact]
    public void Db_Period1_ReturnsCorrect()
    {
        // DB(1000000, 100000, 6, 1, 7)
        // Rate = 1 - (100000/1000000)^(1/6) = 1 - 0.1^(1/6) ≈ 0.319 (rounded to 3dp = 0.319)
        // Dep1 = 1000000 * 0.319 * 7/12 ≈ 186,083
        double result = Calc("DB(1000000,100000,6,1,7)");
        result.Should().BeApproximately(186083.33, 1.0);
    }

    [Fact]
    public void Db_InvalidCost_ReturnsNumError()
        => CalcError("DB(0,100,6,1)").Should().Be("#NUM!");

    // ── VDB ───────────────────────────────────────────────────────────────

    [Fact]
    public void Vdb_WholeFirstPeriod_MatchesDdb()
    {
        // VDB over period 0 to 1 with factor=2 should match DDB(cost, salvage, life, 1)
        double vdb = Calc("VDB(2400,300,10,0,1)");
        double ddb = Calc("DDB(2400,300,10,1)");
        vdb.Should().BeApproximately(ddb, 0.001);
    }

    [Fact]
    public void Vdb_InvalidInputs_ReturnsNumError()
        => CalcError("VDB(2400,300,10,0,11)").Should().Be("#NUM!");

    // ── DOLLARDE / DOLLARFR ───────────────────────────────────────────────

    [Fact]
    public void DepreciationFunctions_RangePeriodArgument_SpillElementwise()
    {
        var periods = new[] { (1, 1, 1.0), (2, 1, 2.0) };

        AssertApproxColumn(EvalWithData("SLN(2400,300,A1:A2)", (1, 1, 10.0), (2, 1, 20.0)), Calc("SLN(2400,300,10)"), Calc("SLN(2400,300,20)"));
        AssertApproxColumn(EvalWithData("SYD(30000,7500,10,A1:A2)", periods), Calc("SYD(30000,7500,10,1)"), Calc("SYD(30000,7500,10,2)"));
        AssertApproxColumn(EvalWithData("DDB(2400,300,10,A1:A2)", periods), Calc("DDB(2400,300,10,1)"), Calc("DDB(2400,300,10,2)"));
        AssertApproxColumn(EvalWithData("DB(1000000,100000,6,A1:A2,7)", periods), Calc("DB(1000000,100000,6,1,7)"), Calc("DB(1000000,100000,6,2,7)"));
        AssertApproxColumn(EvalWithData("VDB(2400,300,10,0,A1:A2)", periods), Calc("VDB(2400,300,10,0,1)"), Calc("VDB(2400,300,10,0,2)"));
        AssertApproxColumn(EvalWithData("AMORDEGRC(2400,43831,44197,300,A1:A2,0.2,0)", periods), Calc("AMORDEGRC(2400,43831,44197,300,1,0.2,0)"), Calc("AMORDEGRC(2400,43831,44197,300,2,0.2,0)"));
        AssertApproxColumn(EvalWithData("AMORLINC(2400,43831,44197,300,A1:A2,0.3,0)", periods), Calc("AMORLINC(2400,43831,44197,300,1,0.3,0)"), Calc("AMORLINC(2400,43831,44197,300,2,0.3,0)"));
    }

    [Fact]
    public void DepreciationFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 2400.0), (2, 1, 3000.0),
            (1, 2, 300.0), (2, 2, 500.0),
            (1, 3, 10.0), (2, 3, 12.0),
            (1, 4, 1.0), (2, 4, 2.0),
            (1, 5, 2.0), (2, 5, 1.5),
            (1, 6, 7.0), (2, 6, 12.0),
            (1, 7, 0.0), (2, 7, 1.0),
            (1, 8, 1.0), (2, 8, 2.0),
            (1, 9, 0.0), (2, 9, 1.0),
            (1, 10, 43831.0), (2, 10, 43862.0),
            (1, 11, 44197.0), (2, 11, 44228.0),
            (1, 12, 0.2), (2, 12, 0.25)
        };

        AssertApproxColumn(EvalWithData("SLN(A1:A2,B1:B2,C1:C2)", cells), Calc("SLN(2400,300,10)"), Calc("SLN(3000,500,12)"));
        AssertApproxColumn(EvalWithData("SYD(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("SYD(2400,300,10,1)"), Calc("SYD(3000,500,12,2)"));
        AssertApproxColumn(EvalWithData("DDB(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2)", cells), Calc("DDB(2400,300,10,1,2)"), Calc("DDB(3000,500,12,2,1.5)"));
        AssertApproxColumn(EvalWithData("DB(A1:A2,B1:B2,C1:C2,D1:D2,F1:F2)", cells), Calc("DB(2400,300,10,1,7)"), Calc("DB(3000,500,12,2,12)"));
        AssertApproxColumn(EvalWithData("VDB(A1:A2,B1:B2,C1:C2,G1:G2,H1:H2,E1:E2,I1:I2)", cells), Calc("VDB(2400,300,10,0,1,2,0)"), Calc("VDB(3000,500,12,1,2,1.5,1)"));
        AssertApproxColumn(EvalWithData("AMORDEGRC(A1:A2,J1:J2,K1:K2,B1:B2,D1:D2,L1:L2,I1:I2)", cells), Calc("AMORDEGRC(2400,43831,44197,300,1,0.2,0)"), Calc("AMORDEGRC(3000,43862,44228,500,2,0.25,1)"));
        AssertApproxColumn(EvalWithData("AMORLINC(A1:A2,J1:J2,K1:K2,B1:B2,D1:D2,L1:L2,I1:I2)", cells), Calc("AMORLINC(2400,43831,44197,300,1,0.2,0)"), Calc("AMORLINC(3000,43862,44228,500,2,0.25,1)"));

        EvalWithData("DDB(A1:A2,B1:B3,10,1)", cells).Should().Be(ErrorValue.Value);
        EvalWithData("AMORLINC(A1:A2,J1:J3,44197,300,1,0.2,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Dollarde_FractionalDollar()
    {
        // DOLLARDE(1.02, 32) = 1 + 2/32 = 1.0625
        double result = Calc("DOLLARDE(1.02,32)");
        result.Should().BeApproximately(1.0625, 0.0001);
    }

    [Fact]
    public void DollarFractionHelpers_RangeFirstArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("DOLLARDE(A1:A2,32)", (1, 1, 1.02), (2, 1, 2.16)),
            Calc("DOLLARDE(1.02,32)"),
            Calc("DOLLARDE(2.16,32)"));
        AssertApproxColumn(
            EvalWithData("DOLLARFR(A1:A2,32)", (1, 1, 1.0625), (2, 1, 2.5)),
            Calc("DOLLARFR(1.0625,32)"),
            Calc("DOLLARFR(2.5,32)"));
    }

    [Fact]
    public void DollarFractionHelpers_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        AssertApproxColumn(
            EvalWithData("DOLLARDE(A1:A2,B1:B2)", (1, 1, 1.02), (2, 1, 2.16), (1, 2, 32.0), (2, 2, 16.0)),
            Calc("DOLLARDE(1.02,32)"),
            Calc("DOLLARDE(2.16,16)"));
        AssertApproxColumn(
            EvalWithData("DOLLARFR(A1:A2,B1:B2)", (1, 1, 1.0625), (2, 1, 2.5), (1, 2, 32.0), (2, 2, 16.0)),
            Calc("DOLLARFR(1.0625,32)"),
            Calc("DOLLARFR(2.5,16)"));

        // Regression guard for R62-formula-array-broadcast-6-1: a 2x1 column vector (A1:A2)
        // crossed with a 1x2 row vector (B1:C1) must 2-D cross-broadcast into a 2x2 spilled
        // result, not #VALUE! -- this previously asserted the old (superseded) #VALUE! behavior.
        AssertApproxGrid(
            EvalWithData("DOLLARDE(A1:A2,B1:C1)", (1, 1, 1.02), (2, 1, 2.16), (1, 2, 32.0), (1, 3, 16.0)),
            new[,] { { Calc("DOLLARDE(1.02,32)"), Calc("DOLLARDE(1.02,16)") }, { Calc("DOLLARDE(2.16,32)"), Calc("DOLLARDE(2.16,16)") } });

        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch.
        EvalWithData("DOLLARDE(A1:A2,B1:B3)", (1, 1, 1.02), (2, 1, 2.16), (1, 2, 32.0), (2, 2, 16.0), (3, 2, 8.0)).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Dollarfr_InverseOfDollarde()
    {
        // DOLLARFR(1.0625, 32) = 1 + 0.0625*32/100 = 1.02
        double result = Calc("DOLLARFR(1.0625,32)");
        result.Should().BeApproximately(1.02, 0.0001);
    }

    [Fact]
    public void Dollarde_Dollarfr_RoundTrip()
    {
        double original = 1.05;
        double fraction = 16;
        double dec = Calc($"DOLLARDE({original},{fraction})");
        double back = Calc($"DOLLARFR({dec},{fraction})");
        back.Should().BeApproximately(original, 0.00001);
    }

    [Fact]
    public void Dollarde_FractionZero_ReturnsDivByZeroError()
        => CalcError("DOLLARDE(1.02,0)").Should().Be("#DIV/0!");

    [Theory]
    [InlineData("DOLLARDE(1.02,-0.5)")]
    [InlineData("DOLLARFR(1.0625,-0.5)")]
    public void DollarFractionHelpers_NegativeFractionBeforeTruncation_ReturnsNumError(string formula)
        => CalcError(formula).Should().Be("#NUM!");
}
