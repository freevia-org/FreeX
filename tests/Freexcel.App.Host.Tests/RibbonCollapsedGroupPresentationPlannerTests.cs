using System.Windows;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonCollapsedGroupPresentationPlannerTests
{
    [Theory]
    [InlineData(760, "Captionless", 44, Visibility.Collapsed, 9, 40, 18, "captionless")]
    [InlineData(761, "Compact", 44, Visibility.Visible, 9, 40, 18, "compact")]
    [InlineData(920, "Compact", 44, Visibility.Visible, 9, 40, 18, "compact")]
    [InlineData(921, "Normal", 64, Visibility.Visible, 10, 60, 22, "normal")]
    public void CreateFootprint_MapsExcelWidthBandsToCollapsedGroupPresentation(
        double availableWidth,
        string expectedMode,
        double expectedWidth,
        Visibility expectedCaptionVisibility,
        double expectedCaptionFontSize,
        double expectedCaptionMaxWidth,
        double expectedIconFontSize,
        string expectedCacheKey)
    {
        var footprint = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(availableWidth);

        footprint.Mode.ToString().Should().Be(expectedMode);
        footprint.Width.Should().Be(expectedWidth);
        footprint.CaptionVisibility.Should().Be(expectedCaptionVisibility);
        footprint.CaptionFontSize.Should().Be(expectedCaptionFontSize);
        footprint.CaptionMaxWidth.Should().Be(expectedCaptionMaxWidth);
        footprint.IconFontSize.Should().Be(expectedIconFontSize);
        RibbonCollapsedGroupPresentationPlanner.GetCacheKey(availableWidth).Should().Be(expectedCacheKey);
    }

    [Theory]
    [InlineData(72, 900, 46)]
    [InlineData(42, 900, 42)]
    [InlineData(72, 1200, 68)]
    [InlineData(60, 1200, 60)]
    public void GetPlannedWidth_CapsMeasuredCollapsedWidthForAdaptivePlanning(
        double measuredWidth,
        double availableWidth,
        double expectedWidth)
    {
        RibbonCollapsedGroupPresentationPlanner
            .GetPlannedWidth(measuredWidth, availableWidth)
            .Should()
            .Be(expectedWidth);
    }
}
