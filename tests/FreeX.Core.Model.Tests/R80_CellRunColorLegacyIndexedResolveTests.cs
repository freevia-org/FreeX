using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R80-io-theme-styles-5-1 (rich-run color resolution): <see cref="CellRunColor.Resolve"/>'s
/// ResolveIndexed had the identical "IndexedIndex - 7" bug as XlsxColorReader.TryReadIndexedColor --
/// legacy fixed indexed values 0-7 (black/white/red/green/blue/yellow/magenta/cyan) produced a
/// negative ColorIndex that <see cref="WorkbookIndexedColorPalette"/> rejects, so ResolveIndexed fell
/// through to "default" (= CellColor.Black), silently mis-coloring any rich-text run using indexed
/// colors 0-7.
/// </summary>
public sealed class R80_CellRunColorLegacyIndexedResolveTests
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
    public void Resolve_LegacyFixedIndexedValue_ResolvesToCorrectRgb(int indexed, byte r, byte g, byte b)
    {
        var color = CellRunColor.FromIndexed(indexed);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(new CellColor(r, g, b),
            $"indexed={indexed} is a real, defined part of the legacy fixed indexed palette and must not " +
            "fall through to default(CellColor) = black");
    }

    [Fact]
    public void Resolve_LegacyIndexedWhite_DoesNotFallBackToDefaultBlack()
    {
        // Pre-fix: IndexedIndex - 7 = 1 - 7 = -6, rejected by WorkbookIndexedColorPalette, so
        // ResolveIndexed returned default(CellColor) which is black -- indistinguishable from a
        // genuinely black run, silently mis-coloring white indexed runs as black.
        var color = CellRunColor.FromIndexed(1);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(CellColor.White);
        resolved.Should().NotBe(CellColor.Black);
    }

    // ---- Sibling/no-regression: the pre-existing 8-63 "index - 7" mapping and the 64/65
    // system fg/bg special cases must keep working after adding the 0-7 fixed range. ----

    [Theory]
    [InlineData(8, 0x00, 0x00, 0x00)]  // Black (duplicate of legacy index 0)
    [InlineData(10, 0xFF, 0x00, 0x00)] // Red (duplicate of legacy index 2)
    [InlineData(18, 0x00, 0x00, 0x80)] // Navy (standard palette, unaffected by the 0-7 fix)
    public void Resolve_StandardIndexedValue_StillResolvesAgainstPaletteViaMinusSeven(int indexed, byte r, byte g, byte b)
    {
        var color = CellRunColor.FromIndexed(indexed);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(new CellColor(r, g, b),
            $"indexed={indexed} must still map to WorkbookIndexedColorPalette entry {indexed - 7} (index-7)");
    }

    [Fact]
    public void Resolve_Indexed64_StillResolvesToBlack()
    {
        var color = CellRunColor.FromIndexed(64);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(CellColor.Black);
    }

    [Fact]
    public void Resolve_Indexed65_StillResolvesToWhite()
    {
        var color = CellRunColor.FromIndexed(65);

        var resolved = color.Resolve(WorkbookTheme.Office, new WorkbookIndexedColorPalette());

        resolved.Should().Be(CellColor.White);
    }
}
