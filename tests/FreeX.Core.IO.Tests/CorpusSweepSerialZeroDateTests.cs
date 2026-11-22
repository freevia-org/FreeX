using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Corpus sweep: a date-formatted cell holding serial 0 ("1/0/1900" in Excel) loaded as serial 1
/// (1900-01-01, the closest calendar date ClosedXML can surface), so MIN/MAX-of-blank date totals
/// differed from Excel's cached 0.
/// </summary>
public class CorpusSweepSerialZeroDateTests
{
    [Fact]
    public void DateFormattedZeroSerial_LoadsAsZeroNotJanuaryFirst()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("S");
            sheet.Cell("A1").Value = 0;
            sheet.Cell("A1").Style.DateFormat.Format = "m/d/yyyy";
            sheet.Cell("A2").Value = 5;
            sheet.Cell("A2").Style.DateFormat.Format = "m/d/yyyy";
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream);
        var s = loaded.Sheets[0];

        var zero = s.GetValue(1, 1);
        (zero is NumberValue { Value: 0 } || zero is DateTimeValue { Value: 0 }).Should().BeTrue($"was {zero}");
        s.GetValue(2, 1).Should().Be(DateTimeValue.FromDateTime(new DateTime(1900, 1, 5)));
    }
}
