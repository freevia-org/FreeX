using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia.Tests;

public sealed class HomeFontBorderPopupCatalogTests
{
    [Fact]
    public void RuntimeCatalog_ExposesFontColorAndBorderPopupRowsForAvaloniaParityEvidence()
    {
        var surfaces = RibbonRuntimeCatalogPlanner.GetSurfaces(
            static key => key,
            [new RibbonRuntimeCatalogNumberFormatOption("General")]);

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
        AvaloniaCommandIdAdapter.ToCanonical("home.fontColor").Should().Be("Font Color");
        AvaloniaCommandIdAdapter.ToCanonical("home.borders").Should().Be("Borders");
        AvaloniaCommandIdAdapter.ToCanonical("home.bordersAll").Should().Be("All Borders");

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
