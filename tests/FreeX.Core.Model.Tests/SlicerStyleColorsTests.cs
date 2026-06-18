using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Verifies <see cref="SlicerStyleColors.Resolve"/> maps the built-in slicer style family
/// (SlicerStyleLight1..6) to theme-derived header/tile/selection colors so slicers theme like Excel.
/// </summary>
public sealed class SlicerStyleColorsTests
{
    private static WorkbookTheme ThemeWith(
        CellColor accent1,
        CellColor accent5)
    {
        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>
        {
            [WorkbookThemeColorSlot.Dark1] = new(0, 0, 0),
            [WorkbookThemeColorSlot.Light1] = CellColor.White,
            [WorkbookThemeColorSlot.Accent1] = accent1,
            [WorkbookThemeColorSlot.Accent2] = new(1, 1, 1),
            [WorkbookThemeColorSlot.Accent3] = new(2, 2, 2),
            [WorkbookThemeColorSlot.Accent4] = new(3, 3, 3),
            [WorkbookThemeColorSlot.Accent5] = accent5,
            [WorkbookThemeColorSlot.Accent6] = new(4, 4, 4),
        };
        return WorkbookTheme.Office with { Colors = colors };
    }

    [Fact]
    public void Resolve_NullOrLight1_UsesNeutralGrayHeader_NotAnAccent()
    {
        var theme = ThemeWith(accent1: new CellColor(0x44, 0x72, 0xC4), accent5: new CellColor(0x5B, 0x9B, 0xD5));

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
    public void Resolve_Light2_HeaderIsAccent1()
    {
        var accent1 = new CellColor(0x44, 0x72, 0xC4);
        var theme = ThemeWith(accent1, accent5: new CellColor(0x5B, 0x9B, 0xD5));

        var colors = SlicerStyleColors.Resolve("SlicerStyleLight2", theme);

        colors.Header.Should().Be(accent1, "Light2 maps to theme Accent1");
        colors.HeaderText.Should().Be(CellColor.White);
        colors.Tile.Should().Be(CellColor.White);
    }

    [Fact]
    public void Resolve_Light6_HeaderIsAccent5_AndDiffersFromLight2()
    {
        var accent1 = new CellColor(0x44, 0x72, 0xC4);
        var accent5 = new CellColor(0x5B, 0x9B, 0xD5);
        var theme = ThemeWith(accent1, accent5);

        var light6 = SlicerStyleColors.Resolve("SlicerStyleLight6", theme);
        var light2 = SlicerStyleColors.Resolve("SlicerStyleLight2", theme);

        light6.Header.Should().Be(accent5, "Light6 maps to theme Accent5");
        light6.Header.Should().NotBe(light2.Header, "Light2 and Light6 must theme distinctly");
    }

    [Fact]
    public void Resolve_UnknownStyle_FallsBackToNeutralDefault()
    {
        var theme = ThemeWith(accent1: new CellColor(10, 20, 30), accent5: new CellColor(40, 50, 60));

        var unknown = SlicerStyleColors.Resolve("SlicerStyleOther1", theme);

        unknown.Should().Be(SlicerStyleColors.Resolve("SlicerStyleLight1", theme));
    }
}
