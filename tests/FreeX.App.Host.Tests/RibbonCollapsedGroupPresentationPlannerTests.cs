using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCollapsedGroupPresentationPlannerTests
{
    [Theory]
    [InlineData(700, "Captionless", 52, Visibility.Collapsed, 12, 48, 18, "captionless")]
    [InlineData(701, "Compact", 52, Visibility.Visible, 12, 48, 18, "compact")]
    [InlineData(920, "Compact", 52, Visibility.Visible, 12, 48, 18, "compact")]
    [InlineData(921, "Normal", 64, Visibility.Visible, 12, 60, 22, "normal")]
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
    [InlineData(-8, 900, 0)]
    [InlineData(72, 900, 54)]
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

    [Fact]
    public void CreateFootprint_ReusesBoxedDependencyPropertyValuesForHotPath()
    {
        var compact = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(900);
        var sameCompactMode = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(760);
        var normal = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(1000);

        ReferenceEquals(compact.BoxedWidth, sameCompactMode.BoxedWidth).Should().BeTrue();
        ReferenceEquals(compact.BoxedMargin, sameCompactMode.BoxedMargin).Should().BeTrue();
        ReferenceEquals(compact.BoxedPadding, sameCompactMode.BoxedPadding).Should().BeTrue();
        ReferenceEquals(compact.BoxedIconFontSize, sameCompactMode.BoxedIconFontSize).Should().BeTrue();
        ReferenceEquals(compact.BoxedWidth, normal.BoxedWidth).Should().BeFalse();
    }

    [Fact]
    public void SetCollapsedButtonFootprint_RebuildsCachedTargetsWhenButtonContentChangesInsideSameMode()
    {
        StaTestRunner.Run(() =>
        {
            var button = new Button { Content = CreateCollapsedButtonContent(out _, out _) };
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(new[] { button }, 900);

            button.Content = CreateCollapsedButtonContent(out var caption, out var icon);
            RibbonAdaptiveStateApplicator.SetCollapsedButtonFootprint(new[] { button }, 900);

            button.Width.Should().Be(52);
            caption.Visibility.Should().Be(Visibility.Visible);
            caption.FontSize.Should().Be(12);
            caption.MaxWidth.Should().Be(48);
            caption.TextWrapping.Should().Be(TextWrapping.NoWrap);
            caption.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
            caption.TextAlignment.Should().Be(TextAlignment.Center);
            icon.FontSize.Should().Be(18);
        });
    }

    private static StackPanel CreateCollapsedButtonContent(out TextBlock caption, out TextBlock icon)
    {
        var content = new StackPanel();
        caption = new TextBlock { Text = "Group" };
        RibbonMetadata.SetRole(caption, RibbonMetadataRole.CommandLabel);
        content.Children.Add(caption);

        icon = new TextBlock { Text = "\uE8A5" };
        RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
        content.Children.Add(icon);

        return content;
    }
}
