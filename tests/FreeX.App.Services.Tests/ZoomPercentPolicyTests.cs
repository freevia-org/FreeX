using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class ZoomPercentPolicyTests
{
    private static readonly ZoomPercentPolicy ExcelPolicy = new(10d, 100d, 400d);
    private static readonly ZoomPercentPolicy WordPolicy = new(50d, 100d, 200d);

    [Theory]
    [InlineData(1, 10)]
    [InlineData(100, 100)]
    [InlineData(500, 400)]
    public void ClampPercent_UsesConfiguredRange(double input, double expected)
    {
        ExcelPolicy.ClampPercent(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("25%", 25)]
    [InlineData(" 150 ", 150)]
    [InlineData("125.5", 125.5)]
    public void TryParsePercent_AcceptsNumericPercentText(string input, double expected)
    {
        ExcelPolicy.TryParsePercent(input, out var percent).Should().BeTrue();
        percent.Should().Be(expected);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("401")]
    public void TryParsePercentInRange_RejectsValuesOutsideConfiguredRange(string input)
    {
        ExcelPolicy.TryParsePercentInRange(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("125", 125)]
    [InlineData("125%", 125)]
    public void TryParseWholePercent_AcceptsWholePercentText(string input, int expected)
    {
        WordPolicy.TryParseWholePercent(input, out var percent).Should().BeTrue();
        percent.Should().Be(expected);
    }

    [Theory]
    [InlineData("125.0")]
    [InlineData("125.5")]
    [InlineData("abc")]
    public void TryParseWholePercent_RejectsFractionalOrInvalidText(string input)
    {
        WordPolicy.TryParseWholePercent(input, out _).Should().BeFalse();
    }

    [Fact]
    public void PercentSliderMapping_RoundTripsThroughDefaultStatusSliderScale()
    {
        var slider = ExcelPolicy.PercentToSlider(200);

        ExcelPolicy.SliderToPercent(slider).Should().BeApproximately(200, 0.0001);
    }

    [Theory]
    [InlineData(25, "50%")]
    [InlineData(125, "125%")]
    [InlineData(250, "200%")]
    public void FormatPercentLabel_ClampsAndNormalizesWholePercent(double input, string expected)
    {
        WordPolicy.FormatPercentLabel(input).Should().Be(expected);
    }

    [Fact]
    public void IsPresetPercent_UsesExactPresetMembership()
    {
        WordPolicy.IsPresetPercent(100, [200, 100, 75]).Should().BeTrue();
        WordPolicy.IsPresetPercent(201, [200, 100, 75]).Should().BeFalse();
    }
}
