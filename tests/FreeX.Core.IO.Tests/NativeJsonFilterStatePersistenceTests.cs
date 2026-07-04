using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for finding G32: <see cref="Sheet.ActiveValueFilterColumns"/> and
/// <see cref="Sheet.ValueFilterHiddenRows"/> — the per-column AND-across-columns value-filter state
/// that <c>FreeX.Core.Commands.FilterCommand</c> relies on — must round-trip through the native JSON
/// (.fxj) adapter alongside <see cref="Sheet.FilterHiddenRows"/>, or a reload leaves the two out of
/// sync and corrupts the next filter recompute.
/// </summary>
public sealed class NativeJsonFilterStatePersistenceTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrips_ActiveValueFilterColumnsAndValueFilterHiddenRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));

        // Two AutoFilter columns are simultaneously active (AND-across-columns), as FilterCommand
        // would leave them after filtering column A then column B.
        sheet.ActiveValueFilterColumns[1] = ["x"];
        sheet.ActiveValueFilterColumns[2] = ["y"];
        sheet.FilterHiddenRows.Add(3);
        sheet.ValueFilterHiddenRows.Add(3);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ActiveValueFilterColumns.Should().ContainKey(1);
        reloadedSheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["x"]);
        reloadedSheet.ActiveValueFilterColumns.Should().ContainKey(2);
        reloadedSheet.ActiveValueFilterColumns[2].Should().BeEquivalentTo(["y"]);
        reloadedSheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u]);
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrips_EmptyActiveValueFilterColumnsAsEmpty()
    {
        var workbook = new Workbook("test");
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new NativeJsonAdapter().Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ActiveValueFilterColumns.Should().BeEmpty();
        reloadedSheet.ValueFilterHiddenRows.Should().BeEmpty();
    }
}
