using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static Dictionary<string, NamedRangeSnapshot> CaptureNamedRanges(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(
            pair => pair.Key,
            pair => new NamedRangeSnapshot(
                pair.Value,
                workbook.TryGetNamedRangeMetadata(pair.Key, out var metadata) ? metadata : NamedRangeMetadata.WorkbookScope),
            StringComparer.OrdinalIgnoreCase);

    internal static void RestoreNamedRanges(Workbook workbook, Dictionary<string, NamedRangeSnapshot>? snapshot)
    {
        if (snapshot is null)
            return;

        workbook.NamedRanges.Clear();
        workbook.NamedRangeMetadataByName.Clear();
        foreach (var (name, namedRange) in snapshot)
            workbook.DefineNamedRange(name, namedRange.Range, namedRange.Metadata);
    }

    internal static void ShiftNamedRangeRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeRowsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeRowsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }

    internal static void ShiftNamedRangeColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeColumnsUp(range, start, count);
        }
    }

    internal static void ShiftNamedRangeColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeColumnsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }
    }
}
