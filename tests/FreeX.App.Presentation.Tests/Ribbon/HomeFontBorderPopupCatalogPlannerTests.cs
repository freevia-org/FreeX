using FluentAssertions;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Ribbon;

public sealed class HomeFontBorderPopupCatalogPlannerTests
{
    [Fact]
    public void BorderPopupGroups_ExposeClassifiedSwatchAndLineStyleRows()
    {
        HomeFontBorderPopupCatalogPlanner.BorderLineColorSwatches
            .Select(swatch => (swatch.Label, swatch.HexColor))
            .Should()
            .Equal(
                ("Black", "#000000"),
                ("Gray", "#808080"),
                ("Accent 1", "#156082"),
                ("Accent 2", "#E97132"));

        HomeFontBorderPopupCatalogPlanner.BorderLineStyles
            .Select(style => (style.Label, style.Style))
            .Should()
            .Equal(
                ("Thin", BorderStyle.Thin),
                ("Medium", BorderStyle.Medium),
                ("Thick", BorderStyle.Thick),
                ("Dashed", BorderStyle.Dashed),
                ("Dotted", BorderStyle.Dotted),
                ("Double", BorderStyle.Double));

        HomeFontBorderPopupCatalogPlanner.ClassifiedFontBorderRowsCovered
            .Should()
            .BeEquivalentTo(
                "Accent 1",
                "Accent 2",
                "Black",
                "Gray",
                "Dashed",
                "Dotted",
                "Double",
                "Medium",
                "Thick",
                "Thin");
    }

    [Fact]
    public void FontColorPopupGroups_ExposeSwatchesWithoutInventingHandlersForEverySwatch()
    {
        HomeFontBorderPopupCatalogPlanner.FontColorSwatches
            .Select(swatch => (swatch.Label, swatch.HexColor))
            .Should()
            .ContainInOrder(
                ("Black", "#000000"),
                ("Red", "#FF0000"),
                ("Green", "#008000"),
                ("Blue", "#0000FF"),
                // NOTE: the FONT color swatches still carry the legacy Office 2013-2021 accents and are
                // not theme-linked -- unlike the BORDER line-color swatches above, which now follow the
                // workbook theme (freex-border-accent-swatch-F1). Same gap, different picker; untouched
                // here deliberately rather than silently changed.
                ("Accent 1", "#4472C4"),
                ("Accent 2", "#ED7D31"));

        HomeFontBorderPopupCatalogPlanner.FontColorSwatches
            .Where(swatch => swatch.Label.StartsWith("Accent ", StringComparison.Ordinal))
            .Should()
            .OnlyContain(swatch => swatch.BoundCommandId == null);

        HomeFontBorderPopupCatalogPlanner.FontColorItems
            .Should()
            .ContainInOrder("Black", "Red", "Green", "Blue", "Accent 1", "Accent 2", "More Colors");
    }
}
