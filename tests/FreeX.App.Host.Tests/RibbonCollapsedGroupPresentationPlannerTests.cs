using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCollapsedGroupPresentationPlannerTests
{
    [Theory]
    [InlineData(700)]
    [InlineData(701)]
    [InlineData(920)]
    [InlineData(921)]
    public void CreateFootprint_MapsSharedPolicyToWpfCollapsedGroupPresentation(double availableWidth)
    {
        var sharedFootprint = RibbonCollapsedGroupBreakpoints.CreateFootprint(availableWidth);
        var footprint = RibbonCollapsedGroupPresentationPlanner.CreateFootprint(availableWidth);

        footprint.Mode.Should().Be(sharedFootprint.Mode);
        footprint.Width.Should().Be(sharedFootprint.Width);
        footprint.Margin.Should().Be(ToThickness(sharedFootprint.Margin));
        footprint.Padding.Should().Be(ToThickness(sharedFootprint.Padding));
        footprint.CaptionVisibility.Should().Be(ToWpfVisibility(sharedFootprint.CaptionVisibility));
        footprint.CaptionFontSize.Should().Be(sharedFootprint.CaptionFontSize);
        footprint.CaptionMaxWidth.Should().Be(sharedFootprint.CaptionMaxWidth);
        footprint.IconFontSize.Should().Be(sharedFootprint.IconFontSize);
        RibbonCollapsedGroupPresentationPlanner.GetCacheKey(availableWidth).Should().Be(sharedFootprint.CacheKey);
    }

    [Theory]
    [InlineData(-8, 900)]
    [InlineData(72, 900)]
    [InlineData(42, 900)]
    [InlineData(72, 1200)]
    [InlineData(60, 1200)]
    public void GetPlannedWidth_DelegatesAdaptivePlanningWidthToSharedPolicy(
        double measuredWidth,
        double availableWidth)
    {
        RibbonCollapsedGroupPresentationPlanner
            .GetPlannedWidth(measuredWidth, availableWidth)
            .Should()
            .Be(RibbonCollapsedGroupBreakpoints.GetPlannedWidth(measuredWidth, availableWidth));
    }

    [Fact]
    public void CollapsedGroupFootprint_SourceKeepsNumericModePolicyInSharedRibbon()
    {
        var plannerSource = DialogSourceTestSupport.ReadHostSources("RibbonCollapsedGroupPresentationPlanner.cs");
        var adaptiveSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");
        var sharedSource = WorkspaceFileLocator.ReadAllText(
            "shared",
            "Free.Shared.Ribbon",
            "Layout",
            "RibbonCollapsedGroupBreakpoints.cs");

        plannerSource.Should().Contain("RibbonCollapsedGroupBreakpoints.CreateFootprint(availableWidth)");
        plannerSource.Should().Contain("ToThickness(");
        plannerSource.Should().Contain("ToVisibility(");
        adaptiveSource.Should().Contain("RibbonCollapsedGroupBreakpoints.GetFootprintMode(availableWidth)");
        (plannerSource + adaptiveSource).Should().NotContain("availableWidth <= 700");
        (plannerSource + adaptiveSource).Should().NotContain("availableWidth <= 920");
        sharedSource.Should().Contain("availableWidth <= 700");
        sharedSource.Should().Contain("availableWidth <= 920");
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

    private static Thickness ToThickness(RibbonCollapsedGroupInsets insets) =>
        new(insets.Left, insets.Top, insets.Right, insets.Bottom);

    private static Visibility ToWpfVisibility(RibbonCollapsedGroupCaptionVisibility visibility) =>
        visibility == RibbonCollapsedGroupCaptionVisibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
}
