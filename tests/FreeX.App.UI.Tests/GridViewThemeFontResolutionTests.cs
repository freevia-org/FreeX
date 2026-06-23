using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests that GridView.ResolveEffectiveCellFontName correctly dispatches through the
/// workbook theme's minor/major font when the cell's FontScheme is set, and falls back
/// to the cell's own FontName when FontScheme is None.
/// </summary>
public sealed class GridViewThemeFontResolutionTests
{
    [Fact]
    public void ResolveEffectiveCellFontName_MinorScheme_ReturnsMinorThemeFont()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Minor };

        var resolved = GridView.ResolveEffectiveCellFontName(style, theme);

        resolved.Should().Be("BodyFont");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_MinorSchemePreservesExplicitModernFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Aptos Narrow", FontScheme = CellFontScheme.Minor };

        var resolved = GridView.ResolveEffectiveCellFontName(style, theme, name => name == "Aptos Narrow");

        resolved.Should().Be("Aptos Narrow",
            "Excel stores the resolved concrete face alongside the theme scheme and displays that face");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_AptosNarrowFallsBackToCalibriCondensedWhenAvailable()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Aptos Narrow", FontScheme = CellFontScheme.Minor };

        var resolved = GridView.ResolveEffectiveCellFontName(style, theme, name => name == "Arial Narrow" || name == "Calibri");

        resolved.Should().Be("Calibri",
            "Office may report Aptos Narrow even when WPF cannot enumerate it, and Calibri with condensed stretch more closely matches Excel's text weight");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_MajorScheme_ReturnsMajorThemeFont()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Major };

        var resolved = GridView.ResolveEffectiveCellFontName(style, theme);

        resolved.Should().Be("HeadingFont");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_NoneScheme_ReturnsStyleFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Arial", FontScheme = CellFontScheme.None };

        var resolved = GridView.ResolveEffectiveCellFontName(style, theme);

        resolved.Should().Be("Arial");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_NullStyle_ReturnsCalibri()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");

        var resolved = GridView.ResolveEffectiveCellFontName(null, theme);

        resolved.Should().Be("Calibri",
            "null style should fall back to Calibri (the default font display name)");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_MinorScheme_ChangesWithThemeFontChange()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var theme2 = theme1.WithFonts("HeadingFont", "Arial");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Minor };

        GridView.ResolveEffectiveCellFontName(style, theme1).Should().Be("BodyFont");
        GridView.ResolveEffectiveCellFontName(style, theme2).Should().Be("Arial",
            "the effective font should reflect the new theme minor font when the theme changes");
    }
}
