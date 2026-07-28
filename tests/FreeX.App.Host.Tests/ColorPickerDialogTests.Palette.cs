using FreeX.App.Services;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows.Controls;

namespace FreeX.App.Host.Tests;

public sealed partial class ColorPickerDialogTests
{
    [Fact]
    public void BuildDefaultSwatches_ReturnsNamedHexColorsWithModelColorValues()
    {
        var swatches = ColorPickerDialog.BuildDefaultSwatches();

        swatches.Should().Contain(sw => sw.Hex == "#000000" && sw.Color == CellColor.Black);
        swatches.Should().Contain(sw => sw.Hex == "#FFFFFF" && sw.Color == CellColor.White);
        swatches.Should().OnlyContain(sw => sw.Hex.Length == 7 && sw.Hex[0] == '#');
        swatches.Select(sw => sw.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildThemePalette_ReturnsExcelLikeThemeColumnsWithShades()
    {
        var columns = ColorPickerDialog.BuildThemePalette();

        columns.Should().HaveCount(10);
        columns.Should().OnlyContain(column => column.Shades.Count == 6);
        columns.Select(column => column.Name).Should().Equal(
            "Text/Background Dark 1",
            "Text/Background Light 1",
            "Text/Background Dark 2",
            "Text/Background Light 2",
            "Accent 1",
            "Accent 2",
            "Accent 3",
            "Accent 4",
            "Accent 5",
            "Accent 6");
        columns[0].Shades[0].Hex.Should().Be("#000000");
        columns[1].Shades[0].Hex.Should().Be("#FFFFFF");
        columns[4].Shades[0].Hex.Should().Be("#156082");
        columns.SelectMany(column => column.Shades).Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildStandardSwatches_ReturnsExcelLikeStandardColorRow()
    {
        var swatches = ColorPickerDialog.BuildStandardSwatches();

        swatches.Should().HaveCount(10);
        swatches.Select(swatch => swatch.Hex).Should().Contain(["#C00000", "#FFFF00", "#7030A0"]);
    }

    [Fact]
    public void BuildCustomSpectrumSwatches_ReturnsHueAndSaturationGrid()
    {
        var swatches = ColorPickerDialog.BuildCustomSpectrumSwatches();

        swatches.Should().HaveCount(48);
        swatches.Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
        swatches.Should().Contain(swatch => swatch.Hex == "#FF0000");
        swatches.Should().Contain(swatch => swatch.Hex == "#00FF00");
        swatches.Should().Contain(swatch => swatch.Hex == "#0000FF");
        swatches.Should().Contain(swatch => swatch.Color.R != swatch.Color.G || swatch.Color.G != swatch.Color.B);
    }

    [Fact]
    public void PalettePlanner_ScalesColorAndChoosesReadableForeground()
    {
        CellColorPalettePlanner.ScaleColor(new CellColor(0x40, 0x80, 0xC0), 0.5)
            .Should()
            .Be(new CellColor(0x20, 0x40, 0x60));

        CellColorPalettePlanner.ScaleColor(new CellColor(0xF0, 0x80, 0x40), 2)
            .Should()
            .Be(new CellColor(0xFF, 0xFF, 0x80));

        CellColorPalettePlanner.NeedsDarkForeground(CellColor.White).Should().BeTrue();
        CellColorPalettePlanner.NeedsDarkForeground(CellColor.Black).Should().BeFalse();
    }

    [Fact]
    public void ThemePanel_AddsSwatchesByRowsSoExcelColumnsStayVertical()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ColorPickerDialog();
            try
            {
                var panel = (Panel)dialog.FindName("ThemeColorsPanel");
                var firstRow = panel.Children
                    .OfType<Button>()
                    .Take(10)
                    .Select(button => (CellColor)button.Tag)
                    .ToArray();

                firstRow.Should().Equal(
                    new CellColor(0x00, 0x00, 0x00),
                    new CellColor(0xFF, 0xFF, 0xFF),
                    new CellColor(0x44, 0x54, 0x6A),
                    new CellColor(0xE7, 0xE6, 0xE6),
                    new CellColor(0x15, 0x60, 0x82),
                    new CellColor(0xE9, 0x71, 0x32),
                    new CellColor(0x19, 0x6B, 0x24),
                    new CellColor(0x0F, 0x9E, 0xD5),
                    new CellColor(0xA0, 0x2B, 0x93),
                    new CellColor(0x4E, 0xA7, 0x2E));
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
