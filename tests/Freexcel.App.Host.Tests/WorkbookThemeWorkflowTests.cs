using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class WorkbookThemeWorkflowTests
{
    [Fact]
    public void CreateColorfulTheme_UsesNamedThemeFontsEffectsAndPalette()
    {
        var theme = WorkbookThemeWorkflow.CreateColorfulTheme();

        theme.Name.Should().Be("Freexcel Colorful");
        theme.MajorFontName.Should().Be("Aptos Display");
        theme.MinorFontName.Should().Be("Aptos");
        theme.EffectsName.Should().Be("Office");
        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(233, 113, 50));
    }

    [Fact]
    public void ApplyGrayscaleColors_PreservesExistingNameFontsAndEffects()
    {
        var theme = WorkbookTheme.Office
            .WithName("Custom")
            .WithFonts("Arial", "Arial")
            .WithEffects("Subtle");

        var updated = WorkbookThemeWorkflow.ApplyGrayscaleColors(theme);

        updated.Name.Should().Be("Custom");
        updated.MajorFontName.Should().Be("Arial");
        updated.MinorFontName.Should().Be("Arial");
        updated.EffectsName.Should().Be("Subtle");
        updated.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(89, 89, 89));
    }

    [Fact]
    public void CreateCustomTheme_UpdatesMetadataAndKeepsPalette()
    {
        var theme = WorkbookThemeWorkflow.CreateCustomTheme(
            WorkbookTheme.Office,
            " My Theme ",
            "Georgia",
            "Verdana",
            "Refined");

        theme.Name.Should().Be("My Theme");
        theme.MajorFontName.Should().Be("Georgia");
        theme.MinorFontName.Should().Be("Verdana");
        theme.EffectsName.Should().Be("Refined");
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1));
    }
}
