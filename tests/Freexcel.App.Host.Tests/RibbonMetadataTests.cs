using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonMetadataTests
{
    [Fact]
    public void RoleHelpers_ReadAttachedMetadataBeforeLegacyTags()
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
    public void RoleHelpers_FallBackToLegacyTagsForStaticXamlCompatibility()
    {
        StaTestRunner.Run(() =>
        {
            RibbonMetadata.IsCommandLabel(new TextBlock { Tag = "RibbonLabel" }).Should().BeTrue();
            RibbonMetadata.IsCommandIcon(new TextBlock { Tag = "RibbonIcon" }).Should().BeTrue();
            RibbonMetadata.IsCollapsedGroupButton(new Button { Tag = "RibbonCollapsedGroupButton" }).Should().BeTrue();
        });
    }

    [Fact]
    public void CompactWidths_ReadAttachedMetadataAndLegacyTags()
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
                .BeTrue();
            legacyFull.Should().Be(58.5);
            legacyCompact.Should().Be(30);
        });
    }

    [Fact]
    public void CommandContentLayout_ReadsAttachedMetadataAndLegacyTags()
    {
        StaTestRunner.Run(() =>
        {
            var metadataGrid = new Grid();
            RibbonMetadata.SetCommandContentLayout(metadataGrid, RibbonCommandContentLayout.Small);

            RibbonMetadata.TryGetCommandContentLayout(metadataGrid, out var metadataLayout).Should().BeTrue();
            metadataLayout.Should().Be(RibbonCommandContentLayout.Small);

            RibbonMetadata.TryGetCommandContentLayout(new Grid { Tag = "RibbonCommandContent:L" }, out var legacyLayout)
                .Should()
                .BeTrue();
            legacyLayout.Should().Be(RibbonCommandContentLayout.Large);
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
    public void LegacyRibbonIconChevron_IsNotTreatedAsCommandIconForFootprintScaling()
    {
        StaTestRunner.Run(() =>
        {
            var chevron = new TextBlock { Tag = "RibbonIcon", Text = "\uE70D" };

            RibbonMetadata.IsCommandIcon(chevron).Should().BeTrue();
            RibbonMetadata.IsCollapsedChevron(chevron).Should().BeTrue();
        });
    }
}
