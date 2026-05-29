using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonMetadataTests
{
    [Fact]
    public void RoleHelpers_ReadAttachedMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var label = new TextBlock();
            var icon = new TextBlock();
            var collapsedButton = new Button();

            RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
            RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);
            RibbonMetadata.SetRole(collapsedButton, RibbonMetadataRole.CollapsedGroupButton);

            RibbonMetadata.IsCommandLabel(label).Should().BeTrue();
            RibbonMetadata.IsCommandIcon(icon).Should().BeTrue();
            RibbonMetadata.IsCollapsedGroupButton(collapsedButton).Should().BeTrue();
        });
    }

    [Fact]
    public void RoleHelpers_AttachedMetadataOverridesConflictingLegacyTags()
    {
        StaTestRunner.Run(() =>
        {
            var label = new TextBlock { Tag = "RibbonIcon" };
            var icon = new TextBlock { Tag = "RibbonLabel" };
            RibbonMetadata.SetRole(label, RibbonMetadataRole.CommandLabel);
            RibbonMetadata.SetRole(icon, RibbonMetadataRole.CommandIcon);

            RibbonMetadata.IsCommandLabel(label).Should().BeTrue();
            RibbonMetadata.IsCommandIcon(label).Should().BeFalse();
            RibbonMetadata.IsCommandIcon(icon).Should().BeTrue();
            RibbonMetadata.IsCommandLabel(icon).Should().BeFalse();
        });
    }

    [Fact]
    public void RoleHelpers_IgnoreLegacyTagsWithoutAttachedMetadata()
    {
        StaTestRunner.Run(() =>
        {
            RibbonMetadata.IsCommandLabel(new TextBlock { Tag = "RibbonLabel" }).Should().BeFalse();
            RibbonMetadata.IsCommandIcon(new TextBlock { Tag = "RibbonIcon" }).Should().BeFalse();
            RibbonMetadata.IsCollapsedGroupButton(new Button { Tag = "RibbonCollapsedGroupButton" }).Should().BeFalse();
        });
    }

    [Fact]
    public void CompactWidths_ReadAttachedMetadataOnly()
    {
        StaTestRunner.Run(() =>
        {
            var metadataButton = new Button();
            RibbonMetadata.SetCompactWidths(metadataButton, 74, 32);

            RibbonMetadata.TryGetCompactWidths(metadataButton, out var metadataFull, out var metadataCompact).Should().BeTrue();
            metadataFull.Should().Be(74);
            metadataCompact.Should().Be(32);

            RibbonMetadata.TryGetCompactWidths(new Button { Tag = "RibbonCompact:58.5:30" }, out var legacyFull, out var legacyCompact)
                .Should()
                .BeFalse();
            legacyFull.Should().Be(0);
            legacyCompact.Should().Be(0);
        });
    }

    [Theory]
    [InlineData(double.NaN, 24)]
    [InlineData(74, double.NaN)]
    [InlineData(double.PositiveInfinity, 24)]
    [InlineData(74, double.PositiveInfinity)]
    [InlineData(0, 24)]
    [InlineData(74, 0)]
    [InlineData(-74, 24)]
    [InlineData(74, -24)]
    [InlineData(24, 74)]
    public void CompactWidths_RejectInvalidLayoutMetadata(double fullWidth, double compactWidth)
    {
        StaTestRunner.Run(() =>
        {
            var button = new Button();
            RibbonMetadata.SetCompactWidths(button, fullWidth, compactWidth);

            RibbonMetadata.TryGetCompactWidths(button, out var actualFullWidth, out var actualCompactWidth)
                .Should()
                .BeFalse();
            actualFullWidth.Should().Be(0);
            actualCompactWidth.Should().Be(0);
        });
    }

    [Fact]
    public void CommandContentLayout_ReadsAttachedMetadataOnly()
    {
        StaTestRunner.Run(() =>
        {
            var metadataGrid = new Grid();
            RibbonMetadata.SetCommandContentLayout(metadataGrid, RibbonCommandContentLayout.Small);

            RibbonMetadata.TryGetCommandContentLayout(metadataGrid, out var metadataLayout).Should().BeTrue();
            metadataLayout.Should().Be(RibbonCommandContentLayout.Small);

            RibbonMetadata.TryGetCommandContentLayout(new Grid { Tag = "RibbonCommandContent:L" }, out var legacyLayout)
                .Should()
                .BeFalse();
            legacyLayout.Should().Be(RibbonCommandContentLayout.None);
        });
    }

    [Fact]
    public void CommandContentLayout_ReturnsNoneForMissingMetadata()
    {
        StaTestRunner.Run(() =>
        {
            RibbonMetadata.TryGetCommandContentLayout(null, out var nullLayout).Should().BeFalse();
            nullLayout.Should().Be(RibbonCommandContentLayout.None);

            RibbonMetadata.TryGetCommandContentLayout(new Grid(), out var untaggedLayout).Should().BeFalse();
            untaggedLayout.Should().Be(RibbonCommandContentLayout.None);
        });
    }

    [Fact]
    public void AttachedLayoutMetadata_IgnoresLegacyTags()
    {
        StaTestRunner.Run(() =>
        {
            var compactButton = new Button { Tag = "RibbonCompact:128:44" };
            RibbonMetadata.SetCompactWidths(compactButton, 74, 32);

            RibbonMetadata.TryGetCompactWidths(compactButton, out var fullWidth, out var compactWidth)
                .Should()
                .BeTrue();
            fullWidth.Should().Be(74);
            compactWidth.Should().Be(32);

            var commandContent = new Grid { Tag = "RibbonCommandContent:L" };
            RibbonMetadata.SetCommandContentLayout(commandContent, RibbonCommandContentLayout.Small);

            RibbonMetadata.TryGetCommandContentLayout(commandContent, out var layout).Should().BeTrue();
            layout.Should().Be(RibbonCommandContentLayout.Small);
        });
    }

    [Fact]
    public void GroupName_UsesAttachedMetadataAndDoesNotInferIdentityFromCaptionShape()
    {
        StaTestRunner.Run(() =>
        {
            var metadataGroup = new Grid();
            RibbonMetadata.SetRole(metadataGroup, RibbonMetadataRole.RibbonGroup);
            RibbonMetadata.SetGroupName(metadataGroup, "Clipboard");

            RibbonMetadata.IsRibbonGroup(metadataGroup).Should().BeTrue();
            RibbonMetadata.TryGetGroupName(metadataGroup, out var metadataName).Should().BeTrue();
            metadataName.Should().Be("Clipboard");

            var groupLikeGrid = new Grid();
            var labelBorder = new Border
            {
                Child = new TextBlock { Text = "Font" }
            };
            Grid.SetRow(labelBorder, 1);
            groupLikeGrid.Children.Add(labelBorder);

            RibbonMetadata.IsRibbonGroup(groupLikeGrid).Should().BeFalse();
            RibbonMetadata.TryGetGroupName(groupLikeGrid, out _).Should().BeFalse(
                "caption shape alone should not make arbitrary grids participate in adaptive ribbon layout");
        });
    }

    [Fact]
    public void GroupName_TrimsAttachedMetadataBeforeAdaptiveLookup()
    {
        StaTestRunner.Run(() =>
        {
            var metadataGroup = new Grid();
            RibbonMetadata.SetGroupName(metadataGroup, "  Page Setup  ");

            RibbonMetadata.TryGetGroupName(metadataGroup, out var metadataName).Should().BeTrue();
            metadataName.Should().Be("Page Setup");
        });
    }

    [Fact]
    public void CollapsedChevron_UsesAttachedRoleOnly()
    {
        StaTestRunner.Run(() =>
        {
            var chevron = new TextBlock { Tag = "RibbonIcon", Text = "\uE70D" };

            RibbonMetadata.IsCommandIcon(chevron).Should().BeFalse();
            RibbonMetadata.IsCollapsedChevron(chevron).Should().BeFalse();

            RibbonMetadata.SetRole(chevron, RibbonMetadataRole.CollapsedChevron);

            RibbonMetadata.IsCommandIcon(chevron).Should().BeTrue();
            RibbonMetadata.IsCollapsedChevron(chevron).Should().BeTrue();
        });
    }

    [Fact]
    public void DropdownChevronAndHandlerState_UseExplicitMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var chevron = new TextBlock();
            var button = new Button();

            RibbonMetadata.SetRole(chevron, RibbonMetadataRole.DropdownChevron);
            RibbonMetadata.SetDropdownMenuButton(button, true);
            RibbonMetadata.SetDropdownZoneHandlerAttached(button, true);
            RibbonMetadata.SetDropdownZoneHighlightAttached(button, true);

            RibbonMetadata.IsDropdownChevron(chevron).Should().BeTrue();
            RibbonMetadata.IsDropdownMenuButton(button).Should().BeTrue();
            RibbonMetadata.GetDropdownMenuButton(button).Should().BeTrue();
            RibbonMetadata.IsCommandIcon(chevron).Should().BeFalse();
            RibbonMetadata.GetDropdownZoneHandlerAttached(button).Should().BeTrue();
            RibbonMetadata.GetDropdownZoneHighlightAttached(button).Should().BeTrue();
        });
    }
}
