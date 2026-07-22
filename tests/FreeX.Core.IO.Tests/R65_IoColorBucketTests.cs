using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 65 io-color bucket findings:
///  - R65-io-theme-color-6-1: legacy OOXML "indexed" attribute values are off-by-8 against
///    <see cref="WorkbookIndexedColorPalette"/>'s 1-based ColorIndex numbering. The palette maps
///    indexed=N to DefaultColors[N-7] (e.g. indexed=8 -&gt; palette index 1 = Black), not N+1.
///  - R65-io-theme-color-6-3: a custom table-style dxf band color expressed as a THEME reference
///    must resolve through the workbook theme, not be dropped for lack of theme context.
/// </summary>
public sealed class R65_IoColorBucketTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---- R65-io-theme-color-6-1 ----

    [Theory]
    [InlineData(8, 0x00, 0x00, 0x00)]   // Black
    [InlineData(10, 0xFF, 0x00, 0x00)]  // Red
    [InlineData(18, 0x00, 0x00, 0x80)]  // Navy
    public void TryReadCellColor_IndexedValue_ResolvesAgainstPaletteViaMinusSeven(int indexed, byte r, byte g, byte b)
    {
        var element = XElement.Parse($"""<color indexed="{indexed}"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(new CellColor(r, g, b),
            $"OOXML indexed=\"{indexed}\" must map to WorkbookIndexedColorPalette entry {indexed - 7} " +
            "(index-7), not index+1");
    }

    [Fact]
    public void TryReadCellColor_IndexedValue8_DoesNotResolveToOldOffByEightMaroon()
    {
        // Pre-fix: indexed=8 resolved via TryResolveColor(8+1=9) = Maroon (0x80,0x00,0x00).
        var element = XElement.Parse("""<color indexed="8"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color);

        color.Should().NotBe(new CellColor(0x80, 0x00, 0x00));
    }

    [Fact]
    public void TryReadCellColor_RgbColor_UnaffectedByIndexedFix()
    {
        var element = XElement.Parse("""<color rgb="FF336699"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(new CellColor(0x33, 0x66, 0x99));
    }

    [Fact]
    public void TryReadCellColor_ThemeColor_UnaffectedByIndexedFix()
    {
        var element = XElement.Parse("""<color theme="4" tint="0.2"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.2));
    }

    [Fact]
    public void TryReadCellColor_IndexedSystemBackground65_StillResolvesToWhite()
    {
        // Sibling/no-regression: the 64/65 auto fg/bg special-cases (from R57-io-theme-colors-5-1)
        // must keep working after the index-7 remap.
        var element = XElement.Parse("""<color indexed="65"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(CellColor.White);
    }

    [Fact]
    public void TryReadCellColor_IndexedSystemForeground64_StillResolvesToBlack()
    {
        var element = XElement.Parse("""<color indexed="64"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(CellColor.Black);
    }

    // ---- R65-io-theme-color-6-3 ----

    [Fact]
    public void StructuredTableStyleMetadataReader_ThemeBandColor_RoundTripsThroughTheme()
    {
        var stylesXml = XDocument.Parse(
            $"""
             <styleSheet xmlns="{WorkbookNs}">
               <dxfs count="1">
                 <dxf>
                   <fill><patternFill><bgColor theme="4" tint="0.4"/></patternFill></fill>
                 </dxf>
               </dxfs>
               <tableStyles count="1">
                 <tableStyle name="CustomTheme" pivot="0" table="1">
                   <tableStyleElement type="headerRow" dxfId="0"/>
                 </tableStyle>
               </tableStyles>
             </styleSheet>
             """);

        var styles = XlsxStructuredTableStyleMetadataReader.Load(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        var style = styles.Should().ContainSingle(s => s.Name == "CustomTheme").Subject;
        var element = style.Elements.Should().ContainSingle(e => e.Type == "headerRow").Subject;

        element.DifferentialFormatId.Should().Be(0, "the dxfId must not be stripped when the band color is a theme reference");
        element.Format.Should().NotBeNull("a theme-color dxf must still produce a StyleDiff so the writer keeps the band fill");
        element.Format!.FillColor.Should().Be(
            WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.4),
            "the header-row band fill must resolve the theme reference, not be dropped");
    }

    [Fact]
    public void StructuredTableStyleMetadataReader_RgbBandColor_StillWorks()
    {
        var stylesXml = XDocument.Parse(
            $"""
             <styleSheet xmlns="{WorkbookNs}">
               <dxfs count="1">
                 <dxf>
                   <fill><patternFill><bgColor rgb="FF112233"/></patternFill></fill>
                 </dxf>
               </dxfs>
               <tableStyles count="1">
                 <tableStyle name="CustomRgb" pivot="0" table="1">
                   <tableStyleElement type="headerRow" dxfId="0"/>
                 </tableStyle>
               </tableStyles>
             </styleSheet>
             """);

        var styles = XlsxStructuredTableStyleMetadataReader.Load(stylesXml, WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        var style = styles.Should().ContainSingle(s => s.Name == "CustomRgb").Subject;
        var element = style.Elements.Should().ContainSingle(e => e.Type == "headerRow").Subject;

        element.Format.Should().NotBeNull();
        element.Format!.FillColor.Should().Be(new CellColor(0x11, 0x22, 0x33));
    }
}
