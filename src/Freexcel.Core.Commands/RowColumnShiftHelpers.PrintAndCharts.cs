using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftPrintAreaRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeRowsDown(printArea, start, count);
    }

    internal static void ShiftPrintAreaColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsUp(printArea, start, count);
    }

    internal static void ShiftPrintAreaColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintArea is { } printArea)
            sheet.PrintArea = ShiftRangeColumnsDown(printArea, start, count);
    }

    internal static List<GridRange> CaptureChartDataRanges(Sheet sheet) =>
        sheet.Charts.Select(c => c.DataRange).ToList();

    internal static void RestoreChartDataRanges(Sheet sheet, List<GridRange>? snapshot)
    {
        if (snapshot is null) return;
        for (int i = 0; i < sheet.Charts.Count && i < snapshot.Count; i++)
            sheet.Charts[i].DataRange = snapshot[i];
    }

    internal static void ShiftChartRowsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartRowsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeRowsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    internal static void ShiftChartColumnsUp(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartColumnsDown(Sheet sheet, SheetId sheetId, uint start, uint count)
    {
        foreach (var chart in sheet.Charts)
            if (chart.DataRange.Start.Sheet == sheetId)
                chart.DataRange = ShiftRangeColumnsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }
}
