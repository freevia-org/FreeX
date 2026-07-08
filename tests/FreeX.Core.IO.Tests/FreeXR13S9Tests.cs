using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-13 bucket S9 fix verification (DIF blank-cell inflation on round-trip).
/// See docs/../scratchpad r13-S9.md for the full finding text.
/// </summary>
public sealed class FreeXR13S9Tests
{
    // R13-other-format-adapters-2: Save() pads the bounding rectangle's gaps with WriteEmpty
    // ("1,0" / ""), and Load() used to unconditionally materialize that as TextValue("") for every
    // gap cell — turning a sparse 3x3 rectangle with only A1 and C3 populated into 9 occupied
    // empty-string cells. Excel's own convention (and this adapter's own writer comment) treats an
    // empty string vector as a gap, so the reloaded sheet must keep exactly the 2 originally
    // populated cells occupied and every other cell genuinely blank.
    [Fact]
    public void DifRoundTrip_LeavesGapsBlank_InsteadOfEmptyStringCells()
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(c3, new NumberValue(2));

        var adapter = new DifFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.Sheets.Single();

        // Only the two originally populated cells are occupied (COUNTA-equivalent) — the other 7
        // cells in the bounding rectangle must not have been materialized as TextValue("").
        loadedSheet.GetOccupiedCellMap().Should().HaveCount(2);

        // Every gap cell in the bounding rectangle reads back as genuinely blank
        // (ISBLANK-equivalent), not as an occupied empty-string text cell.
        for (uint row = 1; row <= 3; row++)
        {
            for (uint col = 1; col <= 3; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (address.Equals(a1) || address.Equals(c3))
                    continue;

                loadedSheet.GetValue(address).Should().Be(BlankValue.Instance,
                    because: $"{address} was never populated and must stay blank, not become TextValue(\"\")");
            }
        }

        loadedSheet.GetValue(a1).Should().Be(new NumberValue(1));
        loadedSheet.GetValue(c3).Should().Be(new NumberValue(2));
    }
}
