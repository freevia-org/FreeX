using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityEngineeringTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=BASE(7,2)", "111")]
    [InlineData("=BASE(7,2,8)", "00000111")]
    [InlineData("=BASE(255,16,4)", "00FF")]
    [InlineData("=BASE(45745,36)", "ZAP")]
    [InlineData("=BASE(0,2,4)", "0000")]
    public void BaseFunction_ReturnsExcelText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void BaseFunction_PadsToExcelMaximumMinLength()
    {
        _eval.Evaluate("=BASE(1,2,255)", MakeSheet())
            .Should().Be(new TextValue(new string('0', 254) + "1"));
    }

    [Theory]
    [InlineData("=BASE(7.9,2)", "111")]
    [InlineData("=BASE(15,2.9,8.9)", "00001111")]
    [InlineData("=BASE(35,36.9)", "Z")]
    public void BaseFunction_TruncatesFractionalArguments(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=DECIMAL(\"FF\",16)", 255)]
    [InlineData("=DECIMAL(111,2)", 7)]
    [InlineData("=DECIMAL(\"zap\",36)", 45745)]
    [InlineData("=DECIMAL(\"00FF\",16)", 255)]
    public void DecimalFunction_ReturnsExcelNumber(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DECIMAL(\"11\",2.9)", 3)]
    [InlineData("=DECIMAL(\"Z\",36.9)", 35)]
    public void DecimalFunction_TruncatesFractionalRadix(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void BaseAndDecimalFunctions_SpillOverRanges()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(15)),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("F")));

        AssertColumn(_eval.Evaluate("=BASE(A1:A2,16)", sheet), new TextValue("A"), new TextValue("F"));
        AssertColumn(_eval.Evaluate("=DECIMAL(B1:B2,16)", sheet), new NumberValue(10), new NumberValue(15));
    }

    [Theory]
    [InlineData("=BASE(-1,2)")]
    [InlineData("=BASE(7,1)")]
    [InlineData("=BASE(7,37)")]
    [InlineData("=BASE(7,2,-1)")]
    [InlineData("=BASE(7,2,256)")]
    [InlineData("=DECIMAL(\"\",16)")]
    [InlineData("=DECIMAL(\"2\",2)")]
    [InlineData("=DECIMAL(\"FF\",1)")]
    [InlineData("=DECIMAL(\"FF\",37)")]
    public void BaseAndDecimalFunctions_InvalidArguments_ReturnNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=BIN2DEC(\"1010\")", 10)]
    [InlineData("=BIN2DEC(1010)", 10)]
    [InlineData("=BIN2DEC(\"1111111111\")", -1)]
    [InlineData("=HEX2DEC(\"ff\")", 255)]
    [InlineData("=HEX2DEC(\"FF\")", 255)]
    [InlineData("=HEX2DEC(\"FFFFFFFFFF\")", -1)]
    [InlineData("=OCT2DEC(17)", 15)]
    [InlineData("=OCT2DEC(\"17\")", 15)]
    [InlineData("=OCT2DEC(\"7777777777\")", -1)]
    public void BaseToDecimalFunctions_UseExcelSignedWidth(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DEC2BIN(10)", "1010")]
    [InlineData("=DEC2BIN(10,8)", "00001010")]
    [InlineData("=DEC2BIN(-1)", "1111111111")]
    [InlineData("=DEC2BIN(0,4)", "0000")]
    [InlineData("=DEC2HEX(255)", "FF")]
    [InlineData("=DEC2HEX(255,4)", "00FF")]
    [InlineData("=DEC2HEX(-1)", "FFFFFFFFFF")]
    [InlineData("=DEC2OCT(15)", "17")]
    [InlineData("=DEC2OCT(15,4)", "0017")]
    [InlineData("=DEC2OCT(-1)", "7777777777")]
    [InlineData("=BIN2HEX(\"1010\")", "A")]
    [InlineData("=BIN2OCT(\"1010\")", "12")]
    [InlineData("=HEX2BIN(\"F\",8)", "00001111")]
    [InlineData("=HEX2OCT(\"F\")", "17")]
    [InlineData("=OCT2BIN(\"17\")", "1111")]
    [InlineData("=OCT2HEX(\"17\")", "F")]
    [InlineData("=OCT2HEX(\"17\",4)", "000F")]
    public void BaseConversionFunctions_ReturnExcelText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=DEC2BIN(10.9)", "1010")]
    [InlineData("=DEC2HEX(31.9,4.9)", "001F")]
    [InlineData("=BIN2HEX(\"101\",4.9)", "0005")]
    public void BaseConversionFunctions_TruncateFractionalNumberAndPlaces(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void BaseConversionFunctions_RangeArguments_SpillElementwise()
    {
        var values = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(15)));

        AssertColumn(_eval.Evaluate("=DEC2BIN(A1:A2)", values), new TextValue("1010"), new TextValue("1111"));
        AssertColumn(_eval.Evaluate("=DEC2HEX(A1:A2)", values), new TextValue("A"), new TextValue("F"));
        AssertColumn(_eval.Evaluate("=DEC2OCT(A1:A2)", values), new TextValue("12"), new TextValue("17"));

        var baseText = MakeSheet(
            (1, 1, new TextValue("1010")),
            (2, 1, new TextValue("1111")),
            (1, 2, new TextValue("A")),
            (2, 2, new TextValue("F")),
            (1, 3, new TextValue("12")),
            (2, 3, new TextValue("17")));

        AssertColumn(_eval.Evaluate("=BIN2DEC(A1:A2)", baseText), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=HEX2DEC(B1:B2)", baseText), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=OCT2DEC(C1:C2)", baseText), new NumberValue(10), new NumberValue(15));

        var places = MakeSheet(
            (1, 1, new NumberValue(4)),
            (2, 1, new NumberValue(5)));

        AssertColumn(_eval.Evaluate("=DEC2BIN(3,A1:A2)", places), new TextValue("0011"), new TextValue("00011"));
        AssertColumn(_eval.Evaluate("=BIN2HEX(\"1010\",A1:A2)", places), new TextValue("000A"), new TextValue("0000A"));
    }

    [Fact]
    public void Convert_RangeNumberArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=CONVERT(A1:A2,\"m\",\"cm\")", sheet), new NumberValue(100), new NumberValue(200));
        AssertColumn(_eval.Evaluate("=CONVERT(A1:A2,\"C\",\"F\")", sheet), new NumberValue(33.8), new NumberValue(35.6));
    }

    [Fact]
    public void Convert_SameShapeUnitArguments_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new TextValue("m")),
            (2, 2, new TextValue("hr")),
            (1, 3, new TextValue("cm")),
            (2, 3, new TextValue("sec")));

        AssertColumn(_eval.Evaluate("=CONVERT(A1:A2,B1:B2,C1:C2)", sheet), new NumberValue(100), new NumberValue(7200));
    }

    [Fact]
    public void Convert_MismatchedUnitArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new TextValue("m")),
            (1, 3, new TextValue("hr")));

        _eval.Evaluate("=CONVERT(A1:A2,B1:C1,\"cm\")", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CONVERT(A1:A2,\"m\",B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=DELTA(5,4)", 0)]
    [InlineData("=DELTA(5,5)", 1)]
    [InlineData("=DELTA(0)", 1)]
    [InlineData("=GESTEP(5,4)", 1)]
    [InlineData("=GESTEP(5,5)", 1)]
    [InlineData("=GESTEP(-1)", 0)]
    public void EngineeringComparisonFunctions_ReturnExcelResults(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void EngineeringComparisonFunctions_SpillOverRanges()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=DELTA(A1:A2,B1:B2)", sheet), new NumberValue(1), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=GESTEP(A1:A2,2)", sheet), new NumberValue(0), new NumberValue(1));
    }

    [Theory]
    [InlineData("=ERF(0)", 0)]
    [InlineData("=ERF(0.745)", 0.70792892)]
    [InlineData("=ERF(1)", 0.84270079)]
    [InlineData("=ERF(-1)", -0.84270079)]
    [InlineData("=ERF(0,1)", 0.84270079)]
    [InlineData("=ERF(1,2)", 0.15262147)]
    [InlineData("=ERFC(0)", 1)]
    [InlineData("=ERFC(1)", 0.15729921)]
    [InlineData("=ERF.PRECISE(0.745)", 0.70792892)]
    [InlineData("=ERF.PRECISE(1)", 0.84270079)]
    [InlineData("=ERFC.PRECISE(1)", 0.15729921)]
    public void ErrorFunctions_ReturnExcelResults(string formula, double expected)
    {
        AssertNumberApproximately(_eval.Evaluate(formula, MakeSheet()), expected);
    }

    [Fact]
    public void ErrorFunctions_SpillOverRanges()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(1)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(2)));

        AssertColumnApproximately(_eval.Evaluate("=ERF(A1:A2)", sheet), 0, 0.84270079);
        AssertColumnApproximately(_eval.Evaluate("=ERFC(A1:A2)", sheet), 1, 0.15729921);
        AssertColumnApproximately(_eval.Evaluate("=ERF(A1:A2,B1:B2)", sheet), 0.84270079, 0.15262147);
        AssertColumnApproximately(_eval.Evaluate("=ERF.PRECISE(A1:A2)", sheet), 0, 0.84270079);
        AssertColumnApproximately(_eval.Evaluate("=ERFC.PRECISE(A1:A2)", sheet), 1, 0.15729921);
    }

    [Theory]
    [InlineData("=ERF(\"x\")")]
    [InlineData("=ERF(0,\"x\")")]
    [InlineData("=ERFC(\"x\")")]
    [InlineData("=ERF.PRECISE(\"x\")")]
    [InlineData("=ERFC.PRECISE(\"x\")")]
    public void ErrorFunctions_NonnumericArguments_ReturnValueError(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=COMPLEX(3,4)", "3+4i")]
    [InlineData("=COMPLEX(3,-4,\"j\")", "3-4j")]
    [InlineData("=COMPLEX(0,1)", "i")]
    [InlineData("=COMPLEX(0,-1)", "-i")]
    [InlineData("=COMPLEX(3,0)", "3")]
    public void Complex_ReturnsExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMREAL(\"6-9i\")", 6)]
    [InlineData("=IMAGINARY(\"3+4i\")", 4)]
    [InlineData("=IMAGINARY(\"0-j\")", -1)]
    [InlineData("=IMAGINARY(4)", 0)]
    [InlineData("=IMABS(\"5+12i\")", 13)]
    public void ComplexInspectionFunctions_ReturnExcelResults(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=IMARGUMENT(\"1+i\")", 0.7853981633974483)]
    [InlineData("=IMARGUMENT(\"1-i\")", -0.7853981633974483)]
    [InlineData("=IMARGUMENT(\"-1+i\")", 2.356194490192345)]
    [InlineData("=IMARGUMENT(\"-1-i\")", -2.356194490192345)]
    [InlineData("=IMARGUMENT(4)", 0)]
    public void ComplexArgumentFunction_ReturnsExcelRadians(string formula, double expected)
    {
        AssertNumberApproximately(_eval.Evaluate(formula, MakeSheet()), expected);
    }

    [Theory]
    [InlineData("=IMCONJUGATE(\"3+4i\")", "3-4i")]
    [InlineData("=IMSUM(\"3+6i\",\"5-2i\")", "8+4i")]
    [InlineData("=IMSUB(\"13+4i\",\"5+3i\")", "8+i")]
    [InlineData("=IMPRODUCT(\"3+4i\",\"5-3i\")", "27+11i")]
    [InlineData("=IMPRODUCT(\"1+2i\",30)", "30+60i")]
    [InlineData("=IMDIV(\"2+4i\",\"1+i\")", "3+i")]
    public void ComplexArithmeticFunctions_ReturnExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMPOWER(\"2+3i\",3)", "-46+9.00000000000001i")]
    [InlineData("=IMPOWER(\"2+3j\",3)", "-46+9.00000000000001j")]
    [InlineData("=IMPOWER(\"1+i\",2)", "2i")]
    [InlineData("=IMPOWER(4,0.5)", "2")]
    [InlineData("=IMPOWER(\"1+i\",-1)", "0.5-0.5i")]
    public void ComplexPowerFunction_ReturnsExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMEXP(0)", "1")]
    [InlineData("=IMLN(1)", "0")]
    [InlineData("=IMLOG10(100)", "2")]
    [InlineData("=IMLOG2(8)", "3")]
    public void ComplexExponentialAndLogFunctions_ReturnRealIdentities(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void ComplexExponentialAndLogFunctions_ReturnExcelComplexParts()
    {
        AssertNumberApproximately(_eval.Evaluate("=IMREAL(IMEXP(\"i\"))", MakeSheet()), Math.Cos(1.0));
        AssertNumberApproximately(_eval.Evaluate("=IMAGINARY(IMEXP(\"i\"))", MakeSheet()), Math.Sin(1.0));
        AssertNumberApproximately(_eval.Evaluate("=IMREAL(IMLN(\"1+i\"))", MakeSheet()), Math.Log(Math.Sqrt(2.0)));
        AssertNumberApproximately(_eval.Evaluate("=IMAGINARY(IMLN(\"1+i\"))", MakeSheet()), Math.PI / 4.0);
        AssertNumberApproximately(_eval.Evaluate("=IMREAL(IMLOG10(\"1+i\"))", MakeSheet()), Math.Log(Math.Sqrt(2.0)) / Math.Log(10.0));
        AssertNumberApproximately(_eval.Evaluate("=IMAGINARY(IMLOG2(\"1+i\"))", MakeSheet()), (Math.PI / 4.0) / Math.Log(2.0));
    }

    [Theory]
    [InlineData("=IMCOS(0)", "1")]
    [InlineData("=IMSIN(0)", "0")]
    [InlineData("=IMCOS(\"1+i\")", "0.833730025131149-0.988897705762865i")]
    [InlineData("=IMSIN(\"1+i\")", "1.29845758141598+0.634963914784736i")]
    public void ComplexTrigFunctions_ReturnExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMSQRT(0)", "0")]
    [InlineData("=IMSQRT(4)", "2")]
    [InlineData("=IMSQRT(-4)", "2i")]
    [InlineData("=IMSQRT(\"1+i\")", "1.09868411346781+0.455089860562227i")]
    [InlineData("=IMSQRT(\"1+j\")", "1.09868411346781+0.455089860562227j")]
    public void ComplexSquareRootFunction_ReturnsExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMCOSH(0)", "1")]
    [InlineData("=IMSINH(0)", "0")]
    [InlineData("=IMCOSH(\"4+3i\")", "-27.0349456030742+3.85115333481178i")]
    [InlineData("=IMSINH(\"4+3i\")", "-27.0168132580039+3.85373803791938i")]
    [InlineData("=IMSINH(\"4+3j\")", "-27.0168132580039+3.85373803791938j")]
    public void ComplexHyperbolicFunctions_ReturnExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMTAN(0)", "0")]
    [InlineData("=IMTAN(\"4+3i\")", "0.00490825806749606+1.00070953606723i")]
    [InlineData("=IMTAN(\"4+3j\")", "0.00490825806749606+1.00070953606723j")]
    public void ComplexTangentFunction_ReturnsExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Theory]
    [InlineData("=IMCOT(\"4+3i\")", "0.00490118239430447-0.999266927805902i")]
    [InlineData("=IMCSC(\"4+3i\")", "-0.0754898329158637+0.0648774713706355i")]
    [InlineData("=IMCSCH(\"4+3i\")", "-0.036275889628626-0.0051744731840194i")]
    [InlineData("=IMSEC(\"4+3i\")", "-0.065294027857947-0.0752249603027732i")]
    [InlineData("=IMSECH(\"4+3i\")", "-0.0362534969158689-0.00516434460775318i")]
    [InlineData("=IMSEC(\"4+3j\")", "-0.065294027857947-0.0752249603027732j")]
    public void ComplexReciprocalTrigFunctions_ReturnExcelComplexText(string formula, string expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void ComplexFunctions_HandleRangesAndErrors()
    {
        var sheet = MakeSheet(
            (1, 1, new TextValue("3+4i")),
            (2, 1, new TextValue("5-2i")),
            (3, 1, ErrorValue.NA));

        AssertColumn(_eval.Evaluate("=IMREAL(A1:A2)", sheet), new NumberValue(3), new NumberValue(5));
        AssertColumnApproximately(_eval.Evaluate("=IMARGUMENT(A1:A2)", sheet), Math.Atan2(4, 3), Math.Atan2(-2, 5));
        AssertColumn(_eval.Evaluate("=IMLOG10(A1:A2)", sheet), _eval.Evaluate("=IMLOG10(A1)", sheet), _eval.Evaluate("=IMLOG10(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMEXP(A1:A3)", sheet), _eval.Evaluate("=IMEXP(A1)", sheet), _eval.Evaluate("=IMEXP(A2)", sheet), ErrorValue.NA);
        AssertColumn(_eval.Evaluate("=IMCOS(A1:A2)", sheet), _eval.Evaluate("=IMCOS(A1)", sheet), _eval.Evaluate("=IMCOS(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMSIN(A1:A2)", sheet), _eval.Evaluate("=IMSIN(A1)", sheet), _eval.Evaluate("=IMSIN(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMSQRT(A1:A2)", sheet), _eval.Evaluate("=IMSQRT(A1)", sheet), _eval.Evaluate("=IMSQRT(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMCOSH(A1:A2)", sheet), _eval.Evaluate("=IMCOSH(A1)", sheet), _eval.Evaluate("=IMCOSH(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMSINH(A1:A2)", sheet), _eval.Evaluate("=IMSINH(A1)", sheet), _eval.Evaluate("=IMSINH(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMTAN(A1:A2)", sheet), _eval.Evaluate("=IMTAN(A1)", sheet), _eval.Evaluate("=IMTAN(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMCOT(A1:A2)", sheet), _eval.Evaluate("=IMCOT(A1)", sheet), _eval.Evaluate("=IMCOT(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMCSC(A1:A2)", sheet), _eval.Evaluate("=IMCSC(A1)", sheet), _eval.Evaluate("=IMCSC(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMSEC(A1:A2)", sheet), _eval.Evaluate("=IMSEC(A1)", sheet), _eval.Evaluate("=IMSEC(A2)", sheet));
        AssertColumn(_eval.Evaluate("=IMPOWER(A1:A2,2)", sheet), _eval.Evaluate("=IMPOWER(A1,2)", sheet), _eval.Evaluate("=IMPOWER(A2,2)", sheet));
        AssertColumn(_eval.Evaluate("=IMPOWER(A1:A2,B1:B2)", MakeSheet(
            (1, 1, new TextValue("2+3i")), (2, 1, new TextValue("1+i")),
            (1, 2, new NumberValue(3)), (2, 2, new NumberValue(-1)))),
            _eval.Evaluate("=IMPOWER(\"2+3i\",3)", sheet),
            _eval.Evaluate("=IMPOWER(\"1+i\",-1)", sheet));
        _eval.Evaluate("=IMSUM(A1:A2)", sheet).Should().Be(new TextValue("8+2i"));
        _eval.Evaluate("=IMSUM(A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Theory]
    [InlineData("=COMPLEX(1,2,\"x\")", "#VALUE!")]
    [InlineData("=COMPLEX(1E309,0)", "#NUM!")]
    [InlineData("=COMPLEX(0,1E309)", "#NUM!")]
    [InlineData("=IMREAL(1E309)", "#NUM!")]
    [InlineData("=IMREAL(\"1E309\")", "#NUM!")]
    [InlineData("=IMAGINARY(\"1E309i\")", "#NUM!")]
    [InlineData("=IMABS(\"1E309+i\")", "#NUM!")]
    [InlineData("=IMREAL(\"not complex\")", "#NUM!")]
    [InlineData("=IMARGUMENT(\"not complex\")", "#NUM!")]
    [InlineData("=IMARGUMENT(0)", "#DIV/0!")]
    [InlineData("=IMDIV(\"1+i\",\"0\")", "#NUM!")]
    [InlineData("=IMLN(\"0\")", "#NUM!")]
    [InlineData("=IMLOG10(\"0\")", "#NUM!")]
    [InlineData("=IMLOG2(\"not complex\")", "#NUM!")]
    [InlineData("=IMCOS(\"not complex\")", "#NUM!")]
    [InlineData("=IMSIN(\"not complex\")", "#NUM!")]
    [InlineData("=IMSQRT(\"not complex\")", "#NUM!")]
    [InlineData("=IMCOSH(\"not complex\")", "#NUM!")]
    [InlineData("=IMSINH(\"not complex\")", "#NUM!")]
    [InlineData("=IMTAN(\"not complex\")", "#NUM!")]
    [InlineData("=IMCOT(\"not complex\")", "#NUM!")]
    [InlineData("=IMCSC(\"not complex\")", "#NUM!")]
    [InlineData("=IMCSCH(\"not complex\")", "#NUM!")]
    [InlineData("=IMSEC(\"not complex\")", "#NUM!")]
    [InlineData("=IMSECH(\"not complex\")", "#NUM!")]
    [InlineData("=IMCOT(0)", "#NUM!")]
    [InlineData("=IMCSC(0)", "#NUM!")]
    [InlineData("=IMCSCH(0)", "#NUM!")]
    [InlineData("=IMEXP(1000)", "#NUM!")]
    [InlineData("=IMCOSH(1000)", "#NUM!")]
    [InlineData("=IMPOWER(\"not complex\",2)", "#NUM!")]
    [InlineData("=IMPOWER(0,-1)", "#NUM!")]
    public void ComplexFunctions_ReturnExcelErrors(string formula, string error)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new ErrorValue(error));
    }

    [Theory]
    [InlineData("=BIN2DEC(\"102\")")]
    [InlineData("=BIN2DEC(\"10101010101\")")]
    [InlineData("=DEC2BIN(512)")]
    [InlineData("=DEC2BIN(10,2)")]
    [InlineData("=DEC2HEX(255,-1)")]
    [InlineData("=DEC2OCT(64,1)")]
    [InlineData("=HEX2DEC(\"10000000000\")")]
    [InlineData("=DEC2HEX(549755813888)")]
    [InlineData("=OCT2DEC(\"10000000000\")")]
    [InlineData("=DEC2OCT(536870912)")]
    public void BaseConversionDomainErrors_ReturnNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=BITAND(13,25)", 9)]
    [InlineData("=BITOR(13,25)", 29)]
    [InlineData("=BITXOR(13,25)", 20)]
    [InlineData("=BITLSHIFT(4,2)", 16)]
    [InlineData("=BITLSHIFT(16,-2)", 4)]
    [InlineData("=BITRSHIFT(16,2)", 4)]
    [InlineData("=BITRSHIFT(4,-2)", 16)]
    public void BitFunctions_ReturnExcelIntegerResults(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void BitFunctions_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12)),
            (2, 1, new NumberValue(10)));

        AssertColumn(_eval.Evaluate("=BITAND(A1:A2,10)", sheet), new NumberValue(8), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=BITOR(A1:A2,5)", sheet), new NumberValue(13), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=BITXOR(A1:A2,5)", sheet), new NumberValue(9), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(A1:A2,1)", sheet), new NumberValue(24), new NumberValue(20));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(A1:A2,1)", sheet), new NumberValue(6), new NumberValue(5));

        var shifts = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)));
        AssertColumn(_eval.Evaluate("=BITAND(12,A1:A2)", shifts), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(4,A1:A2)", shifts), new NumberValue(8), new NumberValue(16));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(16,A1:A2)", shifts), new NumberValue(8), new NumberValue(4));
    }

    [Theory]
    [InlineData("=BITAND(-1,1)")]
    [InlineData("=BITOR(1.5,1)")]
    [InlineData("=BITLSHIFT(1,2.9)")]
    [InlineData("=BITLSHIFT(1,54)")]
    [InlineData("=BITLSHIFT(281474976710655,1)")]
    [InlineData("=BITRSHIFT(8,1.9)")]
    [InlineData("=BITRSHIFT(281474976710656,1)")]
    public void BitFunctions_InvalidArguments_ReturnNum(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=BIN2DEC(NA())")]
    [InlineData("=DEC2BIN(NA())")]
    [InlineData("=BITAND(NA(),1)")]
    [InlineData("=BITOR(1,NA())")]
    [InlineData("=BITLSHIFT(NA(),1)")]
    [InlineData("=BITRSHIFT(1,NA())")]
    [InlineData("=DEC2BIN(10,NA())")]
    [InlineData("=DEC2HEX(255,NA())")]
    [InlineData("=DEC2OCT(15,NA())")]
    [InlineData("=BIN2HEX(\"1010\",NA())")]
    [InlineData("=HEX2BIN(\"F\",NA())")]
    [InlineData("=OCT2HEX(\"17\",NA())")]
    [InlineData("=ERF(NA())")]
    [InlineData("=ERF(0,NA())")]
    [InlineData("=ERFC(NA())")]
    [InlineData("=ERF.PRECISE(NA())")]
    [InlineData("=ERFC.PRECISE(NA())")]
    [InlineData("=BASE(NA(),2)")]
    [InlineData("=BASE(7,NA())")]
    [InlineData("=BASE(7,2,NA())")]
    [InlineData("=DECIMAL(NA(),16)")]
    [InlineData("=DECIMAL(\"FF\",NA())")]
    [InlineData("=IMARGUMENT(NA())")]
    [InlineData("=IMEXP(NA())")]
    [InlineData("=IMLN(NA())")]
    [InlineData("=IMLOG10(NA())")]
    [InlineData("=IMLOG2(NA())")]
    [InlineData("=IMCOS(NA())")]
    [InlineData("=IMSIN(NA())")]
    [InlineData("=IMSQRT(NA())")]
    [InlineData("=IMCOSH(NA())")]
    [InlineData("=IMSINH(NA())")]
    [InlineData("=IMTAN(NA())")]
    [InlineData("=IMCOT(NA())")]
    [InlineData("=IMCSC(NA())")]
    [InlineData("=IMCSCH(NA())")]
    [InlineData("=IMSEC(NA())")]
    [InlineData("=IMSECH(NA())")]
    [InlineData("=IMPOWER(NA(),2)")]
    [InlineData("=IMPOWER(\"1+i\",NA())")]
    public void EngineeringFunctions_PropagateExcelErrors(string formula)
    {
        _eval.Evaluate(formula, MakeSheet()).Should().Be(ErrorValue.NA);
    }

    private static void AssertNumberApproximately(ScalarValue value, double expected)
    {
        var number = value.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(expected, 0.0000002);
    }

    private static void AssertColumnApproximately(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            AssertNumberApproximately(range.Cells[row, 0], expected[row]);
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.Cells[row, 0].Should().Be(expected[row]);
    }

    private static Sheet MakeSheet(params (uint Row, uint Col, ScalarValue Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
        return sheet;
    }
}
