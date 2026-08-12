using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

public sealed class HomeFontBorderPopupCatalogTests
{
    [Fact]
    public void RuntimeCatalog_ExposesFontColorAndBorderPopupRowsForAvaloniaParityEvidence()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces(
            static key => key,
            [new RibbonRuntimeCatalogNumberFormatOption("General")],
            [
                new("Accounting Number Format US Dollar", "US Dollar ($)"),
                new("Accounting Number Format Euro", "Euro (EUR)"),
                new("Accounting Number Format British Pound", "British Pound (GBP)"),
                new("Accounting Number Format Japanese Yen", "Japanese Yen (JPY)"),
            ]);

        surfaces.Single(surface => surface.CommandTitle == "Accounting Symbol Dropdown")
            .Groups.SelectMany(group => group.Items)
            .Should()
            .Contain([
                "Accounting Number Format US Dollar",
                "Accounting Number Format Euro",
                "Accounting Number Format British Pound",
                "Accounting Number Format Japanese Yen",
            ]);

        surfaces.Single(surface => surface.CommandTitle == "Font Color Popup")
            .Groups.SelectMany(group => group.Items)
            .Should()
            .Contain(["Black", "Red", "Green", "Blue", "Accent 1", "Accent 2", "More Colors"]);

        surfaces.Single(surface => surface.CommandTitle == "Borders Popup")
            .Groups.SelectMany(group => group.Items)
            .Should()
            .Contain([
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
            ]);
    }

    [Fact]
    public void CatalogRows_DoNotRequirePlaceholderAvaloniaHandlersForEveryPopupChoice()
    {
        FreeXRibbonCommandCatalog.GetRequired("Font Color").Value.Should().Be("Font Color");
        FreeXRibbonCommandCatalog.GetRequired("Borders").Value.Should().Be("Borders");
        FreeXRibbonCommandCatalog.GetRequired("All Borders").Value.Should().Be("All Borders");

        HomeFontBorderPopupCatalogPlanner.FontColorSwatches
            .Where(swatch => swatch.BoundCommandId is not null)
            .Select(swatch => swatch.BoundCommandId)
            .Should()
            .Equal("home.fontColorAuto", "home.fontColorRed", "home.fontColorGreen", "home.fontColorBlue");

        HomeFontBorderPopupCatalogPlanner.ClassifiedFontBorderRowsCovered
            .Should()
            .Contain(["Black", "Gray", "Accent 1", "Accent 2", "Thin", "Medium", "Thick", "Dashed", "Dotted", "Double"]);
    }
}
