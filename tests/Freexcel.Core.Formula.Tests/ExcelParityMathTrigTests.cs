using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityMathTrigTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=ABS(-2.5)", 2.5)]
    [InlineData("=ACOS(0.5)", 1.0471975511965979)]
    [InlineData("=ASIN(0.5)", 0.5235987755982989)]
    [InlineData("=ATAN(1)", 0.7853981633974483)]
    [InlineData("=ATAN2(1,1)", 0.7853981633974483)]
    [InlineData("=CEILING(2.3,0.5)", 2.5)]
    [InlineData("=COS(0)", 1)]
    [InlineData("=DEGREES(PI())", 180)]
    [InlineData("=EVEN(1.2)", 2)]
    [InlineData("=EVEN(-1.2)", -2)]
    [InlineData("=EXP(1)", 2.718281828459045)]
    [InlineData("=FACT(5)", 120)]
    [InlineData("=FLOOR(2.7,0.5)", 2.5)]
    [InlineData("=GCD(48,18)", 6)]
    [InlineData("=INT(-1.2)", -2)]
    [InlineData("=LCM(4,6)", 12)]
    [InlineData("=LN(EXP(1))", 1)]
    [InlineData("=LOG(100,10)", 2)]
    [InlineData("=MOD(-3,2)", 1)]
    [InlineData("=MROUND(7,2)", 8)]
    [InlineData("=MULTINOMIAL(2,3,4)", 1260)]
    [InlineData("=ODD(1.2)", 3)]
    [InlineData("=ODD(-1.2)", -3)]
    [InlineData("=PERMUT(5,2)", 20)]
    [InlineData("=PI()", 3.141592653589793)]
    [InlineData("=POWER(2,3)", 8)]
    [InlineData("=PRODUCT(2,3,4)", 24)]
    [InlineData("=QUOTIENT(7,3)", 2)]
    [InlineData("=RADIANS(180)", 3.141592653589793)]
    [InlineData("=ROUND(2.345,2)", 2.35)]
    [InlineData("=ROUNDDOWN(-2.349,2)", -2.34)]
    [InlineData("=ROUNDUP(-2.341,2)", -2.35)]
    [InlineData("=SIGN(-10)", -1)]
    [InlineData("=SIN(PI()/2)", 1)]
    [InlineData("=SQRT(9)", 3)]
    [InlineData("=SQRTPI(2)", 2.5066282746310002)]
    [InlineData("=SUM(1,2,3)", 6)]
    [InlineData("=TAN(0)", 0)]
    [InlineData("=TRUNC(-2.349,2)", -2.34)]
    public void MathTrigScalarFunctions_MatchExcelCanonicalResults(string formula, double expected)
    {
        Number(formula).Should().BeApproximately(expected, 1e-10);
    }

    [Theory]
    [InlineData("=ACOS(2)")]
    [InlineData("=ASIN(2)")]
    [InlineData("=CEILING(2.3,-1)")]
    [InlineData("=COMBIN(2,5)")]
    [InlineData("=FLOOR(2.7,-1)")]
    [InlineData("=GCD(-1,2)")]
    [InlineData("=LCM(-1,2)")]
    [InlineData("=LOG(-1)")]
    [InlineData("=MROUND(5,-2)")]
    [InlineData("=SQRT(-1)")]
    public void MathTrigDomainErrors_ReturnExcelNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=ABS(\"x\")")]
    [InlineData("=FACT(\"x\")")]
    [InlineData("=PRODUCT(\"x\")")]
    [InlineData("=SUM(\"x\")")]
    public void MathTrigInvalidDirectText_ReturnsValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Combin_TruncatesArgumentsLikeExcel()
    {
        Number("=COMBIN(5.9,2.1)").Should().Be(10);
    }

    [Fact]
    public void Convert_UsesExcelUnitCategoriesAndPrefixes()
    {
        Number("=CONVERT(1,\"kg\",\"g\")").Should().Be(1000);
        Number("=CONVERT(1,\"m\",\"cm\")").Should().Be(100);
        Number("=CONVERT(32,\"F\",\"C\")").Should().BeApproximately(0, 1e-10);
        _eval.Evaluate("=CONVERT(1,\"kg\",\"m\")", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void MatrixMathFunctions_ReturnExcelShapeAndValues()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)),
            (1, 4, new NumberValue(5)), (1, 5, new NumberValue(6)),
            (2, 4, new NumberValue(7)), (2, 5, new NumberValue(8)));

        Number("=MDETERM(A1:B2)", sheet).Should().BeApproximately(-2, 1e-10);

        var product = _eval.Evaluate("=MMULT(A1:B2,D1:E2)", sheet).Should().BeOfType<RangeValue>().Subject;
        product.At(1, 1).Should().Be(new NumberValue(19));
        product.At(1, 2).Should().Be(new NumberValue(22));
        product.At(2, 1).Should().Be(new NumberValue(43));
        product.At(2, 2).Should().Be(new NumberValue(50));

        var inverse = _eval.Evaluate("=MINVERSE(A1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)inverse.At(1, 1)).Value.Should().BeApproximately(-2, 1e-10);
        ((NumberValue)inverse.At(1, 2)).Value.Should().BeApproximately(1, 1e-10);
        ((NumberValue)inverse.At(2, 1)).Value.Should().BeApproximately(1.5, 1e-10);
        ((NumberValue)inverse.At(2, 2)).Value.Should().BeApproximately(-0.5, 1e-10);
    }

    [Fact]
    public void RandFunctions_ReturnValuesWithinExcelBounds()
    {
        Number("=RAND()").Should().BeInRange(0, 1);
        Number("=RANDBETWEEN(3,5)").Should().BeInRange(3, 5);
    }

    [Fact]
    public void SeriesSum_SumIfs_SumProduct_Subtotal_Aggregate_CoverExcelRangeSemantics()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)), (1, 3, new TextValue("A")),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)), (2, 3, new TextValue("B")),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)), (3, 3, new TextValue("A")));

        Number("=SERIESSUM(2,0,1,A1:A3)", sheet).Should().Be(17);
        Number("=SUMIF(C1:C3,\"A\",B1:B3)", sheet).Should().Be(40);
        Number("=SUMIFS(B1:B3,C1:C3,\"A\",A1:A3,\">1\")", sheet).Should().Be(30);
        Number("=SUMPRODUCT(A1:A3,B1:B3)", sheet).Should().Be(140);
        Number("=SUBTOTAL(9,B1:B3)", sheet).Should().Be(60);
        Number("=AGGREGATE(9,4,B1:B3)", sheet).Should().Be(60);
    }

    [Fact]
    public void MathPhaseA1Functions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 3, new NumberValue(1)), (2, 3, new NumberValue(3)));

        var sqrtpi = _eval.Evaluate("=SQRTPI(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)sqrtpi.At(1, 1)).Value.Should().BeApproximately(Math.Sqrt(2 * Math.PI), 1e-10);
        ((NumberValue)sqrtpi.At(2, 1)).Value.Should().BeApproximately(Math.Sqrt(3 * Math.PI), 1e-10);

        var series = _eval.Evaluate("=SERIESSUM(A1:A2,0,1,C1:C2)", sheet).Should().BeOfType<RangeValue>().Subject;
        series.At(1, 1).Should().Be(new NumberValue(7));
        series.At(2, 1).Should().Be(new NumberValue(10));
    }

    private double Number(string formula) => Number(formula, MakeSheet());

    private double Number(string formula, Sheet sheet)
    {
        var value = _eval.Evaluate(formula, sheet);
        value.Should().BeOfType<NumberValue>(formula);
        return ((NumberValue)value).Value;
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
