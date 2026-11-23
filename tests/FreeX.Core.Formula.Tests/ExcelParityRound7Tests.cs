using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Excel-parity round 7. Every expected value below was measured against Excel 16.0 (build 20625, en-US) through COM
/// (the throwaway probe compared FreeX with Excel over ~5,200 formulas across date/time, text, dynamic-array and
/// statistical/financial functions and these are the distilled regressions of the clusters that were fixed).
/// </summary>
public class ExcelParityRound7Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static readonly double[] X = [12, 15, 11, 18, 20, 14, 16, 19, 13, 17];
    private static readonly double[] Y = [22, 26, 21, 30, 35, 25, 29, 33, 23, 31];

    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (var i = 0; i < X.Length; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(X[i]));
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 2), new NumberValue(Y[i]));
        }

        for (var i = 0; i < 6; i++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 14), new NumberValue(Math.Pow(2, i + 1))); // N: 2..64
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 15), new NumberValue(i + 1));             // O: 1..6
        }

        return sheet;
    }

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, MakeSheet());

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    private RangeValue Range(string formula) => Eval(formula).Should().BeOfType<RangeValue>().Subject;

    // ---------------------------------------------------------------- LINEST / LOGEST / TREND / GROWTH

    [Fact]
    public void Linest_ReturnsSlopeAndInterceptInReverseVariableOrder()
    {
        var result = Range("=LINEST(B1:B10,A1:A10)");

        (result.RowCount, result.ColCount).Should().Be((1, 2));
        Num(result.Cells[0, 0]).Should().BeApproximately(1.5696969697, 1e-9);
        Num(result.Cells[0, 1]).Should().BeApproximately(3.1696969697, 1e-9);
    }

    [Fact]
    public void Linest_WithStats_FillsTheFiveRowTable()
    {
        var result = Range("=LINEST(B1:B10,A1:A10,TRUE,TRUE)");

        (result.RowCount, result.ColCount).Should().Be((5, 2));
        Num(result.Cells[1, 0]).Should().BeApproximately(0.0889692015509, 1e-9);   // se slope
        Num(result.Cells[1, 1]).Should().BeApproximately(1.40250006343, 1e-9);     // se intercept
        Num(result.Cells[2, 0]).Should().BeApproximately(0.974943681419, 1e-9);    // r2
        Num(result.Cells[2, 1]).Should().BeApproximately(0.808102903738, 1e-9);    // se_y
        Num(result.Cells[3, 0]).Should().BeApproximately(311.280742459, 1e-6);     // F
        Num(result.Cells[3, 1]).Should().Be(8);                                    // df
        Num(result.Cells[4, 0]).Should().BeApproximately(203.275757576, 1e-8);     // ss_reg
        Num(result.Cells[4, 1]).Should().BeApproximately(5.22424242424, 1e-9);     // ss_resid
    }

    [Fact]
    public void Linest_NoIntercept_ForcesZeroAndLeavesItsStandardErrorNa()
    {
        var result = Range("=LINEST(B1:B10,A1:A10,FALSE,TRUE)");

        Num(result.Cells[0, 0]).Should().BeApproximately(1.76740442656, 1e-9);
        Num(result.Cells[0, 1]).Should().Be(0);
        result.Cells[1, 1].Should().Be(ErrorValue.NA);
        Num(result.Cells[2, 0]).Should().BeApproximately(0.998898499736, 1e-9);
        Num(result.Cells[3, 1]).Should().Be(9);
    }

    [Fact]
    public void Linest_MultipleVariables_ReturnsCoefficientsLastVariableFirst()
    {
        var result = Range("=LINEST({1;2;4;5},{1,2;2,1;3,5;4,3})");

        (result.RowCount, result.ColCount).Should().Be((1, 3));
        Num(result.Cells[0, 0]).Should().BeApproximately(0.174603174603, 1e-9);
        Num(result.Cells[0, 1]).Should().BeApproximately(1.27777777778, 1e-9);
        Num(result.Cells[0, 2]).Should().BeApproximately(-0.674603174603, 1e-9);
    }

    [Fact]
    public void Linest_ShapeAndDataErrors()
    {
        Eval("=LINEST(B1:B10,A1:A9)").Should().Be(ErrorValue.Ref);
        Eval("=LINEST({1;2;3},{1,2,3})").Should().Be(ErrorValue.Ref);
        Eval("=LINEST({1,\"a\",3},{1,2,3})").Should().Be(ErrorValue.Value);
        Eval("=LINEST(B1:B10,A1:A10,\"a\")").Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Logest_FitsAnExponentialCurve()
    {
        var result = Range("=LOGEST(N1:N6,O1:O6)");

        Num(result.Cells[0, 0]).Should().BeApproximately(2, 1e-9);
        Num(result.Cells[0, 1]).Should().BeApproximately(1, 1e-9);
        Eval("=LOGEST({1,2,0},{1,2,3})").Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Trend_PredictsForKnownAndNewX()
    {
        var known = Range("=TREND(B1:B10,A1:A10)");
        (known.RowCount, known.ColCount).Should().Be((10, 1));
        Num(known.Cells[0, 0]).Should().BeApproximately(22.0060606061, 1e-9);

        Num(Eval("=TREND(B1:B10,A1:A10,11)")).Should().BeApproximately(20.4363636364, 1e-9);
        Num(Eval("=TREND(B1:B10,A1:A10,11,FALSE)")).Should().BeApproximately(19.4414486922, 1e-9);

        var row = Range("=TREND(B1:B10,A1:A10,{11,12})");
        (row.RowCount, row.ColCount).Should().Be((1, 2));
        var column = Range("=TREND(B1:B10,A1:A10,{11;12})");
        (column.RowCount, column.ColCount).Should().Be((2, 1));
        Num(column.Cells[1, 0]).Should().BeApproximately(22.0060606061, 1e-9);

        var noX = Range("=TREND(B1:B10)");
        Num(noX.Cells[0, 0]).Should().BeApproximately(24.5818181818, 1e-9);
    }

    [Fact]
    public void Growth_PredictsOnTheLogScale()
    {
        var known = Range("=GROWTH(N1:N6,O1:O6)");
        (known.RowCount, known.ColCount).Should().Be((6, 1));
        Num(known.Cells[5, 0]).Should().BeApproximately(64, 1e-9);

        Num(Eval("=GROWTH(N1:N6,O1:O6,7)")).Should().BeApproximately(128, 1e-9);
        Eval("=GROWTH({1,2,-1},{1,2,3})").Should().Be(ErrorValue.Num);
    }

    // ---------------------------------------------------------------- dates and times

    [Theory]
    [InlineData("=TIME(12,-30,0)", 0.479166666666667)]
    [InlineData("=TIME(-1,90,0)", 0.0208333333333333)]
    [InlineData("=SECOND(0.999999)", 0)]
    [InlineData("=MINUTE(0.0006944444)", 1)]
    [InlineData("=HOUR(23.999999/24)", 0)]
    [InlineData("=HOUR(2958465.99999)", 23)]
    [InlineData("=DAYS(44000.9,44000.1)", 0)]
    [InlineData("=EDATE(44000.9,1)", 44030)]
    [InlineData("=EDATE(0,0)", 0)]
    [InlineData("=EOMONTH(0,0)", 31)]
    [InlineData("=EOMONTH(2958465,0)", 2958465)]
    [InlineData("=WORKDAY(DATE(2020,1,1),-10.9)", 43816)]
    [InlineData("=WORKDAY.INTL(DATE(2020,1,4),0,\"1111111\")", 43834)]
    [InlineData("=NETWORKDAYS.INTL(DATE(2020,1,1),DATE(2020,1,31),\"1111111\")", 0)]
    [InlineData("=DAYS360(59,61)", 3)]
    [InlineData("=DAYS360(DATE(2019,2,28),DATE(2019,2,28))", -2)]
    [InlineData("=DAYS360(DATE(2019,2,28),DATE(2020,2,29))", 359)]
    public void DateTimeFunctions_MatchExcel(string formula, double expected)
    {
        Num(Eval(formula)).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("=TIME(0,0,-0.5)")]
    [InlineData("=TIME(1,-90,0)")]
    [InlineData("=EDATE(TRUE,1)")]
    [InlineData("=WEEKNUM(DATE(2020,1,1),TRUE)")]
    [InlineData("=WORKDAY(DATE(2020,1,1),1,\"a\")")]
    public void DateTimeFunctions_RejectLikeExcel(string formula)
    {
        Eval(formula).Should().BeOfType<ErrorValue>();
    }

    [Theory]
    [InlineData("=TEXT(43831.5,\"d\")", "1")]
    [InlineData("=TEXT(43831.5,\"m\")", "1")]
    [InlineData("=TEXT(43831.5,\"y\")", "20")]
    [InlineData("=TEXT(43831.5,\"h\")", "12")]
    [InlineData("=TEXT(43831.5,\"s\")", "0")]
    [InlineData("=TEXT(\"5\",\"0.00\")", "5.00")]
    [InlineData("=TEXT(\"2020-01-15\",\"mmm d\")", "Jan 15")]
    public void Text_SingleLetterDateCodesAndNumericText_MatchExcel(string formula, string expected)
    {
        Eval(formula).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=COUPPCD(DATE(2020,1,15),DATE(2025,6,30),2,0)", 43830)]
    [InlineData("=COUPNCD(DATE(2020,1,31),DATE(2025,2,28),2)", 43890)]
    [InlineData("=COUPNUM(DATE(2020,12,30),DATE(2025,6,30),2)", 10)]
    [InlineData("=COUPDAYBS(DATE(2020,1,31),DATE(2025,2,28),2,0)", 150)]
    public void CouponDates_FollowMonthEndMaturities(string formula, double expected)
    {
        Num(Eval(formula)).Should().Be(expected);
    }

    // ---------------------------------------------------------------- text

    [Theory]
    [InlineData("=1E+15&\"\"", "1000000000000000")]
    [InlineData("=1E+19&\"\"", "10000000000000000000")]
    [InlineData("=1E+20&\"\"", "1E+20")]
    [InlineData("=1.5E-5&\"\"", "0.000015")]
    [InlineData("=1E-18&\"\"", "0.000000000000000001")]
    [InlineData("=1E-19&\"\"", "1E-19")]
    [InlineData("=-0&\"\"", "0")]
    [InlineData("=1.23456789012345E+19&\"\"", "12345678901234500000")]
    [InlineData("=(1/3)&\"\"", "0.333333333333333")]
    public void NumberToText_UsesExcelGeneralRules(string formula, string expected)
    {
        Eval(formula).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=SUBSTITUTE(\"aaa\",\"aa\",\"b\",2)", "ab")]
    [InlineData("=SUBSTITUTE(\"aaaa\",\"aa\",\"b\")", "bb")]
    [InlineData("=REPLACE(\"abcdef\",8,1,\"X\")", "abcdefX")]
    [InlineData("=TEXTBEFORE(\"aaa\",\"aa\",-1)", "a")]
    [InlineData("=TEXTAFTER(\"aaa\",\"aa\",-1)", "")]
    [InlineData("=TEXTBEFORE(\"a-b-c\",\"-\",-3,,1)", "")]
    [InlineData("=TEXTAFTER(\"a-b-c\",\"-\",-3,,1)", "a-b-c")]
    [InlineData("=UPPER(\"ß\")", "ß")]
    [InlineData("=LOWER(\"ΑΣ\")", "ας")]
    [InlineData("=PROPER(\"ΑΣ\")", "Ας")]
    [InlineData("=LOWER(\"İ\")", "i")]
    [InlineData("=UPPER(\"ı\")", "I")]
    public void TextFunctions_MatchExcel(string formula, string expected)
    {
        Eval(formula).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=TEXTBEFORE(\"\",\"-\")")]
    [InlineData("=TEXTAFTER(\"\",\"-\")")]
    [InlineData("=UNICHAR(65535)")]
    [InlineData("=UNICHAR(1114111)")]
    public void TextFunctions_ReturnNaLikeExcel(string formula)
    {
        Eval(formula).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Fixed_CapsDecimalsAndNeverPrintsNegativeZero()
    {
        Eval("=FIXED(1234.5,128)").Should().Be(ErrorValue.Value);
        Eval("=FIXED(-0,2)").Should().Be(new TextValue("0.00"));
    }

    // ---------------------------------------------------------------- dynamic arrays

    [Fact]
    public void ByRowByColAndMakeArray_AcceptSingleLetterParameterNames()
    {
        var byRow = Range("=BYROW({1,2;3,4},LAMBDA(r,SUM(r)))");
        Num(byRow.Cells[0, 0]).Should().Be(3);
        Num(byRow.Cells[1, 0]).Should().Be(7);

        var byCol = Range("=BYCOL({1,2;3,4},LAMBDA(c,SUM(c)))");
        Num(byCol.Cells[0, 0]).Should().Be(4);
        Num(byCol.Cells[0, 1]).Should().Be(6);

        var make = Range("=MAKEARRAY(2,3,LAMBDA(r,c,r*c))");
        Num(make.Cells[1, 2]).Should().Be(6);
    }

    [Fact]
    public void HigherOrderFunctions_AcceptABareFunctionName()
    {
        var byRow = Range("=BYROW({1,2;3,4},SUM)");
        Num(byRow.Cells[1, 0]).Should().Be(7);
        Num(Eval("=REDUCE(0,{1,2,3},SUM)")).Should().Be(6);
    }

    [Fact]
    public void ByRow_OfASingleColumn_UnwrapsTheOneByOneRow()
    {
        var result = Range("=BYROW({1;2;3},LAMBDA(r,r*2))");

        (result.RowCount, result.ColCount).Should().Be((3, 1));
        Num(result.Cells[2, 0]).Should().Be(6);
    }

    [Fact]
    public void Lambda_BareLetterNamesAndBracketedOptionalArguments()
    {
        Num(Eval("=LAMBDA(r,r+1)(1)")).Should().Be(2);
        Num(Eval("=LAMBDA(x,[y],x+IF(ISOMITTED(y),0,y))(1)")).Should().Be(1);
    }

    [Fact]
    public void Let_BindingErrorsOnlySurfaceWhenUsed()
    {
        Num(Eval("=LET(x,NA(),42)")).Should().Be(42);
        Num(Eval("=LET(x,NA(),IFERROR(x,0))")).Should().Be(0);
    }

    [Fact]
    public void Sort_PutsErrorsAfterEveryOtherType()
    {
        var result = Range("=SORT({#N/A;\"a\";TRUE;1;#DIV/0!})");

        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new TextValue("a"));
        result.Cells[2, 0].Should().Be(new BoolValue(true));
        result.Cells[3, 0].Should().Be(ErrorValue.DivByZero);
        result.Cells[4, 0].Should().Be(ErrorValue.NA);

        var byKey = Range("=SORTBY({1;2;3},{#N/A;1;#DIV/0!})");
        Num(byKey.Cells[0, 0]).Should().Be(2);
        Num(byKey.Cells[2, 0]).Should().Be(1);
    }

    [Fact]
    public void WrapRows_WithMoreWrapThanItemsIsOneUnpaddedRow()
    {
        var result = Range("=WRAPROWS({1,2,3},5)");

        (result.RowCount, result.ColCount).Should().Be((1, 3));
    }

    // ---------------------------------------------------------------- statistical and financial

    [Theory]
    [InlineData("=PMT(0.05,5,10000,0,2)", -2199.75998217)]
    [InlineData("=FV(0.05,5,-100,0,2)", 580.19128125)]
    [InlineData("=PV(0.05,5,-100,0,2)", 454.595050416)]
    [InlineData("=NPER(0.05,-100,1000,0,2)", 13.2532278981)]
    [InlineData("=IPMT(0.05,1,5,10000,0,2)", 0)]
    [InlineData("=IRR({-100,10,10,10})", -0.424417443832)]
    [InlineData("=IRR({-1000,3900,-5030,2145},0.405)", 0.1)]
    [InlineData("=TBILLPRICE(DATE(2020,1,15),DATE(2021,1,15),0.05)", 94.9166666667)]
    [InlineData("=SKEW({1,2,10})", 1.65231674033)]
    [InlineData("=KURT({1,2,3,10})", 3.228)]
    public void FinancialAndStatistical_MatchExcel(string formula, double expected)
    {
        Num(Eval(formula)).Should().BeApproximately(expected, 1e-6);
    }

    [Theory]
    [InlineData("=PMT(0.05,0,10000)")]
    [InlineData("=RATE(0,-100,1000)")]
    [InlineData("=TBILLPRICE(DATE(2020,1,15),DATE(2020,6,30),10)")]
    public void FinancialFunctions_ReturnNumLikeExcel(string formula)
    {
        Eval(formula).Should().Be(ErrorValue.Num);
    }
}
