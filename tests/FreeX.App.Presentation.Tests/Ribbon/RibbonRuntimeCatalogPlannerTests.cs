using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class RibbonRuntimeCatalogPlannerTests
{
    [Fact]
    public void GetSurfaces_PublishesConditionalFormattingPopupPseudoCommandEvidence()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces(
            static key => key,
            [new RibbonRuntimeCatalogNumberFormatOption("General")]);

        var surface = surfaces.Should().ContainSingle(s => s.CommandTitle == "Conditional Formatting Popup").Subject;

        surface.TabHeader.Should().Be("Home");
        surface.InventoryRow.Should().Be("Conditional Formatting");
        surface.Source.Should().Be(nameof(ConditionalFormatPresetGalleryPlanner));
        surface.ItemCount.Should().Be(ConditionalFormatPresetGalleryPlanner.PopupItems.Count);
        surface.Groups.Select(group => group.Name)
            .Should()
            .Equal("Highlight Cells Rules", "Top/Bottom Rules", "Gallery Families", "Icon Sets", "Rules");
        surface.Groups.SelectMany(group => group.Items)
            .Should()
            .ContainInOrder(
                "Greater Than",
                "Less Than",
                "Between",
                "Equal To",
                "Text that Contains",
                "A Date Occurring",
                "Duplicate Values",
                "Top 10 Items",
                "Top 10%",
                "Bottom 10 Items",
                "Bottom 10%",
                "Above Average",
                "Below Average",
                "Data Bars",
                "Color Scales",
                "3 Arrows",
                "3 Arrows (Gray)",
                "4 Arrows",
                "4 Arrows (Gray)",
                "5 Arrows",
                "5 Arrows (Gray)",
                "3 Traffic Lights",
                "3 Traffic Lights (Rimmed)",
                "3 Signs",
                "3 Symbols",
                "3 Symbols (Uncircled)",
                "3 Flags",
                "4 Traffic Lights",
                "4 Red To Black",
                "4 Ratings",
                "5 Ratings",
                "5 Quarters",
                "5 Boxes",
                "More Rules");
    }
}
