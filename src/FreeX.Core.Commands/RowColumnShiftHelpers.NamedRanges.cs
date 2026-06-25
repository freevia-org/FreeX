using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    /// <summary>
    /// Rewrites all NamedFormulas (defined names whose refers-to is a formula expression)
    /// for a structural insert/delete operation.  Entries that change are recorded in
    /// <paramref name="snapshot"/> so they can be restored by <see cref="RestoreNamedFormulas"/>.
    /// The host-sheet name required by <see cref="FormulaRewriter"/> is taken from the first
    /// sheet in the workbook (NamedFormulas are workbook-scoped, so any sheet name suffices
    /// for absolute-reference rewriting).
    /// </summary>
    internal static void RewriteNamedFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<string, string> snapshot)
    {
        if (workbook.NamedFormulas.Count == 0)
            return;

        var hostSheetName = workbook.Sheets.Count > 0 ? workbook.Sheets[0].Name : string.Empty;

        foreach (var name in workbook.NamedFormulas.Keys.ToList())
        {
            var original = workbook.NamedFormulas[name];
            var rewritten = FormulaRewriter.Rewrite(original, op, hostSheetName);
            if (rewritten is null || rewritten == original)
                continue;

            snapshot[name] = original;
            workbook.NamedFormulas[name] = rewritten;
        }
    }

    /// <summary>
    /// Restores NamedFormulas from a snapshot captured by <see cref="RewriteNamedFormulas"/>.
    /// </summary>
    internal static void RestoreNamedFormulas(Workbook workbook, Dictionary<string, string>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (name, original) in snapshot)
            workbook.NamedFormulas[name] = original;
    }

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
