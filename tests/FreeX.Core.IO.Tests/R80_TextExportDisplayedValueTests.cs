using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.Core.IO.Tests.TextFileAdapterTestHelper;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-services-export-formats-5-1: CSV/TSV/TXT/PRN Save-As must write the cell's DISPLAYED
/// (number-formatted) text, matching real Excel's plain-text Save-As types — not the bare raw
/// value, which loses percent signs, currency symbols/grouping, and custom date shapes entirely.
/// </summary>
public sealed class R80_TextExportDisplayedValueTests
{
    [Fact]
    public void Csv_Save_WritesPercentFormattedNumberAsDisplayedText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(0.15));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0%" });

        var savedText = SaveToUtf8Text(new CsvFileAdapter(), workbook);

        savedText.Should().Be("15%\r\n");
    }

    [Fact]
    public void Csv_Save_WritesCurrencyFormattedNumberAsDisplayedText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1234.5));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });

        var savedText = SaveToUtf8Text(new CsvFileAdapter(), workbook);

        // The formatted text contains the CSV delimiter (','), so real Excel quotes the field.
        savedText.Should().Be("\"$1,234.50\"\r\n");
    }

    [Fact]
    public void Csv_Save_WritesCustomDateFormattedValueAsDisplayedText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 7, 22)));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "dddd, mmmm d, yyyy" });

        var savedText = SaveToUtf8Text(new CsvFileAdapter(), workbook);

        savedText.Should().Be("\"Wednesday, July 22, 2026\"\r\n");
    }

    // --- No-regression sibling: unformatted ("General") numbers/dates keep the existing raw-value
    // rendering (round-trip invariant numbers, ISO dates) since there is no explicit format to honor.
    [Fact]
    public void Csv_Save_UnformattedGeneralCells_StillWriteRawValues()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0.15));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), DateTimeValue.FromDateTime(new DateTime(2026, 7, 22)));

        var savedText = SaveToUtf8Text(new CsvFileAdapter(), workbook);

        savedText.Should().Be("0.15,2026-07-22\r\n");
    }

    [Fact]
    public void Csv_Save_InvalidStyleIds_FallBackToGeneralWithoutChangingRawValues()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1.25));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2.5));
        sheet.GetCell(1, 1)!.StyleId = new StyleId(-1);
        sheet.GetCell(1, 2)!.StyleId = new StyleId(9999);

        var savedText = SaveToUtf8Text(new CsvFileAdapter(), workbook);

        savedText.Should().Be("1.25,2.5\r\n");
    }

    [Fact]
    public void Prn_Save_WritesPercentFormattedNumberAsDisplayedText()
    {
        var (workbook, sheet) = CreateWorkbookWithSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0.15));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0%" });

        var savedText = SaveToUtf8Text(new PrnFileAdapter(), workbook);

        savedText.Should().Be("15%\r\n");
    }
}
