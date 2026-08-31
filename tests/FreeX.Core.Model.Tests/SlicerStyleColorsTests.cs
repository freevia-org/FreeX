using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Verifies <see cref="SlicerStyleColors.Resolve"/> maps the built-in slicer style family
/// (SlicerStyleLight1..6) to theme-derived header/tile/selection colors so slicers theme like Excel.
/// Excel's mapping is Light2→Accent2, …, Light6→Accent6 (a uniform +1 shift from style number to slot).
/// </summary>
public sealed class SlicerStyleColorsTests
{
    private static WorkbookTheme ThemeWith(
        CellColor accent1,
        CellColor accent2,
        CellColor accent5,
        CellColor accent6)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>
        {
            [WorkbookThemeColorSlot.Dark1] = new(0, 0, 0),
            [WorkbookThemeColorSlot.Light1] = CellColor.White,
            [WorkbookThemeColorSlot.Accent1] = accent1,
            [WorkbookThemeColorSlot.Accent2] = accent2,
            [WorkbookThemeColorSlot.Accent3] = new(2, 2, 2),
            [WorkbookThemeColorSlot.Accent4] = new(3, 3, 3),
            [WorkbookThemeColorSlot.Accent5] = accent5,
            [WorkbookThemeColorSlot.Accent6] = accent6,
        };
        return WorkbookTheme.Office with { Colors = colors };
    }

    private static WorkbookTheme SimpleTheme()
        => ThemeWith(
            accent1: new CellColor(0x15, 0x60, 0x82),  // teal (Accent1)
            accent2: new CellColor(0xE9, 0x71, 0x32),  // orange (Accent2)
            accent5: new CellColor(0x5B, 0x9B, 0xD5),
            accent6: new CellColor(0x70, 0xAD, 0x47));

    [Fact]
    public void Resolve_NullOrLight1_UsesNeutralGrayHeader_NotAnAccent()
    {
        var theme = SimpleTheme();

        var defaultColors = SlicerStyleColors.Resolve(null, theme);
        var light1 = SlicerStyleColors.Resolve("SlicerStyleLight1", theme);

        // Light1 (and the null/unknown default) is the neutral gray look, not an accent-colored band.
        defaultColors.Should().Be(light1);
        light1.Header.Should().Be(new CellColor(245, 245, 245));
        light1.HeaderText.Should().Be(new CellColor(64, 64, 64));
        // Selection still tints from accent1 so "selected" reads.
        light1.SelectedTile.Should().NotBe(CellColor.White);
    }

    [Fact]
    public void Resolve_Light2_BorderIsAccent2_HeaderIsWhite()
    {
        // Excel: SlicerStyleLight2 → Accent2. The header background is WHITE with a dark caption;
        // the accent colour is used only for the outer border.
        var accent2 = new CellColor(0xE9, 0x71, 0x32);  // orange in the test theme
        var theme = SimpleTheme();

        var colors = SlicerStyleColors.Resolve("SlicerStyleLight2", theme);

        colors.Border.Should().Be(accent2, "Light2 outer border uses Accent2");
        colors.Header.Should().Be(CellColor.White, "Light2 header background is white, not accent-filled");
        colors.HeaderText.Should().Be(new CellColor(64, 64, 64), "Light2 caption text is dark, not white");
        colors.Tile.Should().Be(CellColor.White);
    }

    [Fact]
    public void Resolve_Light6_BorderIsAccent6_AndDiffersFromLight2()
    {
        var accent6 = new CellColor(0x70, 0xAD, 0x47);
        var theme = SimpleTheme();

        var light6 = SlicerStyleColors.Resolve("SlicerStyleLight6", theme);
        var light2 = SlicerStyleColors.Resolve("SlicerStyleLight2", theme);

        light6.Border.Should().Be(accent6, "Light6 maps to theme Accent6");
        light6.Border.Should().NotBe(light2.Border, "Light2 and Light6 must theme distinctly");
        // Both use white headers (accent-colored outer border only).
        light6.Header.Should().Be(CellColor.White);
    }

    [Fact]
    public void Resolve_UnknownStyle_FallsBackToNeutralDefault()
    {
        var theme = SimpleTheme();

        var unknown = SlicerStyleColors.Resolve("SlicerStyleOther1", theme);

        unknown.Should().Be(SlicerStyleColors.Resolve("SlicerStyleLight1", theme));
    }

    [Fact]
    public void Resolve_TrimsExactFamilyButRejectsTimelineFamilyAndWrongCase()
    {
        var theme = SimpleTheme();
        var light1 = SlicerStyleColors.Resolve("SlicerStyleLight1", theme);

        SlicerStyleColors.Resolve(" \tSlicerStyleLight2\r\n", theme).Border
            .Should().Be(theme.GetColor(WorkbookThemeColorSlot.Accent2));
        SlicerStyleColors.Resolve("TimeSlicerStyleLight2", theme).Should().Be(light1);
        SlicerStyleColors.Resolve("slicerStyleLight2", theme).Should().Be(light1);
    }

    [Fact]
    public void Resolve_NullTheme_ThrowsBeforeStyleResolution()
    {
        var act = () => SlicerStyleColors.Resolve("SlicerStyleLight2", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
