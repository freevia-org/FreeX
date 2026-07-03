using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the DIF (.dif) adapter: single-sheet, values-only round-trips of the typed value records
/// (BOT / numeric "V" / string "1,0" / TRUE-FALSE / ERROR) and the header / EOD framing.
/// </summary>
public sealed class DifFileAdapterTests
{
    private static (Workbook Workbook, Sheet Sheet) RoundTrip(Workbook source)
    {
        var adapter = new DifFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(source, stream);
        stream.Position = 0;
        var wb = adapter.Load(stream);
        return (wb, wb.Sheets.Single());
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var wb = new Workbook("Untitled");
        sheet = wb.AddSheet("Sheet1");
        return wb;
    }

    [Fact]
    public void RoundTrips_NumberAndTextValuesPreservingPositions()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(3.5));
        // Sparse: a gap at (2,3) then a value at (2,4) must keep column alignment.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(42));

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("Name"));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        got.GetValue(new CellAddress(got.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        got.GetValue(new CellAddress(got.Id, 2, 2)).Should().Be(new NumberValue(3.5));
        got.GetValue(new CellAddress(got.Id, 2, 4)).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void RoundTrips_BooleanValues()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new BoolValue(false));

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new BoolValue(true));
        got.GetValue(new CellAddress(got.Id, 1, 2)).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void RoundTrips_ErrorValueAsError()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.NA);

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().BeOfType<ErrorValue>();
    }

    // F27 regression: the DIF writer used to collapse every ErrorValue to the generic "ERROR"
    // indicator, so a specific error like #N/A came back as #VALUE! after a save/reload round
    // trip. The specific error code must now round-trip.
    [Theory]
    [InlineData("#N/A")]
    [InlineData("#REF!")]
    [InlineData("#DIV/0!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#NUM!")]
    public void RoundTrips_SpecificErrorCodesRatherThanDegradingToValueError(string code)
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue(code));

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new ErrorValue(code));
    }

    [Fact]
    public void RoundTrips_QuotedTextWithEmbeddedQuotes()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("say \"hi\""));

        var (_, got) = RoundTrip(wb);
        got.GetValue(new CellAddress(got.Id, 1, 1)).Should().Be(new TextValue("say \"hi\""));
    }

    [Fact]
    public void Save_EmitsHeaderTopicsAndEodTerminator()
    {
        var wb = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        using var stream = new MemoryStream();
        new DifFileAdapter().Save(wb, stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());

        text.Should().StartWith("TABLE");
        text.Should().Contain("VECTORS");
        text.Should().Contain("TUPLES");
        text.Should().Contain("DATA");
        text.Should().Contain("BOT");
        text.TrimEnd().Should().EndWith("EOD");
    }

    [Fact]
    public void Load_ParsesMinimalDifFile()
    {
        var dif = string.Join("\r\n",
            "TABLE", "0,1", "\"sample\"",
            "VECTORS", "0,2", "\"\"",
            "TUPLES", "0,1", "\"\"",
            "DATA", "0,0", "\"\"",
            "-1,0", "BOT",
            "0,5", "V",
            "1,0", "\"hi\"",
            "-1,0", "EOD") + "\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(dif));

        var sheet = new DifFileAdapter().Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(5));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("hi"));
    }
}
