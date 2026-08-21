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
            [new RibbonRuntimeCatalogNumberFormatOption("General")],
            AccountingSymbolOptions());

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

    [Fact]
    public void GetSurfaces_PublishesFontColorAndBorderPopupPseudoCommandEvidence()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces(
            static key => key,
            [new RibbonRuntimeCatalogNumberFormatOption("General")],
            AccountingSymbolOptions());

        var accounting = surfaces.Should().ContainSingle(s => s.CommandTitle == "Accounting Symbol Dropdown").Subject;
        accounting.TabHeader.Should().Be("Home");
        accounting.InventoryRow.Should().Be("Accounting/Date/Time");
        accounting.Source.Should().Be("HomeNumberFormatDropdownPlanner");
        accounting.Groups.Select(group => group.Name).Should().Equal("Symbols");
        accounting.Groups.SelectMany(group => group.Items)
            .Should()
            .ContainInOrder(
                "Accounting Number Format US Dollar",
                "Accounting Number Format Euro",
                "Accounting Number Format British Pound",
                "Accounting Number Format Japanese Yen");

        var fontColor = surfaces.Should().ContainSingle(s => s.CommandTitle == "Font Color Popup").Subject;
        fontColor.TabHeader.Should().Be("Home");
        fontColor.InventoryRow.Should().Be("Font Color");
        fontColor.Source.Should().Be(nameof(HomeFontBorderPopupCatalogPlanner));
        fontColor.Groups.Select(group => group.Name).Should().Equal("Swatches", "Actions");
        fontColor.Groups.SelectMany(group => group.Items)
            .Should()
            .ContainInOrder("Black", "Red", "Green", "Blue", "Accent 1", "Accent 2", "More Colors");

        var borders = surfaces.Should().ContainSingle(s => s.CommandTitle == "Borders Popup").Subject;
        borders.TabHeader.Should().Be("Home");
        borders.InventoryRow.Should().Be("Full Border Gallery");
        borders.Source.Should().Be(nameof(HomeFontBorderPopupCatalogPlanner));
        borders.Groups.Select(group => group.Name)
            .Should()
            .Equal("Presets", "Draw", "Line Color", "Line Style", "Actions");
        borders.Groups.SelectMany(group => group.Items)
            .Should()
            .ContainInOrder(
                "No Border",
                "All Borders",
                "Outside Borders",
                "Inside Borders",
                "Draw Border",
                "Draw Border Grid",
                "Erase Border",
                "Black",
                "Gray",
                "Accent 1",
                "Accent 2",
                "Thin",
                "Medium",
                "Thick",
                "Dashed",
                "Dotted",
                "Double",
                "More Borders");
    }

    private static IReadOnlyList<RibbonRuntimeCatalogAccountingSymbolOption> AccountingSymbolOptions() =>
    [
        new("Accounting Number Format US Dollar", "US Dollar ($)"),
        new("Accounting Number Format Euro", "Euro (EUR)"),
        new("Accounting Number Format British Pound", "British Pound (GBP)"),
        new("Accounting Number Format Japanese Yen", "Japanese Yen (JPY)"),
    ];
}
