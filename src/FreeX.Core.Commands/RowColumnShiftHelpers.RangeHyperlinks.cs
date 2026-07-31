using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    // R106-io-hyperlink-range-shift: Sheet.RangeHyperlinks tracks whole-column/whole-row and
    // oversized-bounded-range hyperlink refs that can never be represented by the single-CellAddress-
    // keyed Sheet.Hyperlinks/HyperlinkMetadata dictionaries (see that property's doc comment). Its
    // values are live GridRanges that must shift on row/column insert/delete exactly like
    // DataValidation/ConditionalFormat ranges (ShiftRuleRowsUp/Down/ColumnsUp/Down above) -- reusing
    // the very same ShiftRange*Up/Down helpers, which already special-case a whole-column/whole-row
    // selection as the "perpendicular axis" no-op case.

    internal static List<KeyValuePair<string, GridRange>>? CaptureRangeHyperlinks(Sheet sheet) =>
        CaptureDictionary(sheet.RangeHyperlinks);

    internal static void RestoreRangeHyperlinks(Sheet sheet, List<KeyValuePair<string, GridRange>>? snapshot) =>
        RestoreDictionary(sheet.RangeHyperlinks, snapshot);

    internal static void ShiftRangeHyperlinksRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.RangeHyperlinks.Count == 0)
            return;

        foreach (var key in sheet.RangeHyperlinks.Keys.ToList())
            sheet.RangeHyperlinks[key] = ShiftRangeRowsUp(sheet.RangeHyperlinks[key], start, count);
    }

    internal static void ShiftRangeHyperlinksRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.RangeHyperlinks.Count == 0)
            return;

        foreach (var key in sheet.RangeHyperlinks.Keys.ToList())
        {
            var shifted = ShiftRangeRowsDown(sheet.RangeHyperlinks[key], start, count);
            if (shifted is null)
                sheet.RangeHyperlinks.Remove(key);
            else
                sheet.RangeHyperlinks[key] = shifted.Value;
        }
    }

    internal static void ShiftRangeHyperlinksColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.RangeHyperlinks.Count == 0)
            return;

        foreach (var key in sheet.RangeHyperlinks.Keys.ToList())
            sheet.RangeHyperlinks[key] = ShiftRangeColumnsUp(sheet.RangeHyperlinks[key], start, count);
    }

    internal static void ShiftRangeHyperlinksColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.RangeHyperlinks.Count == 0)
            return;

        foreach (var key in sheet.RangeHyperlinks.Keys.ToList())
        {
            var shifted = ShiftRangeColumnsDown(sheet.RangeHyperlinks[key], start, count);
            if (shifted is null)
                sheet.RangeHyperlinks.Remove(key);
            else
                sheet.RangeHyperlinks[key] = shifted.Value;
        }
    }
}
