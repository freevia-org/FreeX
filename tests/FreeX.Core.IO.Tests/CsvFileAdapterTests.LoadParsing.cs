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
    public void Load_TreatsStandaloneCarriageReturnsAsRecordSeparators()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A,B\rC,D\r"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("D"));
    }

    [Fact]
    public void Load_KeepsQuotesInsideUnquotedFieldsAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\"cd,\"quoted\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("ab\"cd"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("quoted"));
    }

    [Fact]
    public void Load_ReadsQuotedFieldsWithEmbeddedCrLfAndEscapedQuotes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"line 1\r\n\"\"quoted\"\"\r\nline 3\",tail\r\n"));

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new TextValue("line 1\r\n\"quoted\"\r\nline 3"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("tail"));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 1)).Should().BeNull();
    }

    [Fact]
    public void Load_ReadsFinalQuotedEmptyFieldWithoutTrailingNewline()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"\""));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Load_PreservesQuotedEmptyFieldBetweenPopulatedFields()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("left,\"\",right\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("left"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue(""));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("right"));
    }

    [Fact]
    public void Load_PreservesTrailingQuotedEmptyFieldAtEndOfFile()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("head,\"\""));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("head"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Load_HonorsExcelSeparatorDirective()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nName;Amount\r\nAlice;3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_HonorsCommaSeparatorDirective()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=,\r\nName,Amount\r\nAlice,3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_HonorsTabSeparatorDirective()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=\t\r\nName\tAmount\r\nAlice\t3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_AppliesExcelSeparatorDirectiveOnlyToFirstPhysicalRecord()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nsep=x\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=x"));
    }

    [Fact]
    public void Load_KeepsQuotedSeparatorDirectiveAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"sep=;\"\r\nName,Amount\r\nAlice,3.5\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=;"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("Amount"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new TextValue("Alice"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(3.5));
    }

    [Fact]
    public void Load_ImportsExcelStyleFormulaFieldsAsFormulas()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2,=A1*2\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(2));
        var formulaCell = sheet.GetCell(new CellAddress(sheet.Id, 1, 2));
        formulaCell.Should().NotBeNull();
        formulaCell!.FormulaText.Should().Be("A1*2");
    }

    [Fact]
    public void Load_KeepsQuotedFormulaLikeFieldsAsLiteralText()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"=A1*2\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var cell = sheet.GetCell(new CellAddress(sheet.Id, 1, 1));
        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("=A1*2"));
    }

    [Theory]
    [InlineData("#GETTING_DATA")]
    [InlineData("#CONNECT!")]
    [InlineData("#UNKNOWN!")]
    public void Load_UsesExcelLikeTextCoercionForModernErrorLiterals(string errorCode)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{errorCode}\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new ErrorValue(errorCode));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForPercentages()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12.5%,-3%,4%\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(0.125));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-0.03));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(0.04));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForCurrencyValues()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"$1,234.50\",($42.25)\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new NumberValue(1234.5));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(-42.25));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"May 17, 2026\",17-May-26\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForSpaceSeparatedMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("17 May 2026,May 17 26\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForWeekdayMonthNameDates()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"Sunday, May 17, 2026\",\"Sun, May 17, 2026\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForWeekdayMonthNameDateTimes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"Sunday, May 17, 2026 9:30\",\"Sun, May 17, 2026 9:30 PM\"\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithOffsets()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T12:30+03:00,2026-05-17T09:30Z\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var expectedUtc = new DateTime(2026, 5, 17, 9, 30, 0);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
    }

}
