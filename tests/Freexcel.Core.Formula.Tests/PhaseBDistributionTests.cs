using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for Phase B statistical distribution functions:
/// NORM.DIST, NORM.INV, NORM.S.DIST, NORM.S.INV, STANDARDIZE,
/// T.DIST, T.DIST.RT, T.DIST.2T, T.INV, T.INV.2T, T.TEST,
/// F.DIST, F.DIST.RT, F.INV, F.INV.RT, F.TEST,
/// CHISQ.DIST, CHISQ.DIST.RT, CHISQ.INV, CHISQ.INV.RT, CHISQ.TEST,
/// SKEW, SKEW.P, KURT, FREQUENCY, CONFIDENCE.NORM, CONFIDENCE.T,
/// BINOM.DIST, BINOM.DIST.RANGE, BINOM.INV, NEGBINOM.DIST, POISSON.DIST, HYPERGEOM.DIST,
/// EXPON.DIST, WEIBULL.DIST, GAMMA.DIST, GAMMA.INV, GAMMALN, GAMMA,
/// BETA.DIST, BETA.INV, LOGNORM.DIST, LOGNORM.INV.
/// </summary>
public class PhaseBDistributionTests
{
    private readonly FormulaEvaluator _eval = new();

    private double Calc(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    private string CalcError(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<ErrorValue>($"formula {formula} should return an error");
        return ((ErrorValue)result).Code;
    }

    private double CalcWithData(string formula, params (int row, int col, double val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    // ── NORM.DIST ────────────────────────────────────────────────────────────

    private ScalarValue Eval(string formula, Sheet sheet)
    {
        return _eval.Evaluate("=" + formula, sheet);
    }

    private static Sheet MakeSheet(params (int row, int col, double val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        return sheet;
    }

    private static void AssertColumnApproximately(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            ((NumberValue)range.Cells[row, 0]).Value.Should().BeApproximately(expected[row], 1e-6);
    }

    private static double NormSCdfForTest(double z)
        => 0.5 * (1.0 + ErfForTest(z / Math.Sqrt(2.0)));

    private static double ErfForTest(double x)
    {
        double sign = Math.Sign(x);
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        return sign * y;
    }

    [Fact]
    public void NormDist_StandardNormal_CumulativeAtZero_Returns0Point5()
        => Calc("NORM.DIST(0,0,1,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormDist_StandardNormal_CumulativeAtPositive1_Returns0Point84()
        => Calc("NORM.DIST(1,0,1,TRUE)").Should().BeApproximately(0.8413447460685429, 1e-8);

    [Fact]
    public void NormDist_StandardNormal_PdfAtZero_Returns1OverSqrt2Pi()
        => Calc("NORM.DIST(0,0,1,FALSE)").Should().BeApproximately(1.0 / Math.Sqrt(2 * Math.PI), 1e-9);

    [Fact]
    public void NormDist_NonStandard_CumulativeAtMean_Returns0Point5()
        => Calc("NORM.DIST(5,5,2,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormDist_NegativeStdev_ReturnsNum()
        => CalcError("NORM.DIST(0,0,-1,TRUE)").Should().Be("#NUM!");

    // ── NORM.INV ─────────────────────────────────────────────────────────────

    [Fact]
    public void NormInv_At0Point5_ReturnsMean()
        => Calc("NORM.INV(0.5,0,1)").Should().BeApproximately(0.0, 1e-8);

    [Fact]
    public void NormInv_At0Point84_ReturnsApprox1()
        => Calc("NORM.INV(0.8413447460685429,0,1)").Should().BeApproximately(1.0, 1e-3);

    [Fact]
    public void NormInv_At0_ReturnsNum()
        => CalcError("NORM.INV(0,0,1)").Should().Be("#NUM!");

    [Fact]
    public void NormInv_At1_ReturnsNum()
        => CalcError("NORM.INV(1,0,1)").Should().Be("#NUM!");

    // ── NORM.S.DIST ──────────────────────────────────────────────────────────

    [Fact]
    public void NormSDist_CumulativeAtZero_Returns0Point5()
        => Calc("NORM.S.DIST(0,TRUE)").Should().BeApproximately(0.5, 1e-9);

    [Fact]
    public void NormSDist_PdfAtZero_ReturnsCorrectValue()
        => Calc("NORM.S.DIST(0,FALSE)").Should().BeApproximately(0.3989422804014327, 1e-10);

    // ── NORM.S.INV ───────────────────────────────────────────────────────────

    [Fact]
    public void NormSInv_At0Point5_Returns0()
        => Calc("NORM.S.INV(0.5)").Should().BeApproximately(0.0, 1e-8);

    [Fact]
    public void NormSInv_At0Point975_Returns1Point96()
        => Calc("NORM.S.INV(0.975)").Should().BeApproximately(1.959963984540054, 1e-3);

    // ── STANDARDIZE ──────────────────────────────────────────────────────────

    [Fact]
    public void Standardize_BasicCase()
        => Calc("STANDARDIZE(6,4,2)").Should().BeApproximately(1.0, 1e-10);

    [Fact]
    public void Standardize_ZeroStdev_ReturnsNum()
        => CalcError("STANDARDIZE(5,4,0)").Should().Be("#NUM!");

    // ── T.DIST ───────────────────────────────────────────────────────────────

    [Fact]
    public void NormalDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var sheet = MakeSheet((1, 1, 0.0), (2, 1, 1.0));

        AssertColumnApproximately(Eval("NORM.S.DIST(A1:A2,TRUE)", sheet), 0.5, 0.8413447460685429);
        AssertColumnApproximately(Eval("NORM.DIST(A1:A2,0,1,TRUE)", sheet), 0.5, 0.8413447460685429);
        AssertColumnApproximately(Eval("STANDARDIZE(A1:A2,0,1)", sheet), 0.0, 1.0);

        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 0.8413447460685429));
        AssertColumnApproximately(Eval("NORM.S.INV(A1:A2)", probabilities), 0.0, 1.0);
        AssertColumnApproximately(Eval("NORM.INV(A1:A2,0,1)", probabilities), 0.0, 1.0);
    }

    [Fact]
    public void GammaAndLognormalFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 1.0), (2, 1, 2.0));

        AssertColumnApproximately(Eval("GAMMA(A1:A2)", xValues), 1.0, 1.0);
        AssertColumnApproximately(Eval("GAMMALN(A1:A2)", xValues), 0.0, 0.0);
        AssertColumnApproximately(Eval("GAMMA.DIST(A1:A2,1,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("LOGNORM.DIST(A1:A2,0,1,TRUE)", xValues), 0.5, NormSCdfForTest(Math.Log(2.0)));

        var probabilities = MakeSheet((1, 1, 0.5), (2, 1, 1.0 - Math.Exp(-2.0)));
        AssertColumnApproximately(Eval("GAMMA.INV(A1:A2,1,1)", probabilities), Math.Log(2.0), 2.0);
        AssertColumnApproximately(Eval("LOGNORM.INV(A1:A2,0,1)", MakeSheet((1, 1, 0.5), (2, 1, 0.8413447460685429))), 1.0, Math.E);
    }

    [Fact]
    public void BetaDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 0.25), (2, 1, 0.5));

