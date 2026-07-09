using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R15-file-import-export-edge-2: a quoted SYLK <c>K"..."</c> text constant containing an embedded
/// '\n' gets split across two physical lines by <see cref="StreamWriter.WriteLine"/> on save (the
/// newline itself is never escaped). The reader must be quote-aware across physical lines so the
/// value — and the record after it — round-trip intact.
/// </summary>
public sealed class R15_slk_Tests
{
    [Fact]
    public void RoundTrips_TextValueWithEmbeddedNewline_AndFollowingCellIntact()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a\nb"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("next"));

        var adapter = new SlkFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(wb, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var got = loaded.Sheets.Single();

        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("a\nb"));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new TextValue("next"));
    }
}
