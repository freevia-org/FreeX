using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round 65 io-color bucket findings (rich-run color resolution):
///  - R65-io-theme-color-6-1: <see cref="CellRunColor.Resolve"/> for an indexed color must map the raw
///    OOXML "indexed" value to <see cref="WorkbookIndexedColorPalette"/> via index-7, not index+1.
///  - R65-io-theme-color-6-2: indexed=64/65 (System Foreground/Background) must resolve to Black/White
///    directly, not fall through to the 1..56 palette lookup (which would land on the wrong swatch after
///    the index-7 fix, or on default(CellColor) before it).
/// </summary>
public sealed class R65_CellRunColorIndexedResolveTests
{
    [Fact]
    public void Resolve_Indexed65_ResolvesToWhite()
    {
        var color = CellRunColor.FromIndexed(65);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(CellColor.White,
            "OOXML reserved indexed color 65 is 'System Background', which must resolve to white");
    }

    [Fact]
    public void Resolve_Indexed64_ResolvesToBlack()
    {
        var color = CellRunColor.FromIndexed(64);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(CellColor.Black,
            "OOXML reserved indexed color 64 is 'System Foreground', which must resolve to black");
    }

    [Theory]
    [InlineData(8, 0x00, 0x00, 0x00)]   // Black
    [InlineData(10, 0xFF, 0x00, 0x00)]  // Red
    [InlineData(18, 0x00, 0x00, 0x80)]  // Navy
    public void Resolve_NormalIndexedValue_ResolvesAgainstPaletteViaMinusSeven(int indexed, byte r, byte g, byte b)
    {
        var color = CellRunColor.FromIndexed(indexed);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(new CellColor(r, g, b),
            $"indexed={indexed} must map to WorkbookIndexedColorPalette entry {indexed - 7} (index-7), not index+1");
    }

    [Fact]
    public void Resolve_NormalIndexedValue_DoesNotResolveToOldOffByEightMaroon()
    {
        // Pre-fix: indexed=8 resolved via TryResolveColor(8+1=9) = Maroon (0x80,0x00,0x00).
        var color = CellRunColor.FromIndexed(8);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().NotBe(new CellColor(0x80, 0x00, 0x00));
    }

    [Fact]
    public void Resolve_ThemeColor_UnaffectedByIndexedFix()
    {
        var color = CellRunColor.FromTheme(4, 0.2);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(WorkbookTheme.Office.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.2));
    }

    [Fact]
    public void Resolve_RgbColor_UnaffectedByIndexedFix()
    {
        var rgb = new CellColor(0x11, 0x22, 0x33);
        var color = CellRunColor.FromRgb(rgb);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(rgb);
    }
}