        AssertColumnApproximately(Eval("BETA.DIST(A1:A2,1,1,TRUE)", xValues), 0.25, 0.5);
        AssertColumnApproximately(Eval("BETA.DIST(A1:A2,1,1,FALSE)", xValues), 1.0, 1.0);

        var probabilities = MakeSheet((1, 1, 0.25), (2, 1, 0.5));
        AssertColumnApproximately(Eval("BETA.INV(A1:A2,1,1)", probabilities), 0.25, 0.5);
    }

    [Fact]
    public void SimpleDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var xValues = MakeSheet((1, 1, 1.0), (2, 1, 2.0));

        AssertColumnApproximately(Eval("EXPON.DIST(A1:A2,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("WEIBULL.DIST(A1:A2,1,1,TRUE)", xValues), 1.0 - Math.Exp(-1.0), 1.0 - Math.Exp(-2.0));
        AssertColumnApproximately(Eval("POISSON.DIST(A1:A2,2,FALSE)", xValues), 2.0 * Math.Exp(-2.0), 2.0 * Math.Exp(-2.0));
    }

    [Fact]
    public void TDist_CumulativeAt0_Returns0Point5()
        => Calc("T.DIST(0,10,TRUE)").Should().BeApproximately(0.5, 1e-10);

    [Fact]
    public void TDist_RightTailAt0_Returns0Point5()
        => Calc("T.DIST.RT(0,10)").Should().BeApproximately(0.5, 1e-10);

    [Fact]
    public void TDist_TwoTailSymmetry()
        => Calc("T.DIST.2T(1,10)").Should().BeApproximately(2.0 * (1.0 - Calc("T.DIST(1,10,TRUE)")), 1e-10);

    [Fact]
    public void TDist_NegativeX_ReturnsNum()
        => CalcError("T.DIST.2T(-1,10)").Should().Be("#NUM!");

    // ── T.INV ────────────────────────────────────────────────────────────────

    [Fact]
    public void TDistributionFunctions_RangeFirstArgument_SpillElementwise()
    {
        var sheet = MakeSheet((1, 1, 0.0), (2, 1, 1.0));

        AssertColumnApproximately(Eval("T.DIST(A1:A2,10,TRUE)", sheet), Calc("T.DIST(0,10,TRUE)"), Calc("T.DIST(1,10,TRUE)"));
        AssertColumnApproximately(Eval("T.DIST.RT(A1:A2,10)", sheet), Calc("T.DIST.RT(0,10)"), Calc("T.DIST.RT(1,10)"));
        AssertColumnApproximately(Eval("T.DIST.2T(A1:A2,10)", sheet), Calc("T.DIST.2T(0,10)"), Calc("T.DIST.2T(1,10)"));
        AssertColumnApproximately(Eval("T.INV(A1:A2,10)", MakeSheet((1, 1, 0.5), (2, 1, 0.75))), Calc("T.INV(0.5,10)"), Calc("T.INV(0.75,10)"));
        AssertColumnApproximately(Eval("T.INV.2T(A1:A2,10)", MakeSheet((1, 1, 0.5), (2, 1, 0.25))), Calc("T.INV.2T(0.5,10)"), Calc("T.INV.2T(0.25,10)"));
    }

    [Fact]
    public void TInv_At0Point5_Returns0()
        => Calc("T.INV(0.5,10)").Should().BeApproximately(0.0, 1e-5);

    [Fact]
    public void TInv2T_At0Point05_10df_ReturnsApprox2Point228()
        => Calc("T.INV.2T(0.05,10)").Should().BeApproximately(2.228138852, 1e-4);

    // ── F.DIST ───────────────────────────────────────────────────────────────

    [Fact]
    public void FDist_CumulativeAt0_Returns0()
        => Calc("F.DIST(0,5,10,TRUE)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void FDist_RightTailComplementsCdf()
    {
        double cdf = Calc("F.DIST(2,5,10,TRUE)");
        double rt = Calc("F.DIST.RT(2,5,10)");
        (cdf + rt).Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void FInv_RoundTrip()
    {
        double x = Calc("F.INV(0.95,5,10)");
        double p = Calc($"F.DIST({x.ToString("R")},5,10,TRUE)");
        p.Should().BeApproximately(0.95, 1e-5);
    }

    [Fact]
    public void FInvRt_RoundTrip()
    {
        double x = Calc("F.INV.RT(0.05,5,10)");
        double rt = Calc($"F.DIST.RT({x.ToString("R")},5,10)");
        rt.Should().BeApproximately(0.05, 1e-5);
    }

    // ── CHISQ.DIST ───────────────────────────────────────────────────────────

    [Fact]
    public void ChiSqDist_CumulativeAt0_Returns0()
        => Calc("CHISQ.DIST(0,5,TRUE)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void ChiSqDist_RightTailComplementsCdf()
    {
        double cdf = Calc("CHISQ.DIST(5,5,TRUE)");
        double rt = Calc("CHISQ.DIST.RT(5,5)");
        (cdf + rt).Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void ChiSqInv_RoundTrip()
    {
        double x = Calc("CHISQ.INV(0.95,5)");
        double p = Calc($"CHISQ.DIST({x.ToString("R")},5,TRUE)");
        p.Should().BeApproximately(0.95, 1e-6);
    }

    [Fact]
    public void ChiSqInvRt_RoundTrip()
    {
        double x = Calc("CHISQ.INV.RT(0.05,5)");
        double rt = Calc("CHISQ.DIST.RT(11.0705,5)");
        rt.Should().BeApproximately(0.05, 1e-3);
    }

    // ── SKEW ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Skew_SymmetricData_ReturnsZero()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] vals = [-2, -1, 0, 1, 2];
        for (int i = 0; i < vals.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(vals[i]));
        var result = _eval.Evaluate("=SKEW(A1:A5)", sheet, wb);
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void Skew_KnownValues_ReturnsCorrect()
    {
        // Excel: SKEW(3,4,5,2,3,4,5,6,4,7) = 0.3595...
        double result = Calc("SKEW(3,4,5,2,3,4,5,6,4,7)");
        result.Should().BeApproximately(0.3595430714, 1e-5);
    }

    [Fact]
    public void SkewP_SymmetricPopulation_ReturnsZero()
        => Calc("SKEW.P(-2,-1,0,1,2)").Should().BeApproximately(0.0, 1e-12);

    [Fact]
    public void Skew_TooFewValues_ReturnsDivByZero()
        => CalcError("SKEW(1,2)").Should().Be("#DIV/0!");

    // ── KURT ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Kurt_KnownValues_ReturnsCorrect()
    {
        // Excel: KURT(3,4,5,2,3,4,5,6,4,7) = -0.151799...
        double result = Calc("KURT(3,4,5,2,3,4,5,6,4,7)");
        result.Should().BeApproximately(-0.1517985612, 1e-5);
    }

    [Fact]
    public void Kurt_TooFewValues_ReturnsDivByZero()
        => CalcError("KURT(1,2,3)").Should().Be("#DIV/0!");

    // ── CONFIDENCE.NORM ───────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceNorm_BasicCase()
    {
        // CONFIDENCE.NORM(0.05,2.5,50): z≈1.96, result=z*2.5/sqrt(50)
        double result = Calc("CONFIDENCE.NORM(0.05,2.5,50)");
        result.Should().BeApproximately(0.6929671390, 5e-3);
    }

    [Fact]
    public void ConfidenceNorm_MatchesExcelCachedResult()
        => Calc("CONFIDENCE.NORM(0.05,2.5,50)")
            .Should().BeApproximately(0.69295191217483865, 1e-12);

    [Fact]
    public void Confidence_LegacyAliasMatchesConfidenceNorm()
        => Calc("CONFIDENCE(0.05,2.5,50)")
            .Should().BeApproximately(Calc("CONFIDENCE.NORM(0.05,2.5,50)"), 1e-12);

    [Fact]
    public void ConfidenceNorm_InvalidAlpha_ReturnsNum()
        => CalcError("CONFIDENCE.NORM(0,2.5,50)").Should().Be("#NUM!");

    // ── CONFIDENCE.T ─────────────────────────────────────────────────────────

    [Fact]
    public void ConfidenceT_BasicCase()
    {
        // CONFIDENCE.T(0.05,2.5,10): t(9,0.975)*2.5/sqrt(10)
        double result = Calc("CONFIDENCE.T(0.05,2.5,10)");
        result.Should().BeApproximately(1.7872985, 5e-3);
    }

    // ── BINOM.DIST ────────────────────────────────────────────────────────────

    [Fact]
    public void BinomDist_Pmf_KnownCase()
    {
        // BINOM.DIST(6,10,0.5,FALSE) = C(10,6) * 0.5^10
        double result = Calc("BINOM.DIST(6,10,0.5,FALSE)");
        result.Should().BeApproximately(0.2050781250, 1e-8);
    }

    [Fact]
    public void BinomDist_Cumulative_KnownCase()
    {
        // BINOM.DIST(6,10,0.5,TRUE)
        double result = Calc("BINOM.DIST(6,10,0.5,TRUE)");
        result.Should().BeApproximately(0.828125, 1e-6);
    }

    [Fact]
    public void BinomDist_InvalidProbability_ReturnsNum()
        => CalcError("BINOM.DIST(6,10,1.5,FALSE)").Should().Be("#NUM!");

    // ── BINOM.INV ────────────────────────────────────────────────────────────

    [Fact]
    public void BinomInv_BasicCase()
    {
        // BINOM.INV(10,0.5,0.75) = 6
        double result = Calc("BINOM.INV(10,0.5,0.75)");
        result.Should().BeApproximately(6.0, 1e-10);
    }

    // ── POISSON.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void PoissonDist_Pmf_KnownCase()
    {
        // POISSON.DIST(2,5,FALSE) = e^-5 * 5^2 / 2! = 0.08422...
        double result = Calc("POISSON.DIST(2,5,FALSE)");
        result.Should().BeApproximately(0.08422433748, 1e-8);
    }

    [Fact]
    public void PoissonDist_Cumulative_KnownCase()
    {
        // POISSON.DIST(2,5,TRUE)
        double result = Calc("POISSON.DIST(2,5,TRUE)");
        result.Should().BeApproximately(0.12465201948, 1e-8);
    }

    // ── HYPERGEOM.DIST ────────────────────────────────────────────────────────

    [Fact]
    public void HypergeomDist_Pmf_KnownCase()
    {
        // HYPERGEOM.DIST(1,4,2,10,FALSE): P(X=1) when drawing 4 from pop 10 with 2 successes
        double result = Calc("HYPERGEOM.DIST(1,4,2,10,FALSE)");
        result.Should().BeApproximately(0.5333333333, 1e-6);
    }

    // ── NEGBINOM.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void NegbinomDist_Pmf_KnownCase()
    {
        // NEGBINOM.DIST(10,5,0.25,FALSE)
        double result = Calc("NEGBINOM.DIST(10,5,0.25,FALSE)");
        result.Should().BeApproximately(0.0550487637, 1e-6);
    }

    // ── EXPON.DIST ────────────────────────────────────────────────────────────

    [Fact]
    public void ExponDist_Cdf_KnownCase()
    {
        // EXPON.DIST(0.2,10,TRUE) = 1 - e^(-10*0.2) = 1 - e^-2
        double result = Calc("EXPON.DIST(0.2,10,TRUE)");
        result.Should().BeApproximately(1.0 - Math.Exp(-2.0), 1e-10);
    }

    [Fact]
    public void ExponDist_Pdf_KnownCase()
    {
        // EXPON.DIST(0.2,10,FALSE) = 10 * e^(-10*0.2)
        double result = Calc("EXPON.DIST(0.2,10,FALSE)");
        result.Should().BeApproximately(10.0 * Math.Exp(-2.0), 1e-10);
    }

    // ── WEIBULL.DIST ─────────────────────────────────────────────────────────

    [Fact]
    public void WeibullDist_Cdf_KnownCase()
    {
        // WEIBULL.DIST(105,20,100,TRUE)
        double result = Calc("WEIBULL.DIST(105,20,100,TRUE)");
        result.Should().BeApproximately(0.929581185, 1e-6);
    }

    // ── GAMMA.DIST / GAMMA.INV ────────────────────────────────────────────────

    [Fact]
    public void GammaDist_Pdf_KnownCase()
    {
        // GAMMA.DIST(10,9,1,FALSE): x^8 * e^-10 / Gamma(9) = 10^8 * e^-10 / 8!
        double expected = Math.Pow(10, 8) * Math.Exp(-10) / 40320.0;
        double result = Calc("GAMMA.DIST(10,9,1,FALSE)");
        result.Should().BeApproximately(expected, 1e-8);
    }

    [Fact]
    public void GammaInv_RoundTrip()
    {
        double x = Calc("GAMMA.INV(0.9,9,1)");
        double p = Calc($"GAMMA.DIST({x.ToString("R")},9,1,TRUE)");
        p.Should().BeApproximately(0.9, 1e-6);
    }

    [Fact]
    public void Gamma_PositiveInteger_ReturnsFactorialMinusOne()
        => Calc("GAMMA(5)").Should().BeApproximately(24.0, 1e-12);

    // ── GAMMALN ───────────────────────────────────────────────────────────────

    [Fact]
    public void GammaLn_At1_Returns0()
        => Calc("GAMMALN(1)").Should().BeApproximately(0.0, 1e-10);

    [Fact]
    public void GammaLn_At0Point5_ReturnsHalfLogPi()
        => Calc("GAMMALN(0.5)").Should().BeApproximately(0.5723649429, 1e-6);

    // ── BETA.DIST / BETA.INV ──────────────────────────────────────────────────

    [Fact]
    public void BetaDist_Cdf_MonotonicAndBounded()
    {
        // CDF should be strictly increasing: F(0.2) < F(0.5) < F(0.8)
        double p02 = Calc("BETA.DIST(0.2,8,10,TRUE)");
        double p05 = Calc("BETA.DIST(0.5,8,10,TRUE)");
        double p08 = Calc("BETA.DIST(0.8,8,10,TRUE)");
        p02.Should().BeInRange(0, 1);
        p02.Should().BeLessThan(p05);
        p05.Should().BeLessThan(p08);
        p08.Should().BeLessThan(1.0);
    }

    [Fact]
    public void BetaDist_Cdf_WithBounds()
    {
        // BETA.DIST(2,8,10,TRUE,1,3) maps x to (x-1)/(3-1)=0.5 → I_0.5(8,10)
        double result = Calc("BETA.DIST(2,8,10,TRUE,1,3)");
        double unbounded = Calc("BETA.DIST(0.5,8,10,TRUE)");
        result.Should().BeApproximately(unbounded, 1e-8);
    }

    [Fact]
    public void BetaInv_RoundTrip()
    {
        double x = Calc("BETA.INV(0.7,8,10)");
        double p = Calc($"BETA.DIST({x.ToString("R")},8,10,TRUE)");
        p.Should().BeApproximately(0.7, 1e-6);
    }

    // ── LOGNORM.DIST / LOGNORM.INV ────────────────────────────────────────────

    [Fact]
    public void LognormDist_Cdf_KnownCase()
    {
        // LOGNORM.DIST(4,3.5,1.2,TRUE)
        double result = Calc("LOGNORM.DIST(4,3.5,1.2,TRUE)");
        result.Should().BeApproximately(0.0390835, 1e-4);
    }

    [Fact]
    public void LognormInv_RoundTrip()
    {
        double x = Calc("LOGNORM.INV(0.039,3.5,1.2)");
        double p = Calc($"LOGNORM.DIST({x.ToString("R")},3.5,1.2,TRUE)");
        p.Should().BeApproximately(0.039, 1e-4);
    }

    // ── T.TEST round-trip check ──────────────────────────────────────────────

    [Fact]
    public void TTest_TwoSampleEqualVariance_ReturnsValidPValue()
    {
        // Two independent samples with some overlap → p-value in (0,1)
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] a = [2, 3, 4, 5, 6];
        double[] b = [5, 6, 7, 8, 9];
        for (int i = 0; i < a.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(a[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(b[i]));
        }
        var result = _eval.Evaluate("=T.TEST(A1:A5,B1:B5,2,2)", sheet, wb);
        result.Should().BeOfType<NumberValue>("T.TEST should return a number");
        double p = ((NumberValue)result).Value;
        p.Should().BeInRange(0, 1, "p-value must be in [0,1]");
        p.Should().BeLessThan(0.1, "means differ by 3 units — should be significant");
    }

    // ── CHISQ.TEST round-trip check ──────────────────────────────────────────

    [Fact]
    public void FTest_IdenticalSamples_ReturnsOne()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (int i = 1; i <= 4; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(i));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 2), new NumberValue(i));
        }

        var result = _eval.Evaluate("=F.TEST(A1:A4,B1:B4)", sheet, wb);

        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void VarS_AndStdevS_UseSampleStatistics()
    {
        Calc("VAR.S(1,2,3)").Should().BeApproximately(1.0, 1e-12);
        Calc("STDEV.S(1,2,3)").Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void ForecastLinear_UsesKnownYThenKnownXArgumentOrder()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (int i = 1; i <= 3; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(2 * i + 1));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 2), new NumberValue(i));
        }

        var result = _eval.Evaluate("=FORECAST.LINEAR(4,A1:A3,B1:B3)", sheet, wb);

        result.Should().Be(new NumberValue(9));
    }

    [Fact]
    public void ChiSqTest_LargeDivergence_ReturnsSmallPValue()
    {
        // Highly divergent observed vs expected → small p-value
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        double[] obs = [50, 5, 5];
        double[] exp = [20, 20, 20];
        for (int i = 0; i < obs.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(obs[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(exp[i]));
        }
        var result = _eval.Evaluate("=CHISQ.TEST(A1:A3,B1:B3)", sheet, wb);
        result.Should().BeOfType<NumberValue>("CHISQ.TEST should return a number");
        double p = ((NumberValue)result).Value;
        p.Should().BeInRange(0, 1);
        p.Should().BeLessThan(0.001, "large divergence should give very small p-value");
    }

    // ── FREQUENCY ────────────────────────────────────────────────────────────

    [Fact]
    public void Frequency_BasicCounts()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        // Data: 1,2,3,4,5,6 in A1:A6; Bins: 2,4 in B1:B2
        for (int i = 1; i <= 6; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i, 1), new NumberValue(i));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var result = _eval.Evaluate("=FREQUENCY(A1:A6,B1:B2)", sheet, wb);
        result.Should().BeOfType<RangeValue>();
        var rv = (RangeValue)result;
        // Bucket 1: <=2 → 2 items; Bucket 2: >2 and <=4 → 2 items; Bucket 3: >4 → 2 items
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        ((NumberValue)rv.At(1, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(2, 1)).Value.Should().Be(2);
        ((NumberValue)rv.At(3, 1)).Value.Should().Be(2);
    }

    // ── BINOM.DIST.RANGE ─────────────────────────────────────────────────────

    [Fact]
    public void BinomDistRange_SinglePoint_MatchesBinomDistPmf()
    {
        double point = Calc("BINOM.DIST(3,10,0.5,FALSE)");
        double range = Calc("BINOM.DIST.RANGE(10,0.5,3,3)");
        range.Should().BeApproximately(point, 1e-10);
    }

    [Fact]
    public void BinomDistRange_AllValues_Returns1()
        => Calc("BINOM.DIST.RANGE(10,0.5,0,10)").Should().BeApproximately(1.0, 1e-10);
}
