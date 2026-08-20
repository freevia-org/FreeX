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

    [Fact]
    public void ResolveEffectiveCellFontName_AptosNarrowAvailable_ReturnsAptosNarrowWithNormalStretch()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Aptos Narrow", FontScheme = CellFontScheme.Minor };

        // When the cloud font IS enumerable (isAvailable returns true for "Aptos Narrow"),
        // resolution must short-circuit and return ("Aptos Narrow", Normal) — not the Calibri fallback.
        var resolved = GridView.ResolveEffectiveCellFontName(style, theme, name => name == "Aptos Narrow");

        resolved.Should().Be("Aptos Narrow",
            "when Aptos Narrow is available (e.g. via cloud font cache), it should be used directly without falling back to Calibri");
    }

    // shared-theme-fonts F1: modern default-theme workbooks store the resolved concrete face
    // ("Aptos"/"Aptos Display") alongside the scheme marker, exactly like legacy workbooks stored
    // "Calibri"/"Calibri Light". Those default placeholder names must keep following the theme when
    // Theme Fonts changes -- otherwise the vast majority of real (Aptos-default) cells never re-font,
    // and the WPF grid disagrees with every other resolution path (CellStyle.ResolveEffectiveFontName,
    // used by Avalonia's on-screen grid, PDF export, HTML/clipboard export, and ODS export).
    [Fact]
    public void ResolveEffectiveCellFontName_MinorScheme_AptosPlaceholder_FollowsThemeFontChange()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("Aptos Display", "Aptos");
        var theme2 = theme1.WithFonts("Aptos Display", "Arial");
        var style = new CellStyle { FontName = "Aptos", FontScheme = CellFontScheme.Minor };

        GridView.ResolveEffectiveCellFontName(style, theme1).Should().Be("Aptos");
        GridView.ResolveEffectiveCellFontName(style, theme2).Should().Be("Arial",
            "the modern 'Aptos' explicit name is a default theme-font placeholder, just like legacy " +
            "'Calibri', so it must keep following the workbook theme after a Theme Fonts change");

        // The WPF grid path must agree with every other resolution path (Avalonia, PDF, HTML, ODS),
        // which all go through CellStyle.ResolveEffectiveFontName -- not merely produce some literal.
        GridView.ResolveEffectiveCellFontName(style, theme2)
            .Should().Be(style.ResolveEffectiveFontName(theme2),
                "the on-screen WPF grid must render the same font as PDF/HTML/ODS export and the Avalonia grid");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_MajorScheme_AptosDisplayPlaceholder_FollowsThemeFontChange()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("Aptos Display", "Aptos");
        var theme2 = theme1.WithFonts("Georgia", "Aptos");
        var style = new CellStyle { FontName = "Aptos Display", FontScheme = CellFontScheme.Major };

        GridView.ResolveEffectiveCellFontName(style, theme1).Should().Be("Aptos Display");
        GridView.ResolveEffectiveCellFontName(style, theme2).Should().Be("Georgia",
            "the modern 'Aptos Display' explicit name is a default theme-font placeholder for the " +
            "major (heading) scheme, just like legacy 'Calibri Light', so it must follow the theme");

        GridView.ResolveEffectiveCellFontName(style, theme2)
            .Should().Be(style.ResolveEffectiveFontName(theme2),
                "the on-screen WPF grid must render the same heading font as the other export/render paths");
    }

    [Fact]
    public void ResolveEffectiveCellFontName_MajorScheme_CalibriLightPlaceholder_FollowsThemeFontChange()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("Calibri Light", "Calibri");
        var theme2 = theme1.WithFonts("Georgia", "Calibri");
        var style = new CellStyle { FontName = "Calibri Light", FontScheme = CellFontScheme.Major };

        GridView.ResolveEffectiveCellFontName(style, theme2).Should().Be("Georgia",
            "the legacy 'Calibri Light' major placeholder must follow the theme the same way the " +
            "legacy 'Calibri' minor placeholder already did");
    }

    // Sibling no-regression: a genuinely distinct explicit face (Aptos Narrow, pinned e.g. by a
    // PivotTable style) is NOT a default theme-font placeholder and must keep being preserved verbatim
    // even when the theme's minor font changes -- this is the real-Excel-COM-verified behavior from
    // "Preserve explicit Aptos Narrow cell fonts" and must not be disturbed by the F1 fix.
    [Fact]
    public void ResolveEffectiveCellFontName_AptosNarrow_StillPreservedAcrossThemeFontChange()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("Aptos Display", "Aptos");
        var theme2 = theme1.WithFonts("Aptos Display", "Arial");
        var style = new CellStyle { FontName = "Aptos Narrow", FontScheme = CellFontScheme.Minor };

        GridView.ResolveEffectiveCellFontName(style, theme1, name => name == "Aptos Narrow")
            .Should().Be("Aptos Narrow");
        GridView.ResolveEffectiveCellFontName(style, theme2, name => name == "Aptos Narrow")
            .Should().Be("Aptos Narrow",
                "Aptos Narrow is a genuinely distinct pinned face, not a default theme placeholder, " +
                "so it must NOT be replaced when the theme's minor font changes");
    }
}
