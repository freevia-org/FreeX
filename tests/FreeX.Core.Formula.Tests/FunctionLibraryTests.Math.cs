using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Mod_BasicModulo()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MOD(10,3)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Mod_DivByZero_ReturnsError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=MOD(10,0)", sheet).Should().Be(ErrorValue.DivByZero);
    }


    [Fact]
    public void Mod_OverflowingIntermediate_ReturnsNumError()
    {
        _eval.Evaluate("=MOD(1E308,1E-308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Power_SquaresNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(3,2)", sheet).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Power_NegativeBaseFractionalExponent_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(-1,0.5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Power_ZeroNegativeExponent_ReturnsDivByZeroError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(0,-1)", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Power_ExponentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=POWER(2,NA())", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void Sqrt_PositiveNumber()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SQRT(9)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sqrt_NegativeNumber_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SQRT(-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sqrt_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SQRT(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void UnaryMath_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-4)),
            (2, 1, new NumberValue(9)));

        AssertColumn(_eval.Evaluate("=ABS(A1:A2)", sheet), new NumberValue(4), new NumberValue(9));
        AssertColumn(_eval.Evaluate("=SQRT(A1:A2)", sheet), ErrorValue.Num, new NumberValue(3));
        AssertColumn(_eval.Evaluate("=INT(A1:A2)", sheet), new NumberValue(-4), new NumberValue(9));
        AssertColumn(_eval.Evaluate("=SIGN(A1:A2)", sheet), new NumberValue(-1), new NumberValue(1));
    }


    [Fact]
    public void BinaryMath_RangeNumberArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),
            (2, 1, new NumberValue(9)));

        AssertColumn(_eval.Evaluate("=POWER(A1:A2,2)", sheet), new NumberValue(16), new NumberValue(81));
        AssertColumn(_eval.Evaluate("=MOD(A1:A2,2)", sheet), new NumberValue(0), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=LOG(A1:A2,2)", sheet), new NumberValue(2), new NumberValue(Math.Log(9) / Math.Log(2)));
        AssertColumn(_eval.Evaluate("=QUOTIENT(A1:A2,2)", sheet), new NumberValue(2), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=CEILING(A1:A2,5)", sheet), new NumberValue(5), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=FLOOR(A1:A2,5)", sheet), new NumberValue(0), new NumberValue(5));
        AssertColumn(_eval.Evaluate("=MROUND(A1:A2,5)", sheet), new NumberValue(5), new NumberValue(10));
    }

    [Fact]
    public void BinaryMath_RangeSecondArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=POWER(2,A1:A2)", sheet), new NumberValue(4), new NumberValue(16));
        AssertColumn(_eval.Evaluate("=MOD(10,A1:A2)", sheet), new NumberValue(0), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=LOG(16,A1:A2)", sheet), new NumberValue(4), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=QUOTIENT(10,A1:A2)", sheet), new NumberValue(5), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=CEILING(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=FLOOR(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=MROUND(10,A1:A2)", sheet), new NumberValue(10), new NumberValue(12));
    }

    [Fact]
    public void BinaryMath_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),  (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)),  (2, 2, new NumberValue(4)));

        AssertColumn(_eval.Evaluate("=POWER(A1:A2,B1:B2)", sheet), new NumberValue(16), new NumberValue(6561));
        AssertColumn(_eval.Evaluate("=MOD(A1:A2,B1:B2)", sheet), new NumberValue(0), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=LOG(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(Math.Log(9) / Math.Log(4)));
        AssertColumn(_eval.Evaluate("=QUOTIENT(A1:A2,B1:B2)", sheet), new NumberValue(2), new NumberValue(2));
        AssertColumn(_eval.Evaluate("=CEILING(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=FLOOR(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(8));
        AssertColumn(_eval.Evaluate("=MROUND(A1:A2,B1:B2)", sheet), new NumberValue(4), new NumberValue(8));
    }

    [Fact]
    public void BinaryMath_OneCellRangeArgument_BroadcastsAcrossOtherRange()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)), (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)));

        AssertColumn(_eval.Evaluate("=MOD(A1:A2,B1:B1)", sheet), new NumberValue(0), new NumberValue(1));
    }

    [Fact]
    public void BinaryMath_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(4)),  (2, 1, new NumberValue(9)),
            (1, 2, new NumberValue(2)),  (1, 3, new NumberValue(4)));

        _eval.Evaluate("=POWER(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MOD(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=LOG(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=QUOTIENT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CEILING(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=FLOOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=MROUND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Int_TruncatesDown()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=INT(3.9)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Int_NegativeFloorTowardNegInfinity()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=INT(-3.1)", sheet).Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void Abs_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ABS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Int_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=INT(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Round_NegativeDigits_RoundsLeftOfDecimal()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=ROUND(1234,-2)", sheet).Should().Be(new NumberValue(1200));
    }

    [Fact]
    public void Round_ExcessiveDigits_ClampsLikeExcel()
    {
        _eval.Evaluate("=ROUND(1.2345,16)", MakeSheet()).Should().Be(new NumberValue(1.2345));
        _eval.Evaluate("=ROUND(12345,-16)", MakeSheet()).Should().Be(new NumberValue(0));
        _eval.Evaluate("=ROUND(1,309)", MakeSheet()).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Rounding_RangeNumberArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1.25)),
            (2, 1, new NumberValue(-1.25)));

        AssertColumn(_eval.Evaluate("=ROUND(A1:A2,1)", sheet), new NumberValue(1.3), new NumberValue(-1.3));
        AssertColumn(_eval.Evaluate("=ROUNDUP(A1:A2,1)", sheet), new NumberValue(1.3), new NumberValue(-1.3));
        AssertColumn(_eval.Evaluate("=ROUNDDOWN(A1:A2,1)", sheet), new NumberValue(1.2), new NumberValue(-1.2));
        AssertColumn(_eval.Evaluate("=TRUNC(A1:A2,1)", sheet), new NumberValue(1.2), new NumberValue(-1.2));
    }


    [Fact]
    public void Rounding_SameShapeDigitsArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12.345)),
            (2, 1, new NumberValue(-12.345)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(-1)));

        AssertColumn(_eval.Evaluate("=ROUND(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
        AssertColumn(_eval.Evaluate("=ROUNDUP(A1:A2,B1:B2)", sheet), new NumberValue(12.4), new NumberValue(-20));
        AssertColumn(_eval.Evaluate("=ROUNDDOWN(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
        AssertColumn(_eval.Evaluate("=TRUNC(A1:A2,B1:B2)", sheet), new NumberValue(12.3), new NumberValue(-10));
    }

    [Fact]
    public void Rounding_MismatchedDigitsArgument_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(12.345)),
            (2, 1, new NumberValue(-12.345)),
            (1, 2, new NumberValue(1)),
            (1, 3, new NumberValue(-1)));

        _eval.Evaluate("=ROUND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=ROUNDUP(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=ROUNDDOWN(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=TRUNC(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Round_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUND(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_RoundsUp()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,1)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Ceiling_WithSignificance()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(4.1,0.5)", sheet).Should().Be(new NumberValue(4.5));
    }

    [Fact]
    public void Ceiling_PositiveNumberNegativeSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Ceiling_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=CEILING(2.3,NA())", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void Ceiling_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=CEILING(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_RoundsDown()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,1)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Floor_WithSignificance()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(4.9,0.5)", sheet).Should().Be(new NumberValue(4.5));
    }

    [Fact]
    public void Floor_PositiveNumberNegativeSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_NegativeNumberPositiveSignificance_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(-2.9,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Floor_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FLOOR(2.9,NA())", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void Floor_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=FLOOR(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Randbetween_InRange()
    {
        var sheet = MakeSheet();
        for (int i = 0; i < 20; i++)
        {
            var result = _eval.Evaluate("=RANDBETWEEN(1,10)", sheet);
            result.Should().BeOfType<NumberValue>();
            var n = ((NumberValue)result).Value;
            n.Should().BeGreaterThanOrEqualTo(1).And.BeLessThanOrEqualTo(10);
        }
    }

    [Fact]
    public void Randbetween_SameShapeBottomAndTopRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(5)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(6)));

        var result = _eval.Evaluate("=RANDBETWEEN(A1:A2,B1:B2)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        ((NumberValue)result.At(1, 1)).Value.Should().Be(1);
        ((NumberValue)result.At(2, 1)).Value.Should().BeGreaterThanOrEqualTo(5).And.BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void Randbetween_IntegerRangeOverflow_ReturnsNumError()
    {
        _eval.Evaluate("=RANDBETWEEN(-9223372036854775808,9223372036854775807)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Randarray_ReturnsRequestedShapeWithinBounds()
    {
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=RANDARRAY(2,3,5,6)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        foreach (var value in rv.Cells)
        {
            value.Should().BeOfType<NumberValue>();
            ((NumberValue)value).Value.Should().BeGreaterThanOrEqualTo(5).And.BeLessThan(6);
        }
    }

    [Fact]
    public void Randarray_WholeNumber_ReturnsIntegersWithinInclusiveBounds()
    {
        var sheet = MakeSheet();

        var result = _eval.Evaluate("=RANDARRAY(2,2,1,3,TRUE)", sheet);

        var rv = (RangeValue)result;
        foreach (var value in rv.Cells)
        {
            value.Should().BeOfType<NumberValue>();
            var number = ((NumberValue)value).Value;
            number.Should().BeOneOf(1, 2, 3);
            number.Should().Be(Math.Truncate(number));
        }
    }

    [Fact]
    public void Randarray_AcceptsSpilledScalarControlArguments()
    {
        var result = _eval.Evaluate("=RANDARRAY(SEQUENCE(1,,2),SEQUENCE(1,,2),SEQUENCE(1,,5),SEQUENCE(1,,7),SEQUENCE(1,,TRUE))", MakeSheet());

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        foreach (var value in rv.Cells)
        {
            var number = value.Should().BeOfType<NumberValue>().Subject.Value;
            number.Should().Be(Math.Truncate(number));
            number.Should().BeInRange(5, 7);
        }
    }

    [Fact]
    public void Randarray_WholeNumberIntegerRangeOverflow_ReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(1,1,-9223372036854775808,9223372036854775807,TRUE)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_InvalidDimensions_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=RANDARRAY(0,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_NonFiniteRows_ReturnsValueError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=RANDARRAY(A1,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_MinGreaterThanMax_ReturnsValueError()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=RANDARRAY(1,1,10,1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_OverflowingDecimalRange_ReturnsValueError()
    {
        _eval.Evaluate("=RANDARRAY(1,1,-1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Randarray_IsVolatile()
    {
        BuiltInFunctions.IsVolatile("RANDARRAY").Should().BeTrue();
    }


    [Fact]
    public void Sign_Positive_Returns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(5)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sign_Zero_Returns0()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(0)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sign_Negative_ReturnsMinus1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=SIGN(-7)", sheet).Should().Be(new NumberValue(-1));
    }


    [Fact]
    public void Sign_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SIGN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_Base10()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LOG(100,10)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Log_DefaultBase10()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LOG(1000)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(3, 1e-10);
    }


    [Fact]
    public void Log_OmittedBase_DefaultsTo10()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LOG(1000,)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(3, 1e-10);
    }

    [Fact]
    public void Log_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LOG(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_NonFiniteBase_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LOG(100,A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Log_BaseError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LOG(100,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Ln_NaturalLog()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=LN(1)", sheet);
        result.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Ln_NegativeOrZero_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=LN(0)", sheet).Should().Be(ErrorValue.Num);
    }


    [Fact]
    public void Ln_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=LN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Exp_ZeroReturns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Exp_OneReturnsE()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=EXP(1)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(Math.E, 1e-10);
    }

    [Fact]
    public void Exp_Overflow_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(1000)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Exp_ArgumentError_PropagatesError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=EXP(NA())", sheet).Should().Be(ErrorValue.NA);
    }


    [Fact]
    public void Pi_ReturnsPi()
    {
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=PI()", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(Math.PI, 1e-10);
    }


    [Fact]
    public void Fact_Factorial5()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(5)", sheet).Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Fact_Zero_Returns1()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Fact_Decimal_TruncatesArgument()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(5.9)", sheet).Should().Be(new NumberValue(120));
    }

    [Fact]
    public void Fact_Negative_ReturnsNumError()
    {
        var sheet = MakeSheet();
        _eval.Evaluate("=FACT(-1)", sheet).Should().Be(ErrorValue.Num);
    }


    [Fact] public void Sin_Zero_ReturnsZero() =>
        _eval.Evaluate("=SIN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Sin_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=SIN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Cos_Zero_ReturnsOne() =>
        _eval.Evaluate("=COS(0)", MakeSheet()).Should().Be(new NumberValue(1));

    [Fact]
    public void Cos_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=COS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Tan_Zero_ReturnsZero() =>
        _eval.Evaluate("=TAN(0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Tan_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=TAN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void UnaryTrig_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(0)));

        AssertColumn(_eval.Evaluate("=SIN(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=COS(A1:A2)", sheet), new NumberValue(1), new NumberValue(1));
        AssertColumn(_eval.Evaluate("=TAN(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=DEGREES(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=RADIANS(A1:A2)", sheet), new NumberValue(0), new NumberValue(0));
    }

    [Fact]
    public void AdditionalUnaryMath_RangeArgument_SpillsElementwise()
    {
        var zeros = MakeSheet(
            (1, 1, new NumberValue(0)),
            (2, 1, new NumberValue(0)));
        AssertColumn(_eval.Evaluate("=ASIN(A1:A2)", zeros), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=ATAN(A1:A2)", zeros), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=EXP(A1:A2)", zeros), new NumberValue(1), new NumberValue(1));

        var ones = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(1)));
        AssertColumn(_eval.Evaluate("=ACOS(A1:A2)", ones), new NumberValue(0), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=LN(A1:A2)", ones), new NumberValue(0), new NumberValue(0));

        var facts = MakeSheet(
            (1, 1, new NumberValue(3)),
            (2, 1, new NumberValue(-1)));
        AssertColumn(_eval.Evaluate("=FACT(A1:A2)", facts), new NumberValue(6), ErrorValue.Num);
    }

    [Fact] public void Asin_One_ReturnsHalfPi() =>
        ((NumberValue)_eval.Evaluate("=ASIN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 2, 1e-10);

    [Fact]
    public void Asin_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ASIN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Acos_One_ReturnsZero() =>
        ((NumberValue)_eval.Evaluate("=ACOS(1)", MakeSheet())).Value
            .Should().BeApproximately(0, 1e-10);

    [Fact]
    public void Acos_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ACOS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Atan_One_ReturnsQuarterPi() =>
        ((NumberValue)_eval.Evaluate("=ATAN(1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact]
    public void Atan_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ATAN(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Atan2_XY_ReturnsCorrect() =>
        ((NumberValue)_eval.Evaluate("=ATAN2(1,1)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI / 4, 1e-10);

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_RangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=ATAN2(1,A1:A2)", sheet), new NumberValue(Math.Atan2(2, 1)), new NumberValue(Math.Atan2(3, 1)));
        AssertColumn(_eval.Evaluate("=ATAN2(A1:A2,1)", sheet), new NumberValue(Math.Atan2(1, 2)), new NumberValue(Math.Atan2(1, 3)));
        AssertColumn(_eval.Evaluate("=COMBIN(5,A1:A2)", sheet), new NumberValue(10), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=PERMUT(5,A1:A2)", sheet), new NumberValue(20), new NumberValue(60));

        var numbers = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(6)));
        AssertColumn(_eval.Evaluate("=COMBIN(A1:A2,2)", numbers), new NumberValue(10), new NumberValue(15));
        AssertColumn(_eval.Evaluate("=PERMUT(A1:A2,2)", numbers), new NumberValue(20), new NumberValue(30));
    }

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 2, new NumberValue(4)), (2, 2, new NumberValue(5)));

        AssertColumn(_eval.Evaluate("=ATAN2(A1:A2,B1:B2)", sheet), new NumberValue(Math.Atan2(4, 2)), new NumberValue(Math.Atan2(5, 3)));
        AssertColumn(_eval.Evaluate("=COMBIN(B1:B2,A1:A2)", sheet), new NumberValue(6), new NumberValue(10));
        AssertColumn(_eval.Evaluate("=PERMUT(B1:B2,A1:A2)", sheet), new NumberValue(12), new NumberValue(60));
    }

    [Fact]
    public void TwoArgumentCombinatoricsAndTrig_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)), (2, 1, new NumberValue(3)),
            (1, 2, new NumberValue(4)), (1, 3, new NumberValue(5)));

        _eval.Evaluate("=ATAN2(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=COMBIN(B1:C1,A1:A2)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=PERMUT(B1:C1,A1:A2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Atan2_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ATAN2(A1,1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Degrees_Pi_Returns180() =>
        ((NumberValue)_eval.Evaluate("=DEGREES(PI())", MakeSheet())).Value
            .Should().BeApproximately(180, 1e-10);

    [Fact]
    public void Degrees_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=DEGREES(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Radians_180_ReturnsPi() =>
        ((NumberValue)_eval.Evaluate("=RADIANS(180)", MakeSheet())).Value
            .Should().BeApproximately(Math.PI, 1e-10);

    [Fact]
    public void Radians_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=RADIANS(A1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Sum_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=SUM(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Average_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=AVERAGE(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Min_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MIN(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Max_NonFiniteDirectNumericText_ReturnsNumError()
    {
        _eval.Evaluate("=MAX(\"1E309\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Product_Range_MultipliesAll()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)),(2,1,new NumberValue(3)),(3,1,new NumberValue(4)));
        _eval.Evaluate("=PRODUCT(A1:A3)", sheet).Should().Be(new NumberValue(24));
    }

    [Fact] public void Product_DirectTrue_MultipliesAsOne() =>
        _eval.Evaluate("=PRODUCT(TRUE,2)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact] public void Product_DirectFalse_MultipliesAsZero() =>
        _eval.Evaluate("=PRODUCT(FALSE,2)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Product_DirectTodayResult_MultipliesDateSerial()
    {
        _eval.Evaluate("=PRODUCT(TODAY(),2)", MakeSheet())
            .Should().Be(new NumberValue(DateTime.Today.ToOADate() * 2));
    }

    [Fact] public void Product_RangeFalse_IgnoresLogicalValue()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(false)),
            (2, 1, new NumberValue(2)));
        _eval.Evaluate("=PRODUCT(A1:A2)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Product_OverflowingProduct_ReturnsNumError()
    {
        _eval.Evaluate("=PRODUCT(1E308,1E308)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Quotient_5_2_Returns2() =>
        _eval.Evaluate("=QUOTIENT(5,2)", MakeSheet()).Should().Be(new NumberValue(2));

    [Fact]
    public void Quotient_NonFiniteNumerator_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));
        _eval.Evaluate("=QUOTIENT(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Gcd_12_8_Returns4() =>
        _eval.Evaluate("=GCD(12,8)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact] public void Gcd_DirectNumericText_CoercesValue() =>
        _eval.Evaluate("=GCD(\"6\",9)", MakeSheet()).Should().Be(new NumberValue(3));

    [Fact] public void Gcd_ReferencedLogicalAndText_IgnoresValues()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new TextValue("6")),
            (3, 1, new NumberValue(9)));
        _eval.Evaluate("=GCD(A1:A3)", sheet).Should().Be(new NumberValue(9));
    }

    [Fact] public void Gcd_NegativeArgument_ReturnsNumError() =>
        _eval.Evaluate("=GCD(-12,8)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Lcm_4_6_Returns12() =>
        _eval.Evaluate("=LCM(4,6)", MakeSheet()).Should().Be(new NumberValue(12));

    [Fact] public void Lcm_DirectNumericText_CoercesValue() =>
        _eval.Evaluate("=LCM(\"6\",8)", MakeSheet()).Should().Be(new NumberValue(24));

    [Fact] public void Lcm_ReferencedLogicalAndText_IgnoresValues()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new TextValue("6")),
            (3, 1, new NumberValue(8)));
        _eval.Evaluate("=LCM(A1:A3)", sheet).Should().Be(new NumberValue(8));
    }

    [Fact] public void Lcm_NegativeArgument_ReturnsNumError() =>
        _eval.Evaluate("=LCM(-4,6)", MakeSheet()).Should().Be(ErrorValue.Num);

    [Fact] public void Rounddown_1_29_1_Returns1_2() =>
        _eval.Evaluate("=ROUNDDOWN(1.29,1)", MakeSheet()).Should().Be(new NumberValue(1.2));

    [Fact]
    public void Rounddown_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUNDDOWN(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Rounddown_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=ROUNDDOWN(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    [Fact] public void Roundup_1_21_1_Returns1_3() =>
        _eval.Evaluate("=ROUNDUP(1.21,1)", MakeSheet()).Should().Be(new NumberValue(1.3));

    [Fact]
    public void Roundup_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=ROUNDUP(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Roundup_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=ROUNDUP(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    [Fact] public void Trunc_1_29_1_Returns1_2() =>
        _eval.Evaluate("=TRUNC(1.29,1)", MakeSheet()).Should().Be(new NumberValue(1.2));

    [Fact]
    public void Trunc_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=TRUNC(A1,2)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Trunc_ExcessiveDigits_ClampsLikeExcel() =>
        _eval.Evaluate("=TRUNC(1.2345,309)", MakeSheet()).Should().Be(new NumberValue(1.2345));

    // Binary-representation precision fix: ROUNDDOWN/TRUNC/ROUNDUP now apply Excel's
    // 15-significant-digit correction, matching Excel results for values whose raw
    // double products land just below/above the target integer.

    [Fact] public void Rounddown_4_35_2_Returns4_35() =>
        _eval.Evaluate("=ROUNDDOWN(4.35,2)", MakeSheet()).Should().Be(new NumberValue(4.35));

    [Fact] public void Trunc_4_35_2_Returns4_35() =>
        _eval.Evaluate("=TRUNC(4.35,2)", MakeSheet()).Should().Be(new NumberValue(4.35));

    [Fact] public void Rounddown_8_34_1_Returns8_3() =>
        _eval.Evaluate("=ROUNDDOWN(8.34,1)", MakeSheet()).Should().Be(new NumberValue(8.3));

    // Excel ROUNDDOWN(2.675,2) = 2.67 because 2.675 in double is 2.67499999...,
    // so the correct truncation toward zero is 2.67.
    [Fact] public void Rounddown_2_675_2_Returns2_67() =>
        _eval.Evaluate("=ROUNDDOWN(2.675,2)", MakeSheet()).Should().Be(new NumberValue(2.67));

    [Fact] public void Rounddown_Negative_4_35_2_ReturnsMinus4_35() =>
        _eval.Evaluate("=ROUNDDOWN(-4.35,2)", MakeSheet()).Should().Be(new NumberValue(-4.35));

    [Fact] public void Trunc_Negative_4_35_2_ReturnsMinus4_35() =>
        _eval.Evaluate("=TRUNC(-4.35,2)", MakeSheet()).Should().Be(new NumberValue(-4.35));

    // ROUNDUP away from zero: negative input goes more negative.
    [Fact] public void Roundup_Negative_4_342_2_ReturnsMinus4_35() =>
        _eval.Evaluate("=ROUNDUP(-4.342,2)", MakeSheet()).Should().Be(new NumberValue(-4.35));

    [Fact] public void Roundup_4_342_2_Returns4_35() =>
        _eval.Evaluate("=ROUNDUP(4.342,2)", MakeSheet()).Should().Be(new NumberValue(4.35));

    [Fact] public void Rounddown_NegativeDigits_31415_Returns31400() =>
        _eval.Evaluate("=ROUNDDOWN(31415.92654,-2)", MakeSheet()).Should().Be(new NumberValue(31400));

    [Fact] public void Roundup_NegativeDigits_31415_Returns31500() =>
        _eval.Evaluate("=ROUNDUP(31415.92654,-2)", MakeSheet()).Should().Be(new NumberValue(31500));

    [Fact] public void Rounddown_AlreadyExact_4_3_1_Returns4_3() =>
        _eval.Evaluate("=ROUNDDOWN(4.3,1)", MakeSheet()).Should().Be(new NumberValue(4.3));

    [Fact] public void Rounddown_Zero_Returns0() =>
        _eval.Evaluate("=ROUNDDOWN(0,5)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void Roundup_Zero_Returns0() =>
        _eval.Evaluate("=ROUNDUP(0,5)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact] public void Mround_14_5_Returns15() =>
        _eval.Evaluate("=MROUND(14,5)", MakeSheet()).Should().Be(new NumberValue(15));

    [Fact] public void Mround_ZeroMultiple_ReturnsZero() =>
        _eval.Evaluate("=MROUND(14,0)", MakeSheet()).Should().Be(new NumberValue(0));

    [Fact]
    public void Mround_NonFiniteInput_ReturnsNumError()
    {
        var sheet = MakeSheet((1, 1, new TextValue("1E309")));

        _eval.Evaluate("=MROUND(A1,5)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Mround_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=MROUND(1E308,0.1)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Combin_5_2_Returns10() =>
        _eval.Evaluate("=COMBIN(5,2)", MakeSheet()).Should().Be(new NumberValue(10));

    [Fact]
    public void Combin_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=COMBIN(1030,515)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Permut_5_2_Returns20() =>
        _eval.Evaluate("=PERMUT(5,2)", MakeSheet()).Should().Be(new NumberValue(20));

    [Fact]
    public void Permut_OverflowingResult_ReturnsNumError()
    {
        _eval.Evaluate("=PERMUT(171,171)", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact] public void Odd_2_Returns3() =>
        _eval.Evaluate("=ODD(2)", MakeSheet()).Should().Be(new NumberValue(3));

    [Fact] public void Even_3_Returns4() =>
        _eval.Evaluate("=EVEN(3)", MakeSheet()).Should().Be(new NumberValue(4));

    [Fact]
    public void OddEvenAndIsParity_RangeArgument_SpillsElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(2)),
            (2, 1, new NumberValue(3)));

        AssertColumn(_eval.Evaluate("=ODD(A1:A2)", sheet), new NumberValue(3), new NumberValue(3));
        AssertColumn(_eval.Evaluate("=EVEN(A1:A2)", sheet), new NumberValue(2), new NumberValue(4));
        AssertColumn(_eval.Evaluate("=ISEVEN(A1:A2)", sheet), True(), False());
        AssertColumn(_eval.Evaluate("=ISODD(A1:A2)", sheet), False(), True());
    }

    [Fact]
    public void Sqrtpi_PositiveNumber_ReturnsSquareRootOfNumberTimesPi()
    {
        var result = _eval.Evaluate("=SQRTPI(2)", MakeSheet());

        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(Math.Sqrt(2 * Math.PI), 1e-12);
    }

    [Fact]
    public void BitFunctions_SameShapeRangeArguments_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(3)), (2, 2, new NumberValue(1)));

        AssertColumn(_eval.Evaluate("=BITAND(A1:A2,B1:B2)", sheet), new NumberValue(1), new NumberValue(0));
        AssertColumn(_eval.Evaluate("=BITOR(A1:A2,B1:B2)", sheet), new NumberValue(7), new NumberValue(7));
        AssertColumn(_eval.Evaluate("=BITXOR(A1:A2,B1:B2)", sheet), new NumberValue(6), new NumberValue(7));
        AssertColumn(_eval.Evaluate("=BITLSHIFT(A1:A2,B1:B2)", sheet), new NumberValue(40), new NumberValue(12));
        AssertColumn(_eval.Evaluate("=BITRSHIFT(A1:A2,B1:B2)", sheet), new NumberValue(0), new NumberValue(3));
    }

    [Fact]
    public void BitFunctions_MismatchedRangeArgumentShapes_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(3)), (1, 3, new NumberValue(1)));

        _eval.Evaluate("=BITAND(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITXOR(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITLSHIFT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BITRSHIFT(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void EngineeringBaseConversions_SameShapeNumberAndPlacesRanges_SpillElementwise()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),      (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(4)),      (2, 2, new NumberValue(5)),
            (1, 3, new TextValue("101")),    (2, 3, new TextValue("111")),
            (1, 4, new NumberValue(3)),      (2, 4, new NumberValue(4)));

        AssertTextColumn(_eval.Evaluate("=DEC2BIN(A1:A2,B1:B2)", sheet), "0101", "00110");
        AssertTextColumn(_eval.Evaluate("=BIN2HEX(C1:C2,D1:D2)", sheet), "005", "0007");
    }

    [Fact]
    public void EngineeringBaseConversions_MismatchedNumberAndPlacesRanges_ReturnValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)), (2, 1, new NumberValue(6)),
            (1, 2, new NumberValue(4)), (1, 3, new NumberValue(5)));

        _eval.Evaluate("=DEC2BIN(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=BIN2HEX(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sqrtpi_One_ReturnsSqrtPi() =>
        _eval.Evaluate("=SQRTPI(1)", MakeSheet())
            .Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(Math.Sqrt(Math.PI), 1e-12);

    [Fact]
    public void Sqrtpi_Negative_ReturnsNumError() =>
        _eval.Evaluate("=SQRTPI(-1)", MakeSheet()).Should().Be(ErrorValue.Num);


    [Fact]
    public void Multinomial_TwoArgs_ReturnsExpected() =>
        // (2+3)!/(2!*3!) = 120 / (2*6) = 10
        _eval.Evaluate("=MULTINOMIAL(2,3)", MakeSheet())
            .Should().Be(new NumberValue(10));

    [Fact]
    public void Multinomial_NegativeArg_ReturnsNumError() =>
        _eval.Evaluate("=MULTINOMIAL(2,-1)", MakeSheet()).Should().Be(ErrorValue.Num);


    [Fact]
    public void SeriesSum_SimplePolynomial_ReturnsExpected()
    {
        // x=2, n=0, m=1, coeffs = {1,2,3} → 1*2^0 + 2*2^1 + 3*2^2 = 1+4+12 = 17
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(3)));
        _eval.Evaluate("=SERIESSUM(2,0,1,A1:A3)", sheet)
            .Should().Be(new NumberValue(17));
    }


    [Fact]
    public void Mmult_2x3_Times_3x2_Returns2x2()
    {
        // A = [[1,2,3],[4,5,6]], B = [[7,8],[9,10],[11,12]]
        // A*B = [[58,64],[139,154]]
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)),
            (4, 1, new NumberValue(7)),  (4, 2, new NumberValue(8)),
            (5, 1, new NumberValue(9)),  (5, 2, new NumberValue(10)),
            (6, 1, new NumberValue(11)), (6, 2, new NumberValue(12)));
        var result = _eval.Evaluate("=MMULT(A1:C2,A4:B6)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new NumberValue(58));
        rv.At(1, 2).Should().Be(new NumberValue(64));
        rv.At(2, 1).Should().Be(new NumberValue(139));
        rv.At(2, 2).Should().Be(new NumberValue(154));
    }

    [Fact]
    public void Mmult_IncompatibleDimensions_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(2)));
        // 2x2 * 1x1 = invalid (k mismatch)
        _eval.Evaluate("=MMULT(A1:B2,A1:A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Mdeterm_2x2_ReturnsMinusTwo()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));
        var result = _eval.Evaluate("=MDETERM(A1:B2)", sheet);
        result.Should().BeOfType<NumberValue>().Which.Value.Should().BeApproximately(-2, 1e-12);
    }

    [Fact]
    public void Mdeterm_NonSquare_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new NumberValue(4)), (2, 2, new NumberValue(5)), (2, 3, new NumberValue(6)));
        _eval.Evaluate("=MDETERM(A1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Minverse_2x2_ReturnsInverse()
    {
        // A = [[1,2],[3,4]]; A^-1 = [[-2,1],[1.5,-0.5]]
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));
        var result = _eval.Evaluate("=MINVERSE(A1:B2)", sheet);
        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        ((NumberValue)rv.At(1, 1)).Value.Should().BeApproximately(-2, 1e-12);
        ((NumberValue)rv.At(1, 2)).Value.Should().BeApproximately(1, 1e-12);
        ((NumberValue)rv.At(2, 1)).Value.Should().BeApproximately(1.5, 1e-12);
        ((NumberValue)rv.At(2, 2)).Value.Should().BeApproximately(-0.5, 1e-12);
    }

    [Fact]
    public void Minverse_Singular_ReturnsNumError()
    {
        // Singular matrix [[1,2],[2,4]] – det = 0
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(4)));
        _eval.Evaluate("=MINVERSE(A1:B2)", sheet).Should().Be(ErrorValue.Num);
    }
}
