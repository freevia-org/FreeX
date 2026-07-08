using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression guard for round-14 review finding (bucket T11):
///
///   R14-meta-1 - <see cref="Sheet.ColumnFilterOwnedRows"/> (the row-ownership bookkeeping every
///     column-owned filter mechanism relies on to decide whether a row another column still owns
///     may be un-hidden — see FreeX.Core.Commands.FilterCommand) was never written on native .fxl
///     save and never restored on load, even though its siblings FilterHiddenRows/
///     ActiveValueFilterColumns/ValueFilterHiddenRows all round-trip correctly. A reload therefore
///     came back with an EMPTY ColumnFilterOwnedRows, so the next filter recompute on any column
///     wrongly treated every OTHER column's condition/value filter as inactive and un-hid rows that
///     must stay hidden — the exact AND-across-columns violation round-13's fix (R13-meta-3) was
///     written to prevent, resurfacing after a plain native round-trip.
/// </summary>
public sealed class FreeXR14T11Tests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrips_ColumnFilterOwnedRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));

        // Column A's condition/Top-Bottom/average filter owns row 5 (and also happens to be the
        // AutoFilter's FilterHiddenRows entry); column B's value-list filter independently owns the
        // SAME row 5 too, mirroring what FilterCommand leaves behind for an AND-across-columns hide.
        sheet.ColumnFilterOwnedRows[1] = [5];
        sheet.ActiveValueFilterColumns[2] = ["x"];
        sheet.ValueFilterHiddenRows.Add(5);
        sheet.FilterHiddenRows.Add(5);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ColumnFilterOwnedRows.Should().ContainKey(1);
        reloadedSheet.ColumnFilterOwnedRows[1].Should().BeEquivalentTo([5u]);
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);
        reloadedSheet.ValueFilterHiddenRows.Should().BeEquivalentTo([5u]);

        // The whole point of persisting this map: FilterCommand.IsHiddenByAnyColumnOwnedFilter (the
        // exact AND-across-columns un-hide guard from R13-meta-3) scans ColumnFilterOwnedRows.Values
        // for any column that still owns a row. Before this fix, the reloaded map was always empty, so
        // this scan would find nothing and column B's value-filter clear would wrongly un-hide row 5
        // even though column A's condition/Top-Bottom/average filter still owns it.
        reloadedSheet.ColumnFilterOwnedRows.Values.Any(owned => owned.Contains(5u))
            .Should().BeTrue(
                "column A's reloaded ColumnFilterOwnedRows entry must still be seen as owning row 5, " +
                "or clearing column B's value filter would wrongly un-hide it");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_EmptyColumnFilterOwnedRowsAsEmpty()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ColumnFilterOwnedRows.Should().BeEmpty();
    }
}
