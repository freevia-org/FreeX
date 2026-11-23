using System.Globalization;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 8, financial functions. Every expected value was measured against Excel 16.0 (en-US) through
/// COM; the corpus lives in <see cref="ExcelParityRound8FinancialData"/>.
/// </summary>
public class ExcelParityRound8FinancialTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, new Sheet(SheetId.New(), "S"));

    private static bool SameResult(string expected, ScalarValue actual)
    {
        if (expected[0] == 'E') return actual is ErrorValue e && "E" + e.Code == expected;
        if (actual is not (NumberValue or DateTimeValue)) return false;
        double want = double.Parse(expected[1..], CultureInfo.InvariantCulture);
        double got = actual is NumberValue n ? n.Value : ((DateTimeValue)actual).Value;
        double diff = Math.Abs(want - got);
        double mag = Math.Max(Math.Abs(want), Math.Abs(got));
        return diff < 1e-9 || diff / mag < 1e-7;
    }

    private static IEnumerable<object[]> Rows(string rows)
    {
        foreach (var line in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.LastIndexOf('\t');
            yield return [line[..tab], line[(tab + 1)..].TrimEnd('\r', ';')];
        }
    }

    public static IEnumerable<object[]> CouponCorpus() => Rows(ExcelParityRound8FinancialData.CouponRows);

    public static IEnumerable<object[]> MiscCorpus() => Rows(ExcelParityRound8FinancialData.MiscRows);

    [Theory]
    [MemberData(nameof(CouponCorpus))]
    public void CouponFunctions_MatchExcelOverMonthEndGrid(string formula, string expected)
    {
        var actual = Eval("=" + formula);
        SameResult(expected, actual).Should().BeTrue($"{formula} should be {expected} but was {actual}");
    }

    [Theory]
    [MemberData(nameof(MiscCorpus))]
    public void MeasuredExcelResults(string formula, string expected)
    {
        var actual = Eval("=" + formula);
        SameResult(expected, actual).Should().BeTrue($"{formula} should be {expected} but was {actual}");
    }

    [Theory]
    [InlineData("=COUPDAYBS(DATE(2020,3,31),DATE(2025,8,31),2,0)", 31)]      // end day 31 only drops when the original start day was 30/31
    [InlineData("=COUPDAYSNC(DATE(2020,3,31),DATE(2025,8,31),2,0)", 149)]    // E - A, not a direct count
    [InlineData("=COUPDAYBS(DATE(2020,2,29),DATE(2025,8,31),2,0)", 0)]
    [InlineData("=COUPDAYBS(DATE(2020,1,15),DATE(2025,2,28),1,0)", 315)]     // a last-day-of-February start counts as day 30
    [InlineData("=COUPDAYS(DATE(2020,1,15),DATE(2025,2,28),1,1)", 365)]
    public void CouponDayCounts_FollowExcel(string formula, double expected)
    {
        Eval(formula).Should().BeOfType<NumberValue>().Which.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("=PRICE(DATE(2020,1,15),DATE(2025,6,30),0.05,0.06,100,2,2)", 95.4005718536)]  // DSC = E - A for every basis
    [InlineData("=PRICE(DATE(2020,1,15),DATE(2025,6,30),0.05,0.06,100,2,3)", 95.4001996652)]
    [InlineData("=PRICE(DATE(2020,1,15),DATE(2020,6,30),0.05,0.06,100,2)", 99.5483576642)]   // one coupon left: simple interest
    [InlineData("=PRICE(DATE(2020,1,15),DATE(2020,1,16),0.05,0.06,100,2)", 99.9968084023)]
    [InlineData("=YIELD(DATE(2020,1,15),DATE(2025,6,30),0.05,95,100,4)", 0.0608311021174)]
    [InlineData("=DURATION(DATE(2020,1,15),DATE(2025,6,30),0.05,0.06,1,3)", 4.76448631521)]
    [InlineData("=MDURATION(DATE(2020,1,15),DATE(2025,6,30),0.05,0.06,1,3)", 4.49479841058)]
    public void BondPricing_UsesExcelCouponTerms(string formula, double expected)
    {
        Eval(formula).Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-7);
    }

    [Theory]
    [InlineData("=ACCRINT(DATE(2020,1,1),DATE(2020,7,1),DATE(2021,4,15),0.05,1000,2,0,FALSE)", 64.4444444444)]
    [InlineData("=ACCRINT(DATE(2020,1,1),DATE(2020,7,1),DATE(2020,4,15),0.05,,2,0)", 14.4444444444)]      // par omitted is 1000
    [InlineData("=ACCRINT(DATE(2020,1,1),DATE(2020,7,1),DATE(2020,10,15),0.05,1000,4,0,FALSE)", 26.9444444444)]  // from first_interest minus one period
    [InlineData("=ACCRINT(DATE(2020,1,1),DATE(2020,7,1),DATE(2020,3,15),0.05,1000,4,0,FALSE)", -2.22222222222)]
    public void Accrint_CalcMethodFalse_AccruesFromThePeriodBeforeFirstInterest(string formula, double expected)
    {
        Eval(formula).Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("=PMT(1E-20,5,10000)", -2000)]                                // a rate too small to register is a zero rate
    [InlineData("=PMT(0.05,1E+10,1000)", -50)]                                // a huge term tends to interest-only
    [InlineData("=ISPMT(0.05,6,5,10000)", 100)]                               // the period is neither truncated nor range-checked
    [InlineData("=ISPMT(0.05,1.5,5,10000)", -350)]
    [InlineData("=IPMT(0.05,1.5,5,10000)", -455.308134983)]                   // fractional periods follow the continuous formula
    [InlineData("=PPMT(0.05,1.5,5,10000)", -1854.4398463)]
    [InlineData("=CUMIPMT(0.05/12,60,10000,1.5,12.5,0)", -417.328840799)]     // start rounds up, end rounds down
    [InlineData("=CUMPRINC(0.05/12,60,10000,1.5,12.5,0)", -1658.50686004)]
    [InlineData("=MIRR({-100,-200},0.1,0.1)", -1)]                            // no positive flow is -100%
    [InlineData("=DDB(10000,1000,5,1.5)", 3098.38667697)]                     // continuous fractional periods
    [InlineData("=DDB(10000,1000,5,0.5)", 4000)]                              // below 1 is the first period
    [InlineData("=VDB(10000,1000,5,0,5,0)", 9000)]                            // factor 0 is straight-line
    [InlineData("=VDB(10000,12000,5,0,5)", -2000)]                            // salvage above cost is allowed
    [InlineData("=DB(0,1000,5,1)", 0)]
    public void ClosedFormEdges_FollowExcel(string formula, double expected)
    {
        Eval(formula).Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(expected, 1e-8);
    }

    [Theory]
    [InlineData("=PV(-1,5,-100)", "#DIV/0!")]
    [InlineData("=NPER(-1,-100,1000)", "#NUM!")]
    [InlineData("=ISPMT(0.05,1,0,10000)", "#DIV/0!")]
    [InlineData("=EFFECT(0.05,TRUE)", "#VALUE!")]
    [InlineData("=CUMIPMT(0.05/12,60,10000,1,12,TRUE)", "#VALUE!")]
    [InlineData("=CUMIPMT(0.05/12,60,10000,0.5,12,0)", "#NUM!")]
    [InlineData("=XNPV(0,{-100,110},{43831,44197})", "#NUM!")]
    [InlineData("=XNPV(TRUE,{-100,110},{43831,44197})", "#VALUE!")]
    [InlineData("=XIRR({-100,110},{\"a\",44197})", "#VALUE!")]
    [InlineData("=FVSCHEDULE(1000,{0.05,\"a\"})", "#VALUE!")]
    [InlineData("=DDB(10000,1000,5,6)", "#NUM!")]
    [InlineData("=DB(10000,1000,5,6)", "#NUM!")]
    [InlineData("=DB(10000,1000,5,1,)", "#NUM!")]
    [InlineData("=RATE(1,0,-100,0)", "#NUM!")]
    public void Errors_FollowExcel(string formula, string expected)
    {
        Eval(formula).Should().BeOfType<ErrorValue>().Which.Code.Should().Be(expected);
    }
}
