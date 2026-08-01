using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class PageLayoutOptionsDialogPlannerTests
{
    [Fact]
    public void LineNumbering_BuildInitialState_UsesLabelsIndexMappingAndDefaults()
    {
        LineNumberOptionsDialogPlanner.ModeLabels.Should().Equal(
            "Continuous",
            "Restart Each Page",
            "Restart Each Section");

        var state = LineNumberOptionsDialogPlanner.BuildInitialState(
            5,
            2,
            LineNumberMode.RestartEachPage,
            CultureInfo.InvariantCulture);

        state.StartAtText.Should().Be("5");
        state.CountByText.Should().Be("2");
        state.ModeIndex.Should().Be(1);
        LineNumberOptionsDialogPlanner.ModeIndexFor(LineNumberMode.Continuous).Should().Be(0);
        LineNumberOptionsDialogPlanner.ModeIndexFor(LineNumberMode.RestartEachSection).Should().Be(2);
        LineNumberOptionsDialogPlanner.ModeIndexFor(LineNumberMode.None).Should().Be(0);
        LineNumberOptionsDialogPlanner.ModeForIndex(2).Should().Be(LineNumberMode.RestartEachSection);
        LineNumberOptionsDialogPlanner.ModeForIndex(-1).Should().Be(LineNumberMode.Continuous);
        LineNumberOptionsDialogPlanner.ModeForIndex(99).Should().Be(LineNumberMode.Continuous);
    }

    [Theory]
    [InlineData("0", "1", "Start At must be a whole number of 1 or greater.")]
    [InlineData("abc", "1", "Start At must be a whole number of 1 or greater.")]
    [InlineData("1", "0", "Count By must be a whole number of 1 or greater.")]
    [InlineData("1", "1.5", "Count By must be a whole number of 1 or greater.")]
    public void LineNumbering_TryBuildResult_ValidatesStartAndCount(
        string startAtText,
        string countByText,
        string expectedMessage)
    {
        var input = new LineNumberOptionsDialogInput(startAtText, countByText, 0);

        LineNumberOptionsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeFalse();

        result.Should().BeNull();
        errorMessage.Should().Be(expectedMessage);
    }

    [Fact]
    public void LineNumbering_TryBuildResult_ConstructsResultFromSelectedIndex()
    {
        var input = new LineNumberOptionsDialogInput("7", "3", 1);

        LineNumberOptionsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().Be(new LineNumberOptionsDialogResult(7, 3, LineNumberMode.RestartEachPage));
    }

    [Fact]
    public void Hyphenation_BuildInitialState_FormatsTextAndInvertsDoNotHyphenateCaps()
    {
        var page = new PageSettings
        {
            AutoHyphenation = true,
            HyphenationZonePt = 18.125,
            ConsecutiveHyphenLimit = 2,
            DoNotHyphenateCaps = true,
        };

        var state = HyphenationOptionsDialogPlanner.BuildInitialState(page, CultureInfo.InvariantCulture);

        state.AutoHyphenation.Should().BeTrue();
        state.ZoneText.Should().Be("18.13");
        state.ConsecutiveLimitText.Should().Be("2");
        state.HyphenateCaps.Should().BeFalse();
    }

    [Theory]
    [InlineData("-0.1", "0")]
    [InlineData("bad", "0")]
    [InlineData("0", "-1")]
    [InlineData("0", "bad")]
    public void Hyphenation_TryBuildResult_ValidatesNonNegativeZoneAndLimit(string zoneText, string limitText)
    {
        var input = new HyphenationOptionsDialogInput(true, zoneText, limitText, true);

        HyphenationOptionsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeFalse();

        result.Should().BeNull();
        errorMessage.Should().Be(HyphenationOptionsDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void Hyphenation_TryBuildResult_RoundsConsecutiveLimitAndConstructsResult()
    {
        var input = new HyphenationOptionsDialogInput(
            AutoHyphenation: true,
            ZoneText: "12.5",
            ConsecutiveLimitText: "2.6",
            HyphenateCaps: false);

        HyphenationOptionsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().Be(new HyphenationOptionsDialogResult(
            AutoHyphenation: true,
            ZonePt: 12.5,
            ConsecutiveLimit: 3,
            HyphenateCaps: false));
    }
}
