using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-datetimevalue-1900-serial: an Excel-authored date in 1900-01-01..1900-02-28 must load onto
/// the serial the file actually stores. ClosedXML (like NPOI on the legacy .xls path) surfaces the
/// TRUE calendar date for such a cell — stored serial 15 comes back as 1900-01-15 — and that date's
/// .NET OLE Automation value is 16, one past the Excel serial. The load mapper therefore has to
/// convert through DateTimeValue rather than calling ToOADate(), or a workbook dated 1/15/1900
/// loads as serial 16 and both renders and computes as 1/16/1900.
/// </summary>
public sealed class XlsxEarly1900DateSerialTests
{
    // Raw Excel serials as Excel itself stores them: 1 = 1900-01-01 ... 59 = 1900-02-28, 61 = 1900-03-01.
    private static MemoryStream WriteWorkbookWithRawSerials(params double[] serials)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("S");
            for (var i = 0; i < serials.Length; i++)
            {
                var cell = worksheet.Cell(i + 1, 1);
                cell.Value = serials[i];
                cell.Style.NumberFormat.Format = "m/d/yyyy";
            }
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(45306)]
    public void Load_KeepsTheStoredExcelSerial(double storedSerial)
    {
        using var stream = WriteWorkbookWithRawSerials(storedSerial);

        var workbook = new XlsxFileAdapter().Load(stream);
        var sheet = workbook.Sheets[0];

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().BeOfType<DateTimeValue>()
            .Which.Value.Should().Be(storedSerial);
    }

    [Fact]
    public void SaveAfterLoad_RoundTripsTheStoredExcelSerialUnchanged()
    {
        using var source = WriteWorkbookWithRawSerials(1, 15, 59, 61);
        var workbook = new XlsxFileAdapter().Load(source);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var reopened = new XLWorkbook(saved);
        var worksheet = reopened.Worksheet(1);
        foreach (var (row, serial) in new[] { (1, 1.0), (2, 15.0), (3, 59.0), (4, 61.0) })
        {
            worksheet.Cell(row, 1).GetDateTime()
                .Should().Be(new DateTimeValue(serial).ToDateTime(), $"row {row} holds serial {serial}");
        }
    }
}
