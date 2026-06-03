using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for Phase B statistical distribution functions:
/// NORM.DIST, NORM.INV, NORM.S.DIST, NORM.S.INV, PHI, GAUSS, STANDARDIZE,
/// T.DIST, T.DIST.RT, T.DIST.2T, T.INV, T.INV.2T, T.TEST,
/// F.DIST, F.DIST.RT, F.INV, F.INV.RT, F.TEST,
/// CHISQ.DIST, CHISQ.DIST.RT, CHISQ.INV, CHISQ.INV.RT, CHISQ.TEST,
/// SKEW, SKEW.P, KURT, FREQUENCY, CONFIDENCE.NORM, CONFIDENCE.T,
/// BINOM.DIST, BINOM.DIST.RANGE, BINOM.INV, NEGBINOM.DIST, POISSON.DIST, HYPERGEOM.DIST,
/// EXPON.DIST, WEIBULL.DIST, GAMMA.DIST, GAMMA.INV, GAMMALN, GAMMA,
/// BETA.DIST, BETA.INV, LOGNORM.DIST, LOGNORM.INV.
/// </summary>
public partial class PhaseBDistributionTests
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
}
