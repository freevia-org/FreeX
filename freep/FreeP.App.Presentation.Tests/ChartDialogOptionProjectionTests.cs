using System.Globalization;
using FluentAssertions;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartDialogOptionProjectionTests
{
    private sealed record Option<T>(T Value, string Label);

    [Fact]
    public void OptionProjectionSupportsNullableValuesAndStableFallbacks()
    {
        Option<bool?>[] options = [new(null, "Automatic"), new(true, "On"), new(false, "Off")];

        ChartDialogOptionProjection.FindIndex(options, false, option => option.Value).Should().Be(2);
        ChartDialogOptionProjection.FindIndex(options, default(bool?), option => option.Value).Should().Be(0);
        ChartDialogOptionProjection.ValueAtOrDefault(options, 1, option => option.Value).Should().BeTrue();
        ChartDialogOptionProjection.ValueAtOrDefault(options, -1, option => option.Value, default(bool?)).Should().BeNull();
    }

    [Fact]
    public void NumericProjectionUsesSuppliedCultureAndPreservesValidationMessage()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");

        ChartDialogOptionProjection.ParseOptionalDouble(
            "12,5",
            culture,
            value => double.IsFinite(value) && value >= 0,
            "invalid").Should().Be(12.5);
        ChartDialogOptionProjection.Format(12.5, culture).Should().Be("12,5");

        var act = () => ChartDialogOptionProjection.ParseOptionalInt(
            "11",
            culture,
            value => value is >= 0 and <= 10,
            "whole number required");
        act.Should().Throw<FormatException>().WithMessage("whole number required");
    }

    [Fact]
    public void NonNegativeIntegerListUsesDialogCultureAndRejectsInvalidTokens()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");

        ChartDialogOptionProjection.ParseNonNegativeIntList("0, 2,5", culture, "invalid")
            .Should().Equal(0, 2, 5);

        var act = () => ChartDialogOptionProjection.ParseNonNegativeIntList("0,-1", culture, "invalid");
        act.Should().Throw<FormatException>().WithMessage("invalid");
    }
}
