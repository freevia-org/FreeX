using System.Text;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class CsvFileAdapterTests
{
    [Fact]
    public void Load_UsesExcelLikeTextCoercionForBooleans()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("TRUE,false\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Load_FallsBackToWindows1252WhenUtf8DecodingFails()
    {
        using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
    }

    [Theory]
    [MemberData(nameof(Utf32BomCsvPayloads))]
    public void Load_HonorsUtf32ByteOrderMarks(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new BoolValue(true));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
    }

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
    public void Load_AppliesExcelSeparatorDirectiveOnlyToFirstPhysicalRecord()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nsep=x\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=x"));
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
    public void Save_WritesDateTimeValuesAsInvariantText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2026-05-17,2026-05-17 09:30:00,09:30:00\r\n");
    }

    [Fact]
    public void Save_PreservesFractionalSecondsInDateTimeValues()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(new TimeSpan(0, 9, 30, 15, 250).TotalDays));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2026-05-17 09:30:15.25,09:30:15.25\r\n");
    }

    [Fact]
    public void Save_IgnoresCellsBeyondExcelGridLimits()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1), new TextValue("overflow-column"));
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1), new TextValue("overflow-row"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("visible\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankColumnsFromWorksheetCoordinates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("offset"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be(",offset\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankRowsFromWorksheetCoordinates()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("offset"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\r\noffset\r\n");
    }

    [Fact]
    public void Save_QuotesFormulaLikeTextFieldsToPreserveLiteralText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("\"=A1*2\"\r\n");
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
        stream.Position = 0;

        var roundTripped = adapter.Load(stream);
        var cell = roundTripped.Sheets.Single().GetCell(1, 1);

        cell.Should().NotBeNull();
        cell!.FormulaText.Should().BeNull();
        cell.Value.Should().Be(new TextValue("=A1*2"));
    }

    [Theory]
    [InlineData("+42")]
    [InlineData("-42")]
    public void Save_RoundTripsSignedNumericTextFieldsAsLiteralText(string text)
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

    [Fact]
    public void Save_WritesFormulaCellsAsExcelFormulaFields()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("2,=A1*2\r\n");
    }

    public static TheoryData<byte[]> Utf32BomCsvPayloads() => new()
    {
        Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes("TRUE,42\r\n")).ToArray(),
        new UTF32Encoding(bigEndian: true, byteOrderMark: true)
            .GetPreamble()
            .Concat(new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetBytes("TRUE,42\r\n"))
            .ToArray()
    };
}
