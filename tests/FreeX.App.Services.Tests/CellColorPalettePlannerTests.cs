using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellColorPalettePlannerTests
{
    [Fact]
    public void BuildMenuPlan_ExposesThemeStandardRecentAndCustomSpectrumSections()
    {
        var plan = CellColorPalettePlanner.BuildMenuPlan(
            [
                new CellColor(0x12, 0x34, 0x56),
                new CellColor(0xAA, 0xBB, 0xCC),
                new CellColor(0x12, 0x34, 0x56)
            ]);

        plan.Sections.Select(section => section.Kind).Should().Equal(
            CellColorPaletteSectionKind.Theme,
            CellColorPaletteSectionKind.Standard,
            CellColorPaletteSectionKind.Recent,
            CellColorPaletteSectionKind.CustomSpectrum);

        var theme = plan.Sections[0];
        theme.ThemeColumns.Should().HaveCount(10);
        theme.Swatches.Should().HaveCount(60);

        plan.Sections[1].Swatches.Select(swatch => swatch.Hex)
            .Should()
            .Equal(CellColorPalettePlanner.BuildStandardSwatches().Select(swatch => swatch.Hex));
        plan.Sections[2].Swatches.Select(swatch => swatch.Hex)
            .Should()
            .Equal("#123456", "#AABBCC");
        plan.Sections[3].Swatches.Select(swatch => swatch.Hex)
            .Should()
            .Equal(CellColorPalettePlanner.BuildCustomSpectrumSwatches().Select(swatch => swatch.Hex));
    }

    [Fact]
    public void BuildMenuPlan_SkipsEmptyRecentSectionAndCanOmitCustomSpectrum()
    {
        var plan = CellColorPalettePlanner.BuildMenuPlan(includeCustomSpectrum: false);

        plan.Sections.Select(section => section.Kind).Should().Equal(
            CellColorPaletteSectionKind.Theme,
            CellColorPaletteSectionKind.Standard);
    }

    [Fact]
    public void BuildDefaultSwatches_ReturnsDedupedThemeAndStandardColors()
    {
        var swatches = CellColorPalettePlanner.BuildDefaultSwatches();

        // The default theme row now derives from the workbook's actual (Aptos) theme rather than the
        // frozen legacy Office 2013-2021 palette, so one fewer accent shade collides with the standard
        // color row -> 70 unique swatches (was 69 under the legacy palette).
        swatches.Should().HaveCount(70);
        swatches.Should().Contain(swatch => swatch.Hex == "#000000" && swatch.Color == CellColor.Black);
        swatches.Should().Contain(swatch => swatch.Hex == "#FFFFFF" && swatch.Color == CellColor.White);
        swatches.Should().OnlyContain(swatch => swatch.Hex.Length == 7 && swatch.Hex[0] == '#');
        swatches.Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildThemePalette_ReturnsExcelLikeThemeColumnsWithShades()
    {
        var columns = CellColorPalettePlanner.BuildThemePalette();

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
        // Accent 1 now reflects the workbook's real default theme (Aptos, #156082), not the frozen
        // legacy Office 2013-2021 accent (#4472C4).
        columns[4].Shades[0].Hex.Should().Be("#156082");
        columns.SelectMany(column => column.Shades).Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildStandardSwatches_ReturnsExcelLikeStandardColorRow()
    {
        var swatches = CellColorPalettePlanner.BuildStandardSwatches();

        swatches.Should().HaveCount(10);
        swatches.Select(swatch => swatch.Hex).Should().Equal(
            "#C00000",
            "#FF0000",
            "#FFC000",
            "#FFFF00",
            "#92D050",
            "#00B050",
            "#00B0F0",
            "#0070C0",
            "#002060",
            "#7030A0");
    }

    [Fact]
    public void BuildRecentSwatches_DedupesDisplayOrderAndCapsResults()
    {
        var swatches = CellColorPalettePlanner.BuildRecentSwatches(
            [
                new CellColor(0x01, 0x02, 0x03),
                new CellColor(0xAA, 0xBB, 0xCC),
                new CellColor(0x01, 0x02, 0x03),
                new CellColor(0x11, 0x22, 0x33)
            ],
            capacity: 2);

        swatches.Select(swatch => swatch.Hex).Should().Equal("#010203", "#AABBCC");
        swatches.Select(swatch => swatch.Color).Should().Equal(
            new CellColor(0x01, 0x02, 0x03),
            new CellColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void BuildRecentSwatches_ReturnsEmptyWhenInputOrCapacityIsEmpty()
    {
        CellColorPalettePlanner.BuildRecentSwatches(null).Should().BeEmpty();
        CellColorPalettePlanner.BuildRecentSwatches([CellColor.Black], capacity: 0).Should().BeEmpty();
    }

    [Fact]
    public void PromoteRecentColor_MovesSelectedColorToFrontDedupeAndCaps()
    {
        var colors = CellColorPalettePlanner.PromoteRecentColor(
            [
                new CellColor(0x10, 0x20, 0x30),
                new CellColor(0xAA, 0xBB, 0xCC),
                new CellColor(0x44, 0x55, 0x66)
            ],
            new CellColor(0xAA, 0xBB, 0xCC),
            capacity: 3);

        colors.Should().Equal(
            new CellColor(0xAA, 0xBB, 0xCC),
            new CellColor(0x10, 0x20, 0x30),
            new CellColor(0x44, 0x55, 0x66));

        CellColorPalettePlanner.PromoteRecentColor(colors, new CellColor(0xEE, 0xDD, 0xCC), capacity: 2)
            .Should()
            .Equal(new CellColor(0xEE, 0xDD, 0xCC), new CellColor(0xAA, 0xBB, 0xCC));
    }

    [Fact]
    public void BuildCustomSpectrumSwatches_ReturnsHueAndSaturationGrid()
    {
        var swatches = CellColorPalettePlanner.BuildCustomSpectrumSwatches();

        swatches.Should().HaveCount(48);
        swatches.Select(swatch => swatch.Hex).Should().OnlyHaveUniqueItems();
        swatches.Should().Contain(swatch => swatch.Hex == "#FF0000");
        swatches.Should().Contain(swatch => swatch.Hex == "#00FF00");
        swatches.Should().Contain(swatch => swatch.Hex == "#0000FF");
        swatches.Should().Contain(swatch => swatch.Color.R != swatch.Color.G || swatch.Color.G != swatch.Color.B);
    }

    [Fact]
    public void ScaleColor_ClampsScaledComponents()
    {
        CellColorPalettePlanner.ScaleColor(new CellColor(0x40, 0x80, 0xC0), 0.5)
            .Should()
            .Be(new CellColor(0x20, 0x40, 0x60));

        CellColorPalettePlanner.ScaleColor(new CellColor(0xF0, 0x80, 0x40), 2)
            .Should()
            .Be(new CellColor(0xFF, 0xFF, 0x80));
    }

    [Fact]
    public void NeedsDarkForeground_ChoosesReadableTextForSwatches()
    {
        CellColorPalettePlanner.NeedsDarkForeground(CellColor.White).Should().BeTrue();
        CellColorPalettePlanner.NeedsDarkForeground(CellColor.Black).Should().BeFalse();
    }

    [Fact]
    public void FormatHexColor_ReturnsUppercaseRgbHex()
    {
        CellColorPalettePlanner.FormatHexColor(new CellColor(0x0A, 0xB0, 0xFF))
            .Should()
            .Be("#0AB0FF");
    }

    [Theory]
    [InlineData("#0AB0FF", 0x0A, 0xB0, 0xFF)]
    [InlineData("0ab0ff", 0x0A, 0xB0, 0xFF)]
    [InlineData("  #000000  ", 0x00, 0x00, 0x00)]
    public void TryParseHexColor_ParsesSixDigitRgbWithOrWithoutHashAndWhitespace(
        string text, byte red, byte green, byte blue)
    {
        CellColorPalettePlanner.TryParseHexColor(text, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(red, green, blue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGHHII")]
    public void TryParseHexColor_RejectsInvalidInput(string? text)
    {
        CellColorPalettePlanner.TryParseHexColor(text, out var color).Should().BeFalse();
        color.Should().Be(default(CellColor));
    }

    [Fact]
    public void TryParseHexColor_RoundTripsFormatHexColor()
    {
        var original = new CellColor(0x12, 0x34, 0x56);
        CellColorPalettePlanner.TryParseHexColor(CellColorPalettePlanner.FormatHexColor(original), out var parsed)
            .Should()
            .BeTrue();
        parsed.Should().Be(original);
    }
}
