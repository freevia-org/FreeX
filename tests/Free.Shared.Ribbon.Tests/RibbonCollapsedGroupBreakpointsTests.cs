namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonCollapsedGroupBreakpointsTests
{
    [Theory]
    [InlineData(
        700,
        RibbonCollapsedGroupFootprintMode.Captionless,
        52,
        54,
        0,
        0,
        2,
        0,
        1,
        2,
        1,
        2,
        RibbonCollapsedGroupCaptionVisibility.Collapsed,
        12,
        48,
        18,
        "captionless")]
    [InlineData(
        701,
        RibbonCollapsedGroupFootprintMode.Compact,
        52,
        54,
        0,
        0,
        2,
        0,
        1,
        2,
        1,
        2,
        RibbonCollapsedGroupCaptionVisibility.Visible,
        12,
        48,
        18,
        "compact")]
    [InlineData(
        920,
        RibbonCollapsedGroupFootprintMode.Compact,
        52,
        54,
        0,
        0,
        2,
        0,
        1,
        2,
        1,
        2,
        RibbonCollapsedGroupCaptionVisibility.Visible,
        12,
        48,
        18,
        "compact")]
    [InlineData(
        921,
        RibbonCollapsedGroupFootprintMode.Normal,
        64,
        68,
        1,
        0,
        3,
        0,
        3,
        2,
        3,
        2,
        RibbonCollapsedGroupCaptionVisibility.Visible,
        12,
        60,
        22,
        "normal")]
    public void CreateFootprint_OwnsCollapsedGroupModeAndRendererNeutralFootprintPolicy(
        double availableWidth,
        RibbonCollapsedGroupFootprintMode expectedMode,
        double expectedWidth,
        double expectedPlannedWidth,
        double expectedMarginLeft,
        double expectedMarginTop,
        double expectedMarginRight,
        double expectedMarginBottom,
        double expectedPaddingLeft,
        double expectedPaddingTop,
        double expectedPaddingRight,
        double expectedPaddingBottom,
        RibbonCollapsedGroupCaptionVisibility expectedCaptionVisibility,
        double expectedCaptionFontSize,
        double expectedCaptionMaxWidth,
        double expectedIconFontSize,
        string expectedCacheKey)
    {
        var footprint = RibbonCollapsedGroupBreakpoints.CreateFootprint(availableWidth);

        footprint.Mode.Should().Be(expectedMode);
        footprint.Width.Should().Be(expectedWidth);
        footprint.PlannedWidth.Should().Be(expectedPlannedWidth);
        footprint.Margin.Should().Be(new RibbonCollapsedGroupInsets(
            expectedMarginLeft,
            expectedMarginTop,
            expectedMarginRight,
            expectedMarginBottom));
        footprint.Padding.Should().Be(new RibbonCollapsedGroupInsets(
            expectedPaddingLeft,
            expectedPaddingTop,
            expectedPaddingRight,
            expectedPaddingBottom));
        footprint.CaptionVisibility.Should().Be(expectedCaptionVisibility);
        footprint.CaptionFontSize.Should().Be(expectedCaptionFontSize);
        footprint.CaptionMaxWidth.Should().Be(expectedCaptionMaxWidth);
        footprint.IconFontSize.Should().Be(expectedIconFontSize);
        footprint.CacheKey.Should().Be(expectedCacheKey);
        RibbonCollapsedGroupBreakpoints.GetFootprintMode(availableWidth).Should().Be(expectedMode);
        RibbonCollapsedGroupBreakpoints.GetCacheKey(availableWidth).Should().Be(expectedCacheKey);
    }

    [Theory]
    [InlineData(-8, 900, 0)]
    [InlineData(72, 900, 54)]
    [InlineData(42, 900, 42)]
    [InlineData(72, 1200, 68)]
    [InlineData(60, 1200, 60)]
    public void GetPlannedWidth_CapsMeasuredCollapsedWidthUsingSharedFootprintPolicy(
        double measuredWidth,
        double availableWidth,
        double expectedWidth)
    {
        RibbonCollapsedGroupBreakpoints
            .GetPlannedWidth(measuredWidth, availableWidth)
            .Should()
            .Be(expectedWidth);
    }

    [Fact]
    public void GetFootprint_ReturnsTheSharedModePolicyWithoutRendererTypes()
    {
        var compact = RibbonCollapsedGroupBreakpoints.GetFootprint(RibbonCollapsedGroupFootprintMode.Compact);

        compact.Mode.Should().Be(RibbonCollapsedGroupFootprintMode.Compact);
        compact.Margin.Should().Be(new RibbonCollapsedGroupInsets(0, 0, 2, 0));
        compact.CaptionVisibility.Should().Be(RibbonCollapsedGroupCaptionVisibility.Visible);
    }
}
