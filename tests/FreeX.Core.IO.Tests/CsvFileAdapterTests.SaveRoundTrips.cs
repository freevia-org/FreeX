using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    [Fact]
    public void Save_RoundTripsExplicitEmptyTextFields()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(""));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("tail"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"\",tail\r\n");
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var loadedSheet = roundTripped.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(""));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("tail"));
    }

    [Fact]
    public void Save_RoundTripsTrailingExplicitEmptyTextField()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("head"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(""));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("head,\"\"\r\n");
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var loadedSheet = roundTripped.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue("head"));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Save_RoundTripsFormulaLikeTextFieldsAsLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        var adapter = new CsvFileAdapter();
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

    [Fact]
    public void Save_RoundTripsAtPrefixedTextFieldsAsLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("@SUM(A1)"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"'@SUM(A1)\"\r\n");
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("@SUM(A1)"));
    }

    [Fact]
    public void Save_RoundTripsSeparatorDirectivePrefixTextBeforeBlankCell()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("sep="));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue(""));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.Value.Should().Be(new TextValue("sep="));
    }

    [Theory]
    [InlineData("0042")]
    [InlineData("1E3")]
    [InlineData("+42")]
    [InlineData("-42")]
    public void Save_RoundTripsNumericTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("$42.00")]
    [InlineData("+$42.00")]
    [InlineData("-$42.00")]
    [InlineData(" +$42.00 ")]
    [InlineData(" -$42.00 ")]
    [InlineData("($42.25)")]
    [InlineData(" ($42.25) ")]
    public void Save_RoundTripsCurrencyTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("12.5%")]
    [InlineData("+12%")]
    [InlineData("-3%")]
    [InlineData(" 12.5% ")]
    public void Save_RoundTripsPercentageTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("false")]
    [InlineData(" TRUE ")]
    public void Save_RoundTripsBooleanLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("1/2")]
    [InlineData("2026-05-17")]
    [InlineData("09:30")]
    [InlineData("May 17, 2026")]
    public void Save_RoundTripsDateTimeLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("sep=;")]
    [InlineData("sep=\t")]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    [InlineData("#CONNECT!")]
    [InlineData("#UNKNOWN!")]
    [InlineData("#FIELD!")]
    [InlineData("#BLOCKED!")]
    [InlineData("#GETTING_DATA")]
    [InlineData(" #N/A ")]
    public void Save_RoundTripsErrorLikeTextFieldsAsLiteralText(string text)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));

        var adapter = new CsvFileAdapter();
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
