using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    private static void SeedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
    }

    private static void SeedPivotRefreshPerformanceData(Sheet sheet, int rowCount, int columnItemCount)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Bucket"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));

        for (var index = 0; index < rowCount; index++)
        {
            var row = (uint)index + 2;
            var bucketIndex = index % columnItemCount;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Region {index % 16:00}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"Bucket {bucketIndex:000}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue((bucketIndex % 23) + 1));
        }
    }

    private static void SeedSparseSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(25));
    }

    private static void SeedSalesChannelData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "A6"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B6"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C6"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D6"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A7"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B7"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C7"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D7"), new NumberValue(35));
        sheet.SetCell(Addr(sheet, "A8"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B8"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C8"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "D8"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "A9"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B9"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C9"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "D9"), new NumberValue(45));
    }

    private static void SeedSalesProductChannelData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Product"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Channel"));
        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "C2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "D2"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "E2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Widget"));
        sheet.SetCell(Addr(sheet, "C3"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "D3"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "E3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Gadget"));
        sheet.SetCell(Addr(sheet, "C4"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "D4"), new TextValue("Retail"));
        sheet.SetCell(Addr(sheet, "E4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Gadget"));
        sheet.SetCell(Addr(sheet, "C5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "D5"), new TextValue("Wholesale"));
        sheet.SetCell(Addr(sheet, "E5"), new NumberValue(25));
    }

    private static void SeedSalesWithUnitsData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Units"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "D2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "D3"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "D4"), new NumberValue(4));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "D5"), new NumberValue(2.2));
    }

    private static void SeedDatedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Order Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 20)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 28)));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    private static void SeedPriceSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(7));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(12));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(17));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    private static string Text(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is NumberValue number ? number.Value : double.NaN;

    private static string PivotValueText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue date => date.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => ""
        };

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));
}
