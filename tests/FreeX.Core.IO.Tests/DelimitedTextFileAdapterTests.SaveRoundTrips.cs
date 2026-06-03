using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Save_WritesTabDelimitedRowsAndQuotesTabs()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Note"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alice"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("a\tb"));

        using var stream = new MemoryStream();
        new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t').Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("Name\tNote\r\nAlice\t\"a\tb\"\r\n");
    }

    [Fact]
    public void Save_RoundTripsQuotedFieldsWithEmbeddedCrLfAndQuotes()
    {
        var text = "line 1\r\n\"quoted\"\r\nline 3";
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("tail"));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var loadedSheet = roundTripped.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(text));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("tail"));
    }

    [Fact]
    public void Save_RoundTripsExplicitEmptyTextFields()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(""));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("tail"));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"\"\ttail\r\n");
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var loadedSheet = roundTripped.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(""));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("tail"));
    }

    [Fact]
    public void Save_WritesNonFiniteDateTimeValuesAsText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(double.NegativeInfinity));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("NaN\tInfinity\t-Infinity\r\n");
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_TruncatesSeekableOutputStreamBeforeWritingDelimitedText()
    {
        var largeWorkbook = new Workbook("Large");
        var largeSheet = largeWorkbook.AddSheet("Sheet1");
        largeSheet.SetCell(new CellAddress(largeSheet.Id, 1, 1), new TextValue("long stale value"));
        largeSheet.SetCell(new CellAddress(largeSheet.Id, 1, 2), new TextValue("tail"));
        var smallWorkbook = new Workbook("Small");
        var smallSheet = smallWorkbook.AddSheet("Sheet1");
        smallSheet.SetCell(new CellAddress(smallSheet.Id, 1, 1), new TextValue("ok"));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(largeWorkbook, stream);
        stream.Position = 0;

        adapter.Save(smallWorkbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("ok\r\n");
    }

    [Fact]
    public void Save_WritesOutOfRangeDateTimeValuesAsText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(double.MaxValue));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(double.MinValue));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var max = double.MaxValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var min = double.MinValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be($"\"'{max}\"\t\"'{min}\"\r\n");
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue(max));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue(min));
    }

    [Fact]
    public void Save_RoundTripsFormulaLikeTextFieldsAsLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"'=A1*2\"\r\n");
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("=A1*2"));
    }

    [Theory]
    [InlineData("=A1*2", "\"'=A1*2\"\r\n")]
    [InlineData("+42", "\"'+42\"\r\n")]
    [InlineData("-42", "\"'-42\"\r\n")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"\r\n")]
    public void Save_RoundTripsFormulaPrefixTextFieldsAsLiteralText(string text, string expected)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be(expected);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Fact]
    public void Save_RoundTripsSeparatorDirectivePrefixTextBeforeBlankCell()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("sep="));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(""));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.Value.Should().Be(new TextValue("sep="));
    }

    [Theory]
    [InlineData("sep=;")]
    [InlineData("sep=,")]
    [InlineData("0042")]
    [InlineData("$42.00")]
    [InlineData(" +$42.00 ")]
    [InlineData(" -$42.00 ")]
    [InlineData(" ($42.25) ")]
    [InlineData(" 12.5% ")]
    [InlineData(" TRUE ")]
    [InlineData("2026-05-17")]
    [InlineData("#N/A")]
    [InlineData("#CONNECT!")]
    [InlineData("#UNKNOWN!")]
    [InlineData("#FIELD!")]
    [InlineData("#BLOCKED!")]
    public void Save_RoundTripsCoercionLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

}
