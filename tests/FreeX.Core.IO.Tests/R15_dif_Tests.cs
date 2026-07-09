using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R15-file-import-export-edge-1: a TextValue containing an embedded '\n'/'\r' is written by
/// <see cref="DifFileAdapter"/> as a "1,0" chunk followed by a quoted string whose raw line break
/// survives into the file, so StreamReader.ReadLine splits it across two physical lines. The reader
/// must stay quote-aware across those physical lines instead of assuming a fixed two-line stride —
/// otherwise the split value is mis-recovered and every subsequent record is read off-by-one.
/// </summary>
public sealed class R15_dif_Tests
{
    [Fact]
    public void RoundTrips_TextValueWithEmbeddedNewline_WithoutCascadingOffByOne()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a\nb"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("next"));

        var adapter = new DifFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var got = loaded.Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("a\nb"));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new TextValue("next"));
    }
}
