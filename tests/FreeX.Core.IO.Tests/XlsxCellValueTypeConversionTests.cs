using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

// Type-conversion fidelity for the XLSX load path. Regression for the 2026-06-05 fidelity-batch finding:
// numeric cells with a time/duration number format (which ClosedXML surfaces as XLCellValue.IsTimeSpan)
// were falling through to TextValue("9:00:00") instead of staying numeric like Excel (0.375).
public sealed class XlsxCellValueTypeConversionTests
{
    private static Workbook RoundTrip(Action<Workbook, Sheet> build)
    {
        var workbook = new Workbook("Types");
        var sheet = workbook.AddSheet("S");
        build(workbook, sheet);
        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static ScalarValue ValueAt(Workbook workbook, uint row, uint col) =>
        workbook.GetSheetAt(0).GetCell(row, col)!.Value;

    [Theory]
    [InlineData("h:mm:ss", 0.375)]      // 09:00:00 time of day
    [InlineData("h:mm AM/PM", 0.5)]     // 12:00 PM
    [InlineData("[h]:mm:ss", 1.5)]      // 36:00:00 elapsed duration (> 1 day)
    public void Load_TimeOrDurationFormattedNumber_StaysNumericNotText(string numberFormat, double serial)
    {
        var loaded = RoundTrip((wb, sheet) =>
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
            sheet.GetCell(1u, 1u)!.StyleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        });

        var value = ValueAt(loaded, 1, 1);
        value.Should().BeOfType<NumberValue>($"a numeric cell formatted '{numberFormat}' must round-trip as a number, not text");
        ((NumberValue)value).Value.Should().BeApproximately(serial, 1e-9, "the exact day-fraction serial must be preserved");
    }

    [Fact]
    public void Load_DateFormattedNumber_RoundTripsToSameSerial()
    {
        // Sanity: dates (ClosedXML IsDateTime) are already handled; guard that they stay numerically equal.
        var serial = new DateTime(2026, 6, 5, 9, 30, 0).ToOADate();
        var loaded = RoundTrip((wb, sheet) =>
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(serial));
            sheet.GetCell(1u, 1u)!.StyleId = wb.RegisterStyle(new CellStyle { NumberFormat = "m/d/yyyy h:mm" });
        });

        var value = ValueAt(loaded, 1, 1);
        var asSerial = value switch
        {
            NumberValue n => n.Value,
            DateTimeValue d => d.Value,
            _ => double.NaN,
        };
        asSerial.Should().BeApproximately(serial, 1e-6, "a date-formatted number must round-trip to the same serial");
    }

    [Fact]
    public void Load_PreservesPlainNumberTextBoolean()
    {
        var loaded = RoundTrip((wb, sheet) =>
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42.5));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hello"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new BoolValue(true));
        });

        ValueAt(loaded, 1, 1).Should().BeOfType<NumberValue>();
        ValueAt(loaded, 2, 1).Should().BeOfType<TextValue>();
        ValueAt(loaded, 3, 1).Should().BeOfType<BoolValue>();
    }
}
