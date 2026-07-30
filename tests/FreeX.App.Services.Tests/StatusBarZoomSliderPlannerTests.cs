using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarZoomSliderPlannerTests
{
    [Fact]
    public void Build_ExposesSharedStatusZoomSliderChromePlan()
    {
        var plan = StatusBarZoomSliderPlanner.Build(125);

        plan.ZoomPercent.Should().Be(125);
        plan.ZoomText.Should().Be("125%");
        plan.MinimumSliderValue.Should().Be(0d);
        plan.MaximumSliderValue.Should().Be(200d);
        plan.SmallChange.Should().Be(5d);
        plan.LargeChange.Should().Be(10d);
        plan.SliderWidth.Should().Be(120d);
        plan.SliderHeight.Should().Be(22d);
        plan.SliderTickValues.Should().Equal(100d);
        plan.VisualTickLefts.Should().Equal(8d, 60d, 111d);
    }

    [Fact]
    public void BuildInput_SnapsNearDefaultSliderValueBeforeMappingZoom()
    {
        var plan = StatusBarZoomSliderPlanner.BuildInput(101.5);

        plan.Should().Be(new StatusBarZoomSliderInputPlan(
            SliderValue: 100d,
            ZoomPercent: 100,
            SnappedToDefault: true));
    }

    [Fact]
    public void BuildInput_MapsUnsnappedSliderValueToRoundedZoom()
    {
        var plan = StatusBarZoomSliderPlanner.BuildInput(110);

        plan.SnappedToDefault.Should().BeFalse();
        plan.ZoomPercent.Should().Be(130);
    }

    [Fact]
    public void BuildThumbPlan_UsesSharedTrackInsetsAndClampsToHost()
    {
        var start = StatusBarZoomSliderPlanner.BuildThumbPlan(0);
        var middle = StatusBarZoomSliderPlanner.BuildThumbPlan(100);
        var end = StatusBarZoomSliderPlanner.BuildThumbPlan(200);

        start.Left.Should().Be(3.5);
        middle.Left.Should().Be(55.5);
        end.Left.Should().Be(107.5);
        end.Normalized.Should().Be(1d);
    }

    [Fact]
    public void BuildThumbPlan_FallsBackToSharedGeometryBeforeLayout()
    {
        var plan = StatusBarZoomSliderPlanner.BuildThumbPlan(
            double.NaN,
            double.NaN,
            double.NaN);

        plan.Left.Should().Be(55.5);
        plan.Normalized.Should().Be(0.5d);
    }

    [Theory]
    [InlineData(1, 10, "10%")]
    [InlineData(100, 100, "100%")]
    [InlineData(500, 400, "400%")]
    public void ClampAndFormatZoomPercent_UseSharedExcelRange(int input, int expected, string expectedText)
    {
        StatusBarZoomSliderPlanner.ClampZoomPercent(input).Should().Be(expected);
        StatusBarZoomSliderPlanner.FormatZoomPercent(input).Should().Be(expectedText);
    }
}
