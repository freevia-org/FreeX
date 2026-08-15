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

    [Fact]
    public void PresetLadders_StayAppOwnedButShareMembershipAndLabelPolicy()
    {
        ZoomDialogPlanner.Presets.Should().Equal(400, 200, 100, 75, 50, 25);

        foreach (var preset in ZoomDialogPlanner.Presets)
        {
            ExcelPolicy.IsPresetPercent(preset, ZoomDialogPlanner.Presets).Should().BeTrue();
            ZoomDialogPlanner.IsPreset(preset).Should().BeTrue();
            ZoomDialogPlanner.FormatPresetLabel(preset)
                .Should()
                .Be(ExcelPolicy.FormatPercentLabel(preset));
        }

        ZoomDialogPlanner.IsPreset(125).Should().BeFalse();
    }

    // --- TryResolveWholePercent: the single shared custom-percent route for both Zoom dialogs ---

    [Theory]
    [InlineData("125", 125)]
    [InlineData("125%", 125)]
    [InlineData(" 150 ", 150)]
    [InlineData("  75 % ", 75)]
    [InlineData("125.0", 125)]
    [InlineData("10", 10)]
    [InlineData("400", 400)]
    public void TryResolveWholePercent_RejectMode_AcceptsWholePercentInRange(string input, int expected)
    {
        ExcelPolicy.TryResolveWholePercent(input, ZoomPercentRangeMode.Reject, out var percent, out var error)
            .Should()
            .BeTrue();

        percent.Should().Be(expected);
        error.Should().Be(ZoomPercentInputError.None);
    }

    [Theory]
    [InlineData(null, ZoomPercentInputError.Missing)]
    [InlineData("", ZoomPercentInputError.Missing)]
    [InlineData("   ", ZoomPercentInputError.Missing)]
    [InlineData("%", ZoomPercentInputError.Missing)]
    [InlineData("abc", ZoomPercentInputError.NotNumeric)]
    [InlineData("12x", ZoomPercentInputError.NotNumeric)]
    [InlineData("9", ZoomPercentInputError.OutOfRange)]
    [InlineData("401", ZoomPercentInputError.OutOfRange)]
    [InlineData("-5", ZoomPercentInputError.OutOfRange)]
    [InlineData("125.5", ZoomPercentInputError.NotWholePercent)]
    public void TryResolveWholePercent_RejectMode_ClassifiesEveryRejection(
        string? input,
        ZoomPercentInputError expected)
    {
        ExcelPolicy.TryResolveWholePercent(input, ZoomPercentRangeMode.Reject, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expected);
    }

    [Theory]
    [InlineData("125", 125)]
    [InlineData("125%", 125)]
    [InlineData("25", 50)]
    [InlineData("0", 50)]
    [InlineData("-100", 50)]
    [InlineData("250", 200)]
    [InlineData("100000", 200)]
    public void TryResolveWholePercent_ClampMode_ClampsAtBothBoundsInsteadOfRejecting(string input, int expected)
    {
        WordPolicy.TryResolveWholePercent(input, ZoomPercentRangeMode.Clamp, out var percent, out var error)
            .Should()
            .BeTrue();

        percent.Should().Be(expected);
        error.Should().Be(ZoomPercentInputError.None);
    }

    [Theory]
    [InlineData(null, ZoomPercentInputError.Missing)]
    [InlineData("", ZoomPercentInputError.Missing)]
    [InlineData("\t ", ZoomPercentInputError.Missing)]
    [InlineData("abc", ZoomPercentInputError.NotNumeric)]
    [InlineData("125.5", ZoomPercentInputError.NotWholePercent)]
    public void TryResolveWholePercent_ClampMode_StillRejectsUnparseableAndFractionalText(
        string? input,
        ZoomPercentInputError expected)
    {
        WordPolicy.TryResolveWholePercent(input, ZoomPercentRangeMode.Clamp, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expected);
    }

    [Fact]
    public void TryResolveWholePercent_ClampMode_ReportsNotWholeAfterClampingNotBefore()
    {
        // 250.5 clamps to the 200% ceiling, which *is* whole -- the fractional part of the raw input
        // is irrelevant once clamping has pinned the value to a bound.
        WordPolicy.TryResolveWholePercent("250.5", ZoomPercentRangeMode.Clamp, out var percent, out var error)
            .Should()
            .BeTrue();

        percent.Should().Be(200);
        error.Should().Be(ZoomPercentInputError.None);
    }

    [Fact]
    public void TryResolveWholePercent_FailureLeavesDefaultPercentOnTheOutParameter()
    {
        ExcelPolicy.TryResolveWholePercent("abc", ZoomPercentRangeMode.Reject, out var percent, out _)
            .Should()
            .BeFalse();

        percent.Should().Be(100);
    }
}
