using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityMathTrigTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=ABS(-2.5)", 2.5)]
    [InlineData("=ACOS(0.5)", 1.0471975511965979)]
    [InlineData("=ACOSH(10)", 2.993222846126381)]
    [InlineData("=ACOT(1)", 0.7853981633974483)]
    [InlineData("=ACOT(-1)", 2.356194490192345)]
    [InlineData("=ACOTH(2)", 0.5493061443340548)]
    [InlineData("=ASIN(0.5)", 0.5235987755982989)]
    [InlineData("=ASINH(-2.5)", -1.6472311463710958)]
    [InlineData("=ATAN(1)", 0.7853981633974483)]
    [InlineData("=ATAN2(1,1)", 0.7853981633974483)]
    [InlineData("=ATANH(-0.1)", -0.10033534773107558)]
    [InlineData("=CEILING(2.3,0.5)", 2.5)]
    [InlineData("=CEILING.MATH(4.3)", 5)]
    [InlineData("=CEILING.MATH(4.3,2)", 6)]
    [InlineData("=CEILING.MATH(4.3,-2)", 6)]
    [InlineData("=CEILING.MATH(-4.3,2)", -4)]
    [InlineData("=CEILING.MATH(-4.3,2,1)", -6)]
    [InlineData("=CEILING.PRECISE(4.3,2)", 6)]
    [InlineData("=CEILING.PRECISE(-4.3,2)", -4)]
    [InlineData("=COS(0)", 1)]
    [InlineData("=COSH(4)", 27.308232836016487)]
    [InlineData("=COT(30)", -0.15611995216165922)]
    [InlineData("=COTH(1)", 1.3130352854993312)]
    [InlineData("=CSC(15)", 1.5377805615408537)]
    [InlineData("=CSCH(1)", 0.8509181282393216)]
    [InlineData("=DEGREES(PI())", 180)]
    [InlineData("=EVEN(1.2)", 2)]
    [InlineData("=EVEN(-1.2)", -2)]
    [InlineData("=EXP(1)", 2.718281828459045)]
    [InlineData("=FACT(5)", 120)]
    [InlineData("=FACTDOUBLE(6)", 48)]
    [InlineData("=FACTDOUBLE(7)", 105)]
    [InlineData("=FLOOR(2.7,0.5)", 2.5)]
    [InlineData("=FLOOR.MATH(4.3)", 4)]
    [InlineData("=FLOOR.MATH(4.3,2)", 4)]
    [InlineData("=FLOOR.MATH(4.3,-2)", 4)]
    [InlineData("=FLOOR.MATH(-4.3,2)", -6)]
    [InlineData("=FLOOR.MATH(-4.3,2,1)", -4)]
    [InlineData("=FLOOR.PRECISE(4.3,2)", 4)]
    [InlineData("=FLOOR.PRECISE(-4.3,2)", -6)]
    [InlineData("=GCD(48,18)", 6)]
    [InlineData("=INT(-1.2)", -2)]
    [InlineData("=ISO.CEILING(4.3)", 5)]
    [InlineData("=ISO.CEILING(-4.3)", -4)]
    [InlineData("=ISO.CEILING(4.3,2)", 6)]
    [InlineData("=ISO.CEILING(4.3,-2)", 6)]
    [InlineData("=ISO.CEILING(-4.3,2)", -4)]
    [InlineData("=ISO.CEILING(-4.3,-2)", -4)]
    [InlineData("=ISO.CEILING(4.3,0)", 0)]
    [InlineData("=LCM(4,6)", 12)]
    [InlineData("=LN(EXP(1))", 1)]
    [InlineData("=LOG(100,10)", 2)]
    [InlineData("=LOG10(86)", 1.9344984512435677)]
    [InlineData("=LOG10(10)", 1)]
    [InlineData("=LOG10(100000)", 5)]
    [InlineData("=MOD(-3,2)", 1)]
    [InlineData("=MROUND(7,2)", 8)]
    [InlineData("=MULTINOMIAL(2,3,4)", 1260)]
    [InlineData("=ODD(1.2)", 3)]
    [InlineData("=ODD(-1.2)", -3)]
    [InlineData("=COMBIN(1030,1)", 1030)]
    [InlineData("=COMBIN(1030,0)", 1)]
    [InlineData("=COMBINA(4,3)", 20)]
    [InlineData("=COMBINA(10,3)", 220)]
    [InlineData("=COMBINA(1030,1)", 1030)]
    [InlineData("=COMBINA(1030,0)", 1)]
    [InlineData("=PERMUT(5,2)", 20)]
    [InlineData("=PERMUTATIONA(3,2)", 9)]
    [InlineData("=PERMUTATIONA(2,2)", 4)]
    [InlineData("=PI()", 3.141592653589793)]
    [InlineData("=POWER(2,3)", 8)]
    [InlineData("=PRODUCT(2,3,4)", 24)]
    [InlineData("=QUOTIENT(7,3)", 2)]
    [InlineData("=RADIANS(180)", 3.141592653589793)]
    [InlineData("=ROUND(2.345,2)", 2.35)]
    [InlineData("=ROUNDDOWN(-2.349,2)", -2.34)]
    [InlineData("=ROUNDUP(-2.341,2)", -2.35)]
    [InlineData("=SEC(45)", 1.9035944074044246)]
    [InlineData("=SECH(0)", 1)]
    [InlineData("=SIGN(-10)", -1)]
    [InlineData("=SIN(PI()/2)", 1)]
    [InlineData("=SINH(1)", 1.1752011936438014)]
    [InlineData("=SQRT(9)", 3)]
    [InlineData("=SQRTPI(2)", 2.5066282746310002)]
    [InlineData("=SUM(1,2,3)", 6)]
    [InlineData("=SUMSQ(3,4)", 25)]
    [InlineData("=SUMX2MY2(3,4)", -7)]
    [InlineData("=SUMX2PY2(3,4)", 25)]
    [InlineData("=SUMXMY2(3,4)", 1)]
    [InlineData("=TAN(0)", 0)]
    [InlineData("=TANH(0.5)", 0.46211715726000974)]
    [InlineData("=TRUNC(-2.349,2)", -2.34)]
    public void MathTrigScalarFunctions_MatchExcelCanonicalResults(string formula, double expected)
    {
        Number(formula).Should().BeApproximately(expected, 1e-10);
    }

    [Theory]
    [InlineData("=ACOS(2)")]
    [InlineData("=ACOSH(0.999999)")]
    [InlineData("=ACOTH(1)")]
    [InlineData("=ASIN(2)")]
    [InlineData("=ATANH(1)")]
    [InlineData("=ATANH(-1)")]
    [InlineData("=CEILING(2.3,-1)")]
    [InlineData("=COMBIN(2,5)")]
    [InlineData("=COMBINA(0,1)")]
    [InlineData("=COSH(1000)")]
    [InlineData("=COTH(134217728)")]
    [InlineData("=CSCH(134217728)")]
    [InlineData("=FACTDOUBLE(-1)")]
    [InlineData("=FLOOR(2.7,-1)")]
    [InlineData("=GCD(-1,2)")]
    [InlineData("=LCM(-1,2)")]
    [InlineData("=LOG(-1)")]
    [InlineData("=LOG10(0)")]
    [InlineData("=LOG10(-1)")]
    [InlineData("=MROUND(5,-2)")]
    [InlineData("=PERMUT(0,0)")]
    [InlineData("=PERMUT(0.9,0)")]
    [InlineData("=SEC(134217728)")]
    [InlineData("=SECH(134217728)")]
    [InlineData("=SQRT(-1)")]
    public void MathTrigDomainErrors_ReturnExcelNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=COT(0)")]
    [InlineData("=COTH(0)")]
    [InlineData("=CSC(0)")]
    [InlineData("=CSCH(0)")]
    public void ReciprocalTrigZeroDenominator_ReturnsDivByZero(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("=ABS(\"x\")")]
    [InlineData("=ACOSH(\"x\")")]
    [InlineData("=ACOT(\"x\")")]
    [InlineData("=ASINH(\"x\")")]
    [InlineData("=ATANH(\"x\")")]
    [InlineData("=COMBINA(\"x\",2)")]
    [InlineData("=CEILING.MATH(\"x\")")]
    [InlineData("=COSH(\"x\")")]
    [InlineData("=FACT(\"x\")")]
    [InlineData("=FACTDOUBLE(\"x\")")]
    [InlineData("=FLOOR.MATH(\"x\")")]
    [InlineData("=ISO.CEILING(\"x\")")]
    [InlineData("=LOG10(\"x\")")]
    [InlineData("=PERMUTATIONA(\"x\",2)")]
    [InlineData("=PRODUCT(\"x\")")]
    [InlineData("=SEC(\"x\")")]
    [InlineData("=SINH(\"x\")")]
    [InlineData("=TANH(\"x\")")]
    [InlineData("=SUM(\"x\")")]
    [InlineData("=SUMXMY2(\"x\",1)")]
    public void MathTrigInvalidDirectText_ReturnsValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Combin_TruncatesArgumentsLikeExcel()
    {
        Number("=COMBIN(5.9,2.1)").Should().Be(10);
        Number("=COMBINA(4.9,3.1)").Should().Be(20);
        Number("=PERMUTATIONA(3.9,2.1)").Should().Be(9);
        Number("=FACT(170.9)").Should().Be(Number("=FACT(170)"));
        Number("=FACTDOUBLE(6.9)").Should().Be(48);
    }

    [Fact]
    public void Round_UsesExcelDecimalMidpointSemantics()
    {
        Number("=ROUND(1.005,2)").Should().Be(1.01);
        Number("=ROUND(9995,-1)").Should().Be(10000);
    }

    [Fact]
    public void Mround_UsesExcelDecimalMidpointSemantics()
    {
        Number("=MROUND(6.05,0.1)").Should().BeApproximately(6.1, 1e-12);
        Number("=MROUND(-6.05,-0.1)").Should().BeApproximately(-6.1, 1e-12);
    }

    [Fact]
    public void Power_NonFiniteArguments_ReturnExcelNumError()
    {
        _eval.Evaluate("=POWER(1,1E309)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=POWER(1E309,0)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Exp_NonFiniteArguments_ReturnExcelNumError()
    {
        _eval.Evaluate("=EXP(1E309)", MakeSheet()).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=EXP(-1E309)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void GcdAndLcm_RangeArgumentsScanAllReferencedCellsLikeExcel()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12)),
            (2, 1, new NumberValue(18)),
            (3, 1, new TextValue("ignored")),
            (4, 1, new BoolValue(true)),
            (1, 2, new NumberValue(4)),
            (2, 2, new NumberValue(6)));

        Number("=GCD(A1:A4)", sheet).Should().Be(6);
        Number("=LCM(B1:B2)", sheet).Should().Be(12);
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

        var unit = _eval.Evaluate("=MUNIT(3)", sheet).Should().BeOfType<RangeValue>().Subject;
        unit.RowCount.Should().Be(3);
        unit.ColCount.Should().Be(3);
        unit.At(1, 1).Should().Be(new NumberValue(1));
        unit.At(1, 2).Should().Be(new NumberValue(0));
        unit.At(2, 2).Should().Be(new NumberValue(1));
        unit.At(3, 3).Should().Be(new NumberValue(1));
    }

    [Theory]
    [InlineData("=MUNIT(0)")]
    [InlineData("=MUNIT(-1)")]
    [InlineData("=MUNIT(\"x\")")]
    public void Munit_InvalidDimension_ReturnsValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
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
        Number("=SUMSQ(A1:A3)", sheet).Should().Be(14);
        Number("=SUMXMY2(A1:A3,B1:B3)", sheet).Should().Be(1134);
        Number("=SUMPRODUCT(A1:A3,B1:B3)", sheet).Should().Be(140);
        Number("=SUBTOTAL(9,B1:B3)", sheet).Should().Be(60);
        Number("=AGGREGATE(9,4,B1:B3)", sheet).Should().Be(60);
    }

    [Fact]
    public void SumSq_CountsDirectLogicalValuesButIgnoresReferencedLogicalValues()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new NumberValue(2)));

        Number("=SUMSQ(TRUE,2)").Should().Be(5);
        Number("=SUMSQ(A1:A2)", sheet).Should().Be(4);
    }

    [Fact]
    public void SumXFunctions_ReturnNAForShapeMismatchAndPropagateErrors()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new NumberValue(3)),
            (2, 2, ErrorValue.NA));

        _eval.Evaluate("=SUMXMY2(A1:A2,B1:B1)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=SUMXMY2(A1:A2,B1:B2)", sheet).Should().Be(ErrorValue.NA);
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

    [Fact]
    public void IsoCeiling_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4.3)),
            (2, 1, new NumberValue(-4.3)),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(-2)));

        var result = _eval.Evaluate("=ISO.CEILING(A1:A2,B1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.At(1, 1).Should().Be(new NumberValue(6));
        result.At(2, 1).Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void ModernCeilingFloorFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4.3)),
            (2, 1, new NumberValue(-4.3)),
            (1, 2, new NumberValue(2)),
            (2, 2, new NumberValue(2)));

        var ceiling = _eval.Evaluate("=CEILING.MATH(A1:A2,B1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ceiling.At(1, 1).Should().Be(new NumberValue(6));
        ceiling.At(2, 1).Should().Be(new NumberValue(-4));

        var floor = _eval.Evaluate("=FLOOR.MATH(A1:A2,B1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        floor.At(1, 1).Should().Be(new NumberValue(4));
        floor.At(2, 1).Should().Be(new NumberValue(-6));
    }

    [Fact]
    public void CombinatoricFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),
            (2, 1, new NumberValue(10)),
            (1, 2, new NumberValue(3)),
            (2, 2, new NumberValue(3)));

        var combina = _eval.Evaluate("=COMBINA(A1:A2,B1:B2)", sheet).Should().BeOfType<RangeValue>().Subject;
        combina.At(1, 1).Should().Be(new NumberValue(20));
        combina.At(2, 1).Should().Be(new NumberValue(220));

        var permutationA = _eval.Evaluate("=PERMUTATIONA(A1:A2,2)", sheet).Should().BeOfType<RangeValue>().Subject;
        permutationA.At(1, 1).Should().Be(new NumberValue(16));
        permutationA.At(2, 1).Should().Be(new NumberValue(100));

        var factDouble = _eval.Evaluate("=FACTDOUBLE(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        factDouble.At(1, 1).Should().Be(new NumberValue(8));
        factDouble.At(2, 1).Should().Be(new NumberValue(3840));
    }

    [Fact]
    public void ReciprocalTrigFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));

        var sec = _eval.Evaluate("=SEC(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)sec.At(1, 1)).Value.Should().BeApproximately(1.0 / Math.Cos(1), 1e-10);
        ((NumberValue)sec.At(2, 1)).Value.Should().BeApproximately(1.0 / Math.Cos(2), 1e-10);

        var acot = _eval.Evaluate("=ACOT(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)acot.At(1, 1)).Value.Should().BeApproximately(Math.PI / 4.0, 1e-10);
        ((NumberValue)acot.At(2, 1)).Value.Should().BeApproximately(Math.Atan(0.5), 1e-10);

        var log10 = _eval.Evaluate("=LOG10(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)log10.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)log10.At(2, 1)).Value.Should().BeApproximately(Math.Log10(2), 1e-12);
    }

    [Fact]
    public void HyperbolicTrigFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)));

        var sinh = _eval.Evaluate("=SINH(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)sinh.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)sinh.At(2, 1)).Value.Should().BeApproximately(Math.Sinh(1), 1e-12);

        var cosh = _eval.Evaluate("=COSH(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)cosh.At(1, 1)).Value.Should().BeApproximately(1, 1e-12);
        ((NumberValue)cosh.At(2, 1)).Value.Should().BeApproximately(Math.Cosh(1), 1e-12);

        var tanh = _eval.Evaluate("=TANH(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)tanh.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)tanh.At(2, 1)).Value.Should().BeApproximately(Math.Tanh(1), 1e-12);

        var acosh = _eval.Evaluate("=ACOSH(A2:A3)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)acosh.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)acosh.At(2, 1)).Value.Should().BeApproximately(Math.Acosh(2), 1e-12);

        var asinh = _eval.Evaluate("=ASINH(A1:A2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)asinh.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)asinh.At(2, 1)).Value.Should().BeApproximately(Math.Asinh(1), 1e-12);

        var atanh = _eval.Evaluate("=ATANH(A1:A2/2)", sheet).Should().BeOfType<RangeValue>().Subject;
        ((NumberValue)atanh.At(1, 1)).Value.Should().BeApproximately(0, 1e-12);
        ((NumberValue)atanh.At(2, 1)).Value.Should().BeApproximately(Math.Atanh(0.5), 1e-12);
    }

    [Fact]
    public void SeriesSum_XNAndMRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 2, new NumberValue(0)), (2, 2, new NumberValue(1)), (3, 2, new NumberValue(2)),
            (1, 3, new NumberValue(1)), (2, 3, new NumberValue(2)),
            (1, 4, new NumberValue(1)), (2, 4, new NumberValue(3)));

        var result = _eval.Evaluate("=SERIESSUM(A1:A2,B1:B2,C1:C2,D1:D2)", sheet).Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new NumberValue(7));
        result.At(2, 1).Should().Be(new NumberValue(84));

        _eval.Evaluate("=SERIESSUM(A1:A2,B1:B3,C1:C2,D1:D2)", sheet)
            .Should().Be(ErrorValue.Value);
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
