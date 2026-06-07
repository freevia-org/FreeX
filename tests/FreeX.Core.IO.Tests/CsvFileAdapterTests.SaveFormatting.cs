using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    [Fact]
    public void Save_WritesDateTimeValuesAsInvariantText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("2026-05-17,2026-05-17 09:30:00,09:30:00\r\n");
    }

    [Fact]
    public void Save_TruncatesSeekableOutputStreamBeforeWritingCsv()
    {
        var largeWorkbook = new Workbook("Large");
        var largeSheet = largeWorkbook.AddSheet("Sheet1");
        largeSheet.SetCell(new CellAddress(largeSheet.Id, 1, 1), new TextValue("long stale value"));
        largeSheet.SetCell(new CellAddress(largeSheet.Id, 1, 2), new TextValue("tail"));
        var smallWorkbook = new Workbook("Small");
        var smallSheet = smallWorkbook.AddSheet("Sheet1");
        smallSheet.SetCell(new CellAddress(smallSheet.Id, 1, 1), new TextValue("ok"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(largeWorkbook, stream);
        stream.Position = 0;

        adapter.Save(smallWorkbook, stream);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("ok\r\n");
    }

    [Fact]
    public void Save_PreservesFractionalSecondsInDateTimeValues()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new DateTimeValue(new TimeSpan(0, 9, 30, 15, 250).TotalDays));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("2026-05-17 09:30:15.25,09:30:15.25\r\n");
    }

    [Fact]
    public void Save_IgnoresCellsBeyondExcelGridLimits()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1), new TextValue("overflow-column"));
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1), new TextValue("overflow-row"));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("visible\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankColumnsFromWorksheetCoordinates()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("offset"));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be(",offset\r\n");
    }

    [Fact]
    public void Save_PreservesLeadingBlankRowsFromWorksheetCoordinates()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("offset"));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("\r\noffset\r\n");
    }

    [Fact]
    public void Save_QuotesFormulaLikeTextFieldsToPreserveLiteralText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("=A1*2"));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("\"'=A1*2\"\r\n");
    }

    [Fact]
    public void Save_QuotesTextFieldsThatNeedCsvEscaping()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a,b"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("say \"hi\""));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("line\nbreak"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("carriage\rreturn"));

        SaveToUtf8Text(new CsvFileAdapter(), workbook).Should().Be("\"a,b\",\"say \"\"hi\"\"\",\"line\nbreak\",\"carriage\rreturn\"\r\n");
    }

    [Fact]
    public void Save_RoundTripsQuotedFieldsWithEmbeddedCrLfAndQuotes()
    {
        var text = "line 1\r\n\"quoted\"\r\nline 3";
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(text));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("tail"));

        var adapter = new CsvFileAdapter();
        var roundTripped = SaveAndLoad(adapter, workbook);
        var loadedSheet = roundTripped.Sheets.Single();

        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue(text));
        loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new TextValue("tail"));
    }
}
