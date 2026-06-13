using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Tests for CellFontScheme, CellStyle.ResolveEffectiveFontName, and StyleDiff font-scheme pinning.
/// </summary>
public sealed class CellFontSchemeTests
{
    // ── WorkbookTheme.ResolveSchemeFontName ────────────────────────────────────

    [Fact]
    public void ResolveSchemeFontName_MinorReturnsMinorFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");

        theme.ResolveSchemeFontName(CellFontScheme.Minor).Should().Be("BodyFont");
    }

    [Fact]
    public void ResolveSchemeFontName_MajorReturnsMajorFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");

        theme.ResolveSchemeFontName(CellFontScheme.Major).Should().Be("HeadingFont");
    }

    [Fact]
    public void ResolveSchemeFontName_NoneReturnsNull()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");

        theme.ResolveSchemeFontName(CellFontScheme.None).Should().BeNull();
    }

    // ── CellStyle.ResolveEffectiveFontName ────────────────────────────────────

    [Fact]
    public void ResolveEffectiveFontName_MinorSchemeResolvesToMinorFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Minor };

        style.ResolveEffectiveFontName(theme).Should().Be("BodyFont");
    }

    [Fact]
    public void ResolveEffectiveFontName_MajorSchemeResolvesToMajorFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Major };

        style.ResolveEffectiveFontName(theme).Should().Be("HeadingFont");
    }

    [Fact]
    public void ResolveEffectiveFontName_NoneSchemeResolvesToFontName()
    {
        var theme = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var style = new CellStyle { FontName = "Arial", FontScheme = CellFontScheme.None };

        style.ResolveEffectiveFontName(theme).Should().Be("Arial");
    }

    [Fact]
    public void ResolveEffectiveFontName_MinorSchemeUpdatesWhenThemeFontChanges()
    {
        var theme1 = WorkbookTheme.Office.WithFonts("HeadingFont", "BodyFont");
        var theme2 = theme1.WithFonts("HeadingFont2", "Arial");
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.Minor };

        style.ResolveEffectiveFontName(theme1).Should().Be("BodyFont");
        style.ResolveEffectiveFontName(theme2).Should().Be("Arial",
            "changing the theme's minor font should update the effective font without changing the style");
    }

    // ── CellStyle Clone/Equals/GetHashCode round-trip ─────────────────────────

    [Theory]
    [InlineData(CellFontScheme.None)]
    [InlineData(CellFontScheme.Minor)]
    [InlineData(CellFontScheme.Major)]
    public void CellStyle_Clone_PreservesFontScheme(CellFontScheme scheme)
    {
        var style = new CellStyle { FontScheme = scheme };
        var clone = style.Clone();
        clone.FontScheme.Should().Be(scheme);
    }

    [Fact]
    public void CellStyle_Equals_ReturnsFalse_WhenFontSchemeDiffers()
    {
        var a = new CellStyle { FontScheme = CellFontScheme.Minor };
        var b = new CellStyle { FontScheme = CellFontScheme.Major };
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void CellStyle_GetHashCode_DiffersWhenFontSchemeDiffers()
    {
        var a = new CellStyle { FontScheme = CellFontScheme.None };
        var b = new CellStyle { FontScheme = CellFontScheme.Minor };
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    // ── StyleDiff: explicit FontName pins FontScheme to None ──────────────────

    [Fact]
    public void StyleDiff_ApplyingFontName_PinsFontSchemeToNone()
    {
        var style = new CellStyle { FontName = "Aptos", FontScheme = CellFontScheme.Minor };
        var diff = new StyleDiff(FontName: "Arial");

        var result = diff.ApplyTo(style);

        result.FontName.Should().Be("Arial");
        result.FontScheme.Should().Be(CellFontScheme.None,
            "explicitly choosing a font name should override the theme font scheme");
    }

    [Fact]
    public void StyleDiff_ApplyingFontName_WithExplicitFontScheme_HonorsScheme()
    {
        var style = new CellStyle { FontName = "Arial", FontScheme = CellFontScheme.None };
        var diff = new StyleDiff(FontName: "Aptos", FontScheme: CellFontScheme.Minor);

        var result = diff.ApplyTo(style);

        result.FontName.Should().Be("Aptos");
        result.FontScheme.Should().Be(CellFontScheme.Minor,
            "when FontScheme is explicit in the diff (e.g. FormatPainter), it should be preserved");
    }

    [Fact]
    public void StyleDiff_ApplyingFontSchemeOnly_UpdatesSchemeWithoutChangingFontName()
    {
        var style = new CellStyle { FontName = "Calibri", FontScheme = CellFontScheme.None };
        var diff = new StyleDiff(FontScheme: CellFontScheme.Minor);

        var result = diff.ApplyTo(style);

        result.FontName.Should().Be("Calibri");
        result.FontScheme.Should().Be(CellFontScheme.Minor);
    }

    // ── StyleDiff.FromStyle preserves FontScheme (FormatPainter path) ─────────

    [Fact]
    public void StyleDiff_FromStyle_PreservesFontScheme()
    {
        var source = new CellStyle { FontName = "Aptos", FontScheme = CellFontScheme.Minor };
        var diff = StyleDiff.FromStyle(source);

        var target = new CellStyle { FontName = "Arial", FontScheme = CellFontScheme.None };
        var result = diff.ApplyTo(target);

        result.FontScheme.Should().Be(CellFontScheme.Minor,
            "FormatPainter (StyleDiff.FromStyle) must copy the font scheme to the target");
        result.FontName.Should().Be("Aptos");
    }
}
