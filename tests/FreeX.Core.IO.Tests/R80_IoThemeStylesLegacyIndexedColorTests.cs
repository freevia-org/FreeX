using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-io-theme-styles-5-1: the legacy indexed palette's low fixed range (OOXML/BIFF indexed
/// values 0-7 -- black/white/red/green/blue/yellow/magenta/cyan) is a real, distinct part of the
/// indexed-color model. <see cref="XlsxColorReader"/>'s TryReadIndexedColor unconditionally computed
/// "index - 7" to map the raw OOXML value into <see cref="WorkbookIndexedColorPalette"/>'s 1-based
/// ColorIndex space -- correct only for 8-63, where 8-15 duplicate these same eight fixed colors
/// before the 48 customizable "standard colors" begin at 16. For index 0-7 this produced a negative
/// ColorIndex, which the palette rejects, so the whole read failed and every caller's black fallback
/// silently mis-colored borders, rich-text runs, conditional-format stops, gradient-fill stops, and
/// structured-table dxf colors.
/// </summary>
public sealed class R80_IoThemeStylesLegacyIndexedColorTests
{
    [Theory]
    [InlineData(0, 0x00, 0x00, 0x00)] // Black
    [InlineData(1, 0xFF, 0xFF, 0xFF)] // White
    [InlineData(2, 0xFF, 0x00, 0x00)] // Red
    [InlineData(3, 0x00, 0xFF, 0x00)] // Green
    [InlineData(4, 0x00, 0x00, 0xFF)] // Blue
    [InlineData(5, 0xFF, 0xFF, 0x00)] // Yellow
    [InlineData(6, 0xFF, 0x00, 0xFF)] // Magenta
    [InlineData(7, 0x00, 0xFF, 0xFF)] // Cyan
    public void TryReadCellColor_LegacyFixedIndexedValue_ResolvesToCorrectRgb(int indexed, byte r, byte g, byte b)
    {
        var element = XElement.Parse($"""<color indexed="{indexed}"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue($"indexed=\"{indexed}\" is a real, defined part of the legacy fixed indexed palette");

        color.Should().Be(new CellColor(r, g, b),
            $"OOXML indexed=\"{indexed}\" must resolve to its fixed legacy-palette RGB, not fail/fall back to black");
    }

    [Fact]
    public void TryReadCellColor_LegacyIndexedRed_DoesNotFallBackToBlack()
    {
        // Pre-fix: TryResolveColor(2 - 7 = -5) was rejected by WorkbookIndexedColorPalette's
        // ">= 1 and < Length" guard, so TryReadIndexedColor returned false and callers like
        // XlsxCellBorderStyleReader.ReadEdge fell back to CellColor.Black.
        var element = XElement.Parse("""<color indexed="2"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        var resolved = XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color);

        resolved.Should().BeTrue();
        color.Should().Be(new CellColor(0xFF, 0x00, 0x00));
        color.Should().NotBe(CellColor.Black);
    }

    // ---- Sibling/no-regression: the pre-existing 8-63 "index - 7" mapping and the 64/65
    // system fg/bg special cases must keep working after adding the 0-7 fixed range. ----

    [Theory]
    [InlineData(8, 0x00, 0x00, 0x00)]  // Black (duplicate of legacy index 0)
    [InlineData(10, 0xFF, 0x00, 0x00)] // Red (duplicate of legacy index 2)
    [InlineData(18, 0x00, 0x00, 0x80)] // Navy (standard palette, unaffected by the 0-7 fix)
    public void TryReadCellColor_StandardIndexedValue_StillResolvesAgainstPaletteViaMinusSeven(int indexed, byte r, byte g, byte b)
    {
        var element = XElement.Parse($"""<color indexed="{indexed}"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(new CellColor(r, g, b),
            $"indexed={indexed} must still map to WorkbookIndexedColorPalette entry {indexed - 7} (index-7)");
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

    [Fact]
    public void TryReadCellColor_IndexedSystemBackground65_StillResolvesToWhite()
    {
        var element = XElement.Parse("""<color indexed="65"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue();

        color.Should().Be(CellColor.White);
    }
}
