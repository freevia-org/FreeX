using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r298: .prn is the one adapter whose save-load-save is not byte-stable, and the reason is a
/// documented design choice with an UNdocumented consequence.
///
/// <para>Found by testing idempotence rather than any named feature: save, load, save again, and
/// compare the bytes. Seven adapters reproduce themselves exactly. PRN does not, and the diff shows
/// why -- a value written in column B on a row whose column A is empty comes back in column A.</para>
///
/// <para>The write side is fixed-width, so the position information IS in the file. The read side
/// splits on runs of whitespace, which discards it. That is deliberate and the adapter says so:
/// "a .prn file is fundamentally a space-aligned text dump, and re-separating on whitespace is how
/// Excel re-imports one." What the adapter did NOT say is what that costs, and the cost is a silent
/// column shift -- so these tests state it rather than leaving the next reader to discover it from a
/// corrupted sheet.</para>
///
/// <para>Deliberately NOT changed here. Reading fixed-width would need column boundaries inferred
/// from whitespace runs across lines -- Excel's Text Import Wizard heuristic -- which changes how
/// every real .prn file imports, not just the ones FreeX wrote. That is a format decision, and it is
/// recorded as a candidate rather than made in a review pass.</para>
/// </summary>
public sealed class R298_PrnWhitespaceReadShiftsLeadingEmptyColumnsTests
{
    private static Sheet RoundTrip(Workbook workbook)
    {
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream).Sheets.First();
    }

    /// <summary>
    /// The declared limitation, pinned. If this ever starts failing, the reader has gained
    /// position awareness -- which is an improvement, and the test should be inverted rather than
    /// deleted.
    /// </summary>
    [Fact]
    public void AValueAfterAnEmptyLeadingColumnComesBackShiftedLeft()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(9.5));

        var loaded = RoundTrip(workbook);

        loaded.GetValue(new CellAddress(loaded.Id, 2, 1)).Should().Be(new NumberValue(9.5),
            "the reader splits on whitespace runs, so the empty leading column leaves no token and "
            + "everything after it moves one column left -- the documented parse strategy's "
            + "undocumented cost");
        loaded.GetValue(new CellAddress(loaded.Id, 2, 2)).Should().Be(BlankValue.Instance);
    }

    /// <summary>
    /// Rows with no empty leading column are unaffected, which is why the limitation is easy to miss:
    /// the common shape round-trips perfectly.
    /// </summary>
    [Fact]
    public void AFullyPopulatedRowRoundTripsToTheSameColumns()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(12.5));

        var loaded = RoundTrip(workbook);

        loaded.GetValue(new CellAddress(loaded.Id, 1, 1)).Should().Be(new TextValue("alpha"));
        loaded.GetValue(new CellAddress(loaded.Id, 1, 2)).Should().Be(new NumberValue(12.5));
    }

    /// <summary>
    /// The idempotence check that found this, kept as a test for the seven adapters that DO
    /// reproduce themselves. A format whose second save differs from its first is losing or
    /// inventing something, without anyone needing to know in advance what.
    /// </summary>
    [Theory]
    [InlineData("csv")]
    [InlineData("slk")]
    [InlineData("dif")]
    [InlineData("html")]
    [InlineData("ods")]
    [InlineData("xml")]
    [InlineData("json")]
    public void SavingTwiceProducesTheSameBytes(string key)
    {
        IFileAdapter adapter = key switch
        {
            "csv" => new CsvFileAdapter(),
            "slk" => new SlkFileAdapter(),
            "dif" => new DifFileAdapter(),
            "html" => new HtmlFileAdapter(),
            "ods" => new OdsFileAdapter(),
            "xml" => new SpreadsheetXmlFileAdapter(),
            "json" => new NativeJsonAdapter(),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12.5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("with, comma \"and quote\""));

        using var firstStream = new MemoryStream();
        adapter.Save(workbook, firstStream);
        var first = firstStream.ToArray();

        using var reloadStream = new MemoryStream(first);
        var reloaded = adapter.Load(reloadStream);

        using var secondStream = new MemoryStream();
        adapter.Save(reloaded, secondStream);

        secondStream.ToArray().Should().Equal(first,
            $"{key} must reproduce its own output: a second save that differs means the load lost "
            + "something the save then wrote differently, or invented something that was not there");
    }
}
