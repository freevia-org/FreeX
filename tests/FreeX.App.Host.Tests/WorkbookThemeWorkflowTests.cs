using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookThemeWorkflowTests
{
    [Fact]
    public void CreateColorfulTheme_UsesNamedThemeFontsEffectsAndPalette()
    {
        var theme = WorkbookThemeWorkflow.CreateColorfulTheme();

        theme.Name.Should().Be("FreeX Colorful");
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
    public void ThemeColorSlots_All_MatchesWorkbookThemeColorSlotEnum()
    {
        WorkbookThemeColorSlots.All.Should().Equal(
            WorkbookThemeColorSlot.Dark1,
            WorkbookThemeColorSlot.Light1,
            WorkbookThemeColorSlot.Dark2,
            WorkbookThemeColorSlot.Light2,
            WorkbookThemeColorSlot.Accent1,
            WorkbookThemeColorSlot.Accent2,
            WorkbookThemeColorSlot.Accent3,
            WorkbookThemeColorSlot.Accent4,
            WorkbookThemeColorSlot.Accent5,
            WorkbookThemeColorSlot.Accent6,
            WorkbookThemeColorSlot.Hyperlink,
            WorkbookThemeColorSlot.FollowedHyperlink);

        WorkbookThemeColorSlots.All.Should().BeEquivalentTo(Enum.GetValues<WorkbookThemeColorSlot>());
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

    [Fact]
    public void WorkbookThemeDialogPlanner_CreatesCustomThemeWithValidatedColors()
    {
        var colors = WorkbookThemeColorSlots.All
            .ToDictionary(slot => slot, _ => "#010203");
        colors[WorkbookThemeColorSlot.Accent1] = "#112233";

        WorkbookThemeDialogPlanner.TryCreateTheme(
                WorkbookTheme.Office,
                " Demo ",
                "Georgia",
                "Verdana",
                "Refined",
                colors,
                out var theme,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        theme.Name.Should().Be("Demo");
        theme.MajorFontName.Should().Be("Georgia");
        theme.MinorFontName.Should().Be("Verdana");
        theme.EffectsName.Should().Be("Refined");
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(0x11, 0x22, 0x33));
    }

    [Fact]
    public void WorkbookThemeDialogPlanner_ReportsInvalidColorSlot()
    {
        var colors = WorkbookThemeColorSlots.All
            .ToDictionary(slot => slot, _ => "#010203");
        colors[WorkbookThemeColorSlot.Hyperlink] = "not-a-color";

        WorkbookThemeDialogPlanner.TryCreateTheme(
                WorkbookTheme.Office,
                "Demo",
                "Georgia",
                "Verdana",
                "Refined",
                colors,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be(new WorkbookThemeDialogValidationError(
            WorkbookThemeColorSlot.Hyperlink,
            "Enter theme colors as #RRGGBB values."));
    }
}
