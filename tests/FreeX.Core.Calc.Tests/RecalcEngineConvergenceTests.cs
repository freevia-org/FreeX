using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class RecalcEngineConvergenceTests
{
    public static TheoryData<ScalarValue, ScalarValue, double> FiniteNumericCases => new()
    {
        { new NumberValue(12.5), new NumberValue(10), 2.5 },
        { new DateTimeValue(12.5), new DateTimeValue(10), 2.5 },
        { new NumberValue(12.5), new DateTimeValue(10), 2.5 },
        { new DateTimeValue(10), new NumberValue(12.5), 2.5 },
        { new NumberValue(-0.0), new DateTimeValue(0.0), 0.0 }
    };

    [Theory]
    [MemberData(nameof(FiniteNumericCases))]
    public void ComputeConvergenceDelta_FiniteNumbersAndDatesUseTheirSerialMagnitude(
        ScalarValue previous,
        ScalarValue current,
        double expected)
    {
        RecalcEngine.ComputeConvergenceDelta(previous, current).Should().Be(expected);
    }

    public static TheoryData<ScalarValue?, ScalarValue?> EqualFallbackCases => new()
    {
        { new NumberValue(double.NaN), new NumberValue(double.NaN) },
        { new NumberValue(double.PositiveInfinity), new NumberValue(double.PositiveInfinity) },
        { new DateTimeValue(double.NegativeInfinity), new DateTimeValue(double.NegativeInfinity) },
        { new BoolValue(true), new BoolValue(true) },
        { new TextValue("same"), new TextValue("same") },
        { new ErrorValue("#VALUE!"), new ErrorValue("#VALUE!") },
        { BlankValue.Instance, new BlankValue() },
        { null, null }
    };

    [Theory]
    [MemberData(nameof(EqualFallbackCases))]
    public void ComputeConvergenceDelta_EqualNonFiniteOrNonNumericValuesAreConverged(
        ScalarValue? previous,
        ScalarValue? current)
    {
        RecalcEngine.ComputeConvergenceDelta(previous, current).Should().Be(0.0);
    }

    public static TheoryData<ScalarValue?, ScalarValue?> ChangedFallbackCases => new()
    {
        { new NumberValue(double.NegativeInfinity), new NumberValue(double.PositiveInfinity) },
        { new DateTimeValue(double.NaN), new DateTimeValue(double.PositiveInfinity) },
        { new NumberValue(1), new NumberValue(double.PositiveInfinity) },
        { new NumberValue(1), new TextValue("1") },
        { new BoolValue(false), new BoolValue(true) },
        { new TextValue("before"), new TextValue("after") },
        { ErrorValue.Value, ErrorValue.Ref },
        { BlankValue.Instance, null }
    };

    [Theory]
    [MemberData(nameof(ChangedFallbackCases))]
    public void ComputeConvergenceDelta_ChangedNonFiniteOrNonNumericValuesDoNotConverge(
        ScalarValue? previous,
        ScalarValue? current)
    {
        RecalcEngine.ComputeConvergenceDelta(previous, current).Should().Be(double.MaxValue);
    }
}
