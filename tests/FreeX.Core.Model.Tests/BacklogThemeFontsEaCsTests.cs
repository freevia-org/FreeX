using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Backlog (round-19 deferred R19-theme-extlst-2): the ribbon Fonts gallery calls WorkbookTheme.WithFonts,
// which used to null NativeFontSchemeXml wholesale -- discarding the source theme's East-Asian <a:ea> and
// complex-script <a:cs> typefaces so the writer re-emitted them empty. WithFonts now patches only the
// major/minor <a:latin> typefaces in place (mirroring WithEffects), preserving ea/cs.
public sealed class BacklogThemeFontsEaCsTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string SourceFontSchemeXml =
        "<a:fontScheme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office\">" +
        "<a:majorFont><a:latin typeface=\"Aptos Display\"/><a:ea typeface=\"Yu Gothic\"/>" +
        "<a:cs typeface=\"Times New Roman\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Aptos\"/><a:ea typeface=\"Yu Mincho\"/>" +
        "<a:cs typeface=\"Arial\"/></a:minorFont></a:fontScheme>";

    [Fact]
    public void WithFonts_PatchesLatinTypefacesButPreservesEastAsianAndComplexScript()
    {
        var theme = WorkbookTheme.Office
            .WithNativeFontSchemeXml(SourceFontSchemeXml)
            .WithFonts("Calibri", "Calibri Light");

        theme.MajorFontName.Should().Be("Calibri");
        theme.MinorFontName.Should().Be("Calibri Light");

        // Pre-fix WithFonts nulled NativeFontSchemeXml, dropping the whole source scheme.
        theme.NativeFontSchemeXml.Should().NotBeNull();
        var scheme = XElement.Parse(theme.NativeFontSchemeXml!);

        var majorFont = scheme.Element(A + "majorFont")!;
        var minorFont = scheme.Element(A + "minorFont")!;

        // The chosen fonts are applied to the Latin typefaces...
        majorFont.Element(A + "latin")!.Attribute("typeface")!.Value.Should().Be("Calibri");
        minorFont.Element(A + "latin")!.Attribute("typeface")!.Value.Should().Be("Calibri Light");

        // ...while the East-Asian and complex-script typefaces survive (the actual bug).
        majorFont.Element(A + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic");
        majorFont.Element(A + "cs")!.Attribute("typeface")!.Value.Should().Be("Times New Roman");
        minorFont.Element(A + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Mincho");
        minorFont.Element(A + "cs")!.Attribute("typeface")!.Value.Should().Be("Arial");
    }

    [Fact]
    public void WithFonts_WithNoNativeFontScheme_LeavesItNull()
    {
        // A synthesized theme with no native scheme (e.g. the built-in Office default) keeps a null
        // native scheme so the writer synthesizes one -- unchanged from the prior behavior.
        var theme = WorkbookTheme.Office.WithFonts("Calibri", "Calibri Light");

        theme.MajorFontName.Should().Be("Calibri");
        theme.NativeFontSchemeXml.Should().BeNull();
    }

    [Fact]
    public void WithFonts_InvalidNativeFontSchemeXml_RemainsNonFatalAndClearsNativeScheme()
    {
        var theme = WorkbookTheme.Office
            .WithNativeFontSchemeXml("\uD800")
            .WithFonts("Calibri", "Calibri Light");

        theme.MajorFontName.Should().Be("Calibri");
        theme.MinorFontName.Should().Be("Calibri Light");
        theme.NativeFontSchemeXml.Should().BeNull();
    }
}
