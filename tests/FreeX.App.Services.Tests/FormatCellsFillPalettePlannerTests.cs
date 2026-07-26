using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class FormatCellsFillPalettePlannerTests
{
    [Fact]
    public void BackgroundEntries_MatchWpfOrderAndDimensions()
    {
        var entries = FormatCellsFillPalettePlanner.BackgroundEntries;

        entries.Should().HaveCount(30);
        entries[0].Kind.Should().Be(FormatCellsFillPaletteEntryKind.Clear);
        entries[19].Kind.Should().Be(FormatCellsFillPaletteEntryKind.More);
        entries.Count(entry => entry.IsColor).Should().Be(28);
        entries.Where(entry => entry.IsColor).Select(entry => entry.Color).Should().Equal(
            new CellColor(255, 255, 255), new CellColor(0, 0, 0), new CellColor(89, 89, 89),
            new CellColor(128, 128, 128), new CellColor(217, 217, 217), new CellColor(192, 0, 0),
            new CellColor(255, 0, 0), new CellColor(237, 125, 49), new CellColor(255, 192, 0),
            new CellColor(255, 255, 0), new CellColor(255, 242, 204), new CellColor(0, 97, 0),
            new CellColor(146, 208, 80), new CellColor(226, 239, 218), new CellColor(31, 78, 121),
            new CellColor(91, 155, 213), new CellColor(221, 235, 247), new CellColor(112, 48, 160),
            new CellColor(0, 176, 240), new CellColor(0, 176, 180), new CellColor(112, 173, 71),
            new CellColor(84, 130, 53), new CellColor(255, 199, 206), new CellColor(244, 176, 132),
            new CellColor(204, 192, 218), new CellColor(68, 84, 106), new CellColor(131, 60, 12),
            new CellColor(197, 90, 17));
    }

    [Fact]
    public void PatternEntries_MatchWpfOrderAndMoreColorsSlot()
    {
        var entries = FormatCellsFillPalettePlanner.PatternEntries;

        entries.Should().HaveCount(8);
        entries[7].Kind.Should().Be(FormatCellsFillPaletteEntryKind.More);
        entries.Where(entry => entry.IsColor).Select(entry => entry.Color).Should().Equal(
            CellColor.Black,
            new CellColor(128, 128, 128),
            new CellColor(255, 0, 0),
            new CellColor(255, 192, 0),
            new CellColor(0, 176, 80),
            new CellColor(0, 112, 192),
            new CellColor(112, 48, 160));
    }
}
