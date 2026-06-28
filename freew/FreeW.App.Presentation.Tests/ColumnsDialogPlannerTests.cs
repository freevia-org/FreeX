using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ColumnsDialogPlannerTests
{
    [Fact]
    public void Presets_ExposeWordColumnsDialogChoicesInDisplayOrder()
    {
        ColumnsDialogPlanner.Presets.Select(preset => preset.Label)
            .Should().Equal("One", "Two", "Three", "Left", "Right");
    }

    [Fact]
    public void BuildInitialState_SelectsEqualPresetFromPageState()
    {
        var page = new PageSettings
        {
            ColumnCount = 3,
            ColumnSpacingPt = 42,
            ColumnsLineBetween = true,
            WidthPt = 600,
            MarginLeftPt = 50,
            MarginRightPt = 70,
        };

        var state = ColumnsDialogPlanner.BuildInitialState(page, CultureInfo.InvariantCulture);

        state.PresetIndex.Should().Be(2);
        state.CountText.Should().Be("3");
        state.SpacingText.Should().Be("42");
        state.LineBetween.Should().BeTrue();
        state.ContentWidthPt.Should().Be(480);
    }

    [Fact]
    public void BuildInitialState_SelectsUnequalPresetFromExistingWidths()
    {
        ColumnsDialogPlanner.BuildInitialState(
                new PageSettings { ColumnCount = 2, ColumnWidthsPt = [108, 360] },
                CultureInfo.InvariantCulture)
            .PresetIndex.Should().Be(3);

        ColumnsDialogPlanner.BuildInitialState(
                new PageSettings { ColumnCount = 2, ColumnWidthsPt = [360, 108] },
                CultureInfo.InvariantCulture)
            .PresetIndex.Should().Be(4);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(99, 2)]
    public void ColumnCountForPreset_MapsSelectionToPlannedCount(int presetIndex, int expectedCount)
    {
        ColumnsDialogPlanner.ColumnCountForPreset(presetIndex).Should().Be(expectedCount);
    }

    [Fact]
    public void PlanUnequalWidths_UsesWordStyleLeftAndRightSplit()
    {
        ColumnsDialogPlanner.PlanUnequalWidths(3, contentWidthPt: 468, spacingPt: 36)
            .Should().Equal(108, 324);

        ColumnsDialogPlanner.PlanUnequalWidths(4, contentWidthPt: 468, spacingPt: 36)
            .Should().Equal(324, 108);
    }

    [Theory]
    [InlineData("0", "36")]
    [InlineData("13", "36")]
    [InlineData("two", "36")]
    [InlineData("2", "-1")]
    [InlineData("2", "wide")]
    public void TryBuildResult_RejectsInvalidCountOrSpacing(string countText, string spacingText)
    {
        var input = ValidInput() with { CountText = countText, SpacingText = spacingText };

        ColumnsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(ColumnsDialogPlanner.ValidationMessage);
    }

    [Fact]
    public void TryBuildResult_CreatesCustomResult()
    {
        var input = ValidInput() with
        {
            PresetIndex = 1,
            CountText = "5",
            SpacingText = "18.5",
            LineBetween = true,
        };

        ColumnsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Count.Should().Be(5);
        result.SpacingPt.Should().Be(18.5);
        result.LineBetween.Should().BeTrue();
        result.WidthsPt.Should().BeNull();
    }

    [Fact]
    public void TryBuildResult_UnequalPresetForcesTwoColumns()
    {
        var input = ValidInput() with
        {
            PresetIndex = 3,
            CountText = "7",
            SpacingText = "36",
            ContentWidthPt = 468,
        };

        ColumnsDialogPlanner.TryBuildResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result!.Count.Should().Be(2);
        result.WidthsPt.Should().Equal(108, 324);
    }

    private static ColumnsDialogInput ValidInput() => new(
        PresetIndex: 0,
        CountText: "2",
        SpacingText: "36",
        LineBetween: false,
        ContentWidthPt: 468);
}
