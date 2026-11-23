using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 8, statistical functions. Expected values were measured against Excel 16.0 (en-US) through
/// COM; the corpus lives in <see cref="ExcelParityRound8StatisticalData"/>.
/// </summary>
public class ExcelParityRound8StatisticalTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));

    public static IEnumerable<object[]> Corpus()
    {
        foreach (var line in ExcelParityRound8StatisticalData.Rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.LastIndexOf('\t');
            yield return [line[..tab], line[(tab + 1)..].TrimEnd('\r', ';')];
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void MeasuredExcelResults(string formula, string expected)
    {
        var actual = Eval("=" + formula);
        if (expected[0] == 'E')
        {
            actual.Should().BeOfType<ErrorValue>($"{formula} should be {expected}").Which.Code.Should().Be(expected[1..]);
            return;
        }

        double want = double.Parse(expected[1..], CultureInfo.InvariantCulture);
        double got = actual switch
        {
            NumberValue n => n.Value,
            DateTimeValue d => d.Value,
            _ => double.NaN,
        };
        double diff = Math.Abs(want - got);
        double mag = Math.Max(Math.Abs(want), Math.Abs(got));
        (diff < 1e-9 || diff / mag < 1e-7).Should().BeTrue($"{formula} should be {expected} but was {actual}");
    }

    [Theory]
    [InlineData("=NORM.S.INV(1E-300)", -37.0470962994)]   // deep tails keep their relative precision
    [InlineData("=NORM.INV(1E-20,0,1)", -9.2623400898)]
    [InlineData("=GAMMA.INV(1E-10,5,1)", 0.026165532816)]  // tiny probabilities no longer run away
    [InlineData("=GAMMA.INV(0.5,1E+5,1)", 99999.6666669)]
    [InlineData("=CHISQ.INV(0.5,1E+10)", 9999999999.33)]   // degrees of freedom up to 1E+10 are accepted
    [InlineData("=T.INV(0.5,10)", 0)]
    [InlineData("=T.INV.2T(1,10)", 0)]
    [InlineData("=T.DIST.RT(-1,10)", 0.829553433849)]      // a negative x is allowed in the right tail
    [InlineData("=LARGE({12,15,11,18,20,14,16,19,13,17},1.9)", 19)] // a fractional k rounds up
    [InlineData("=FISHERINV(1000)", 1)]
    [InlineData("=FACTDOUBLE(-1)", 1)]
    [InlineData("=PERMUTATIONA(0,3)", 0)]
    [InlineData("=WEIBULL.DIST(0,1,4,FALSE)", 0)]
    [InlineData("=HYPGEOM.DIST(5,4,8,20,FALSE)", 0)]       // an impossible count has probability 0
    public void Values_FollowExcel(string formula, double expected)
    {
        Eval(formula).Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, Math.Max(1e-9, Math.Abs(expected) * 1e-9));
    }

    [Theory]
    [InlineData("=GAMMA.DIST(0,1,2,FALSE)", "#NUM!")]
    [InlineData("=BETA.DIST(0,1,1,FALSE)", "#NUM!")]
    [InlineData("=BETA.INV(0,2,3)", "#NUM!")]
    [InlineData("=BINOM.INV(10,0.5,0)", "#NUM!")]
    [InlineData("=BINOM.DIST(0,5,1,FALSE)", "#NUM!")]
    [InlineData("=NEGBINOM.DIST(3,5,1,FALSE)", "#NUM!")]
    [InlineData("=T.DIST(1,10000000001,TRUE)", "#NUM!")]
    [InlineData("=COMBIN(2147483647,1)", "#NUM!")]
    [InlineData("=MULTINOMIAL(100,100)", "#NUM!")]
    [InlineData("=SUMSQ(1,\"a\")", "#VALUE!")]
    [InlineData("=Z.TEST({1},2,1)", "#DIV/0!")]
    [InlineData("=F.TEST({1,1,1},{4,5,6})", "#DIV/0!")]
    [InlineData("=T.TEST({1,2},{2},2,2)", "#DIV/0!")]
    [InlineData("=CONFIDENCE.T(0.05,2,1)", "#DIV/0!")]
    [InlineData("=CHISQ.TEST({1,2;3,4},{-1,2;3,4})", "#NUM!")]
    [InlineData("=SKEW.P(1,2)", "#DIV/0!")]
    [InlineData("=PROB({1,2,3,4},{0.1,0.2,0.3,0.4000000000001},2)", "#NUM!")]
    public void Errors_FollowExcel(string formula, string expected)
    {
        Eval(formula).Should().BeOfType<ErrorValue>().Which.Code.Should().Be(expected);
    }
}
