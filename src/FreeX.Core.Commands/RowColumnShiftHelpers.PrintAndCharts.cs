using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftPrintAreaRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintAreas.Count == 0) return;
        sheet.SetPrintAreas(sheet.PrintAreas.Select(r => ShiftRangeRowsUp(r, start, count)));
    }

    internal static void ShiftPrintAreaRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintAreas.Count == 0) return;
        sheet.SetPrintAreas(sheet.PrintAreas.Select(r => ShiftRangeRowsDown(r, start, count))
            .Where(r => r.HasValue).Select(r => r!.Value));
    }

    internal static void ShiftPrintAreaColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintAreas.Count == 0) return;
        sheet.SetPrintAreas(sheet.PrintAreas.Select(r => ShiftRangeColumnsUp(r, start, count)));
    }

    internal static void ShiftPrintAreaColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.PrintAreas.Count == 0) return;
        sheet.SetPrintAreas(sheet.PrintAreas.Select(r => ShiftRangeColumnsDown(r, start, count))
            .Where(r => r.HasValue).Select(r => r!.Value));
    }

    // NOTE: chart hosting is per-sheet (Sheet.Charts) but a chart's DataRange/verbatim
    // formulas may reference a *different* sheet (e.g. a chart on "Dashboard" plotting
    // data on "Data"). All of the Capture/Restore/Shift/Rewrite helpers below therefore
    // iterate every sheet in the workbook — not just the sheet being structurally edited —
    // so a chart hosted anywhere still has its references corrected when the sheet its
    // DataRange/verbatim formulas point at is edited. This mirrors the workbook-wide walk
    // already used by RewriteAllFormulas and ShiftNamedRangeRowsUp/etc.

    /// <summary>
    /// Snapshot of a single sheet's charts' DataRange values, keyed by the hosting sheet
    /// so <see cref="RestoreChartDataRanges(Workbook, List{ChartDataRangeWorkbookSnapshot}?)"/>
    /// can restore each chart on its own sheet.
    /// </summary>
    internal sealed class ChartDataRangeWorkbookSnapshot
    {
        public required SheetId HostSheet { get; init; }
        public required List<GridRange> Ranges { get; init; }
    }

    internal static List<ChartDataRangeWorkbookSnapshot> CaptureChartDataRanges(Workbook workbook)
    {
        var result = new List<ChartDataRangeWorkbookSnapshot>(workbook.Sheets.Count);
        foreach (var s in workbook.Sheets)
        {
            if (s.Charts.Count == 0) continue;
            result.Add(new ChartDataRangeWorkbookSnapshot
            {
                HostSheet = s.Id,
                Ranges = s.Charts.Select(c => c.DataRange).ToList()
            });
        }
        return result;
    }

    internal static void RestoreChartDataRanges(Workbook workbook, List<ChartDataRangeWorkbookSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            var sheet = workbook.GetSheet(entry.HostSheet);
            if (sheet is null) continue;
            for (int i = 0; i < sheet.Charts.Count && i < entry.Ranges.Count; i++)
                sheet.Charts[i].DataRange = entry.Ranges[i];
        }
    }

    internal static void ShiftChartRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
                if (chart.DataRange.Start.Sheet == sheetId)
                    chart.DataRange = ShiftRangeRowsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
                if (chart.DataRange.Start.Sheet == sheetId)
                    chart.DataRange = ShiftRangeRowsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    internal static void ShiftChartColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
                if (chart.DataRange.Start.Sheet == sheetId)
                    chart.DataRange = ShiftRangeColumnsUp(chart.DataRange, start, count);
    }

    internal static void ShiftChartColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
                if (chart.DataRange.Start.Sheet == sheetId)
                    chart.DataRange = ShiftRangeColumnsDown(chart.DataRange, start, count) ?? chart.DataRange;
    }

    // ── Chart series-column-mapping shifting ──────────────────────────────────
    // ChartSeriesColumnMapping.ValueColumn is an ABSOLUTE worksheet column index (see
    // ChartModel.Support.cs), parsed once at load time from each series' <c:val> range so the
    // renderer can plot exactly the mapped columns (combo charts that skip columns, or list
    // series out of column order — see ChartRenderer.SeriesFormatting.cs HasAuthoritativeSeriesColumns).
    // It must be shifted in lockstep with DataRange on column insert/delete, or a mapping silently
    // keeps pointing at its old absolute column while the underlying data physically moved,
    // rendering a phantom/blank series in the inserted gap and dropping the real series that moved
    // into the mapped column's old slot (R14-chart-editing-1). Row insert/delete does not touch
    // this — the mapping is column-only — so there is no corresponding row-shift variant.

    /// <summary>
    /// Snapshot of a single sheet's charts' <see cref="ChartModel.SeriesColumnMappings"/> lists,
    /// keyed by the hosting sheet and chart index, for undo.
    /// </summary>
    internal sealed class ChartSeriesColumnMappingsWorkbookSnapshot
    {
        public required SheetId HostSheet { get; init; }
        public required List<List<ChartSeriesColumnMapping>> Charts { get; init; }
    }

    internal static List<ChartSeriesColumnMappingsWorkbookSnapshot> CaptureChartSeriesColumnMappings(Workbook workbook)
    {
        var result = new List<ChartSeriesColumnMappingsWorkbookSnapshot>(workbook.Sheets.Count);
        foreach (var s in workbook.Sheets)
        {
            if (s.Charts.Count == 0) continue;
            result.Add(new ChartSeriesColumnMappingsWorkbookSnapshot
            {
                HostSheet = s.Id,
                Charts = s.Charts.Select(c => new List<ChartSeriesColumnMapping>(c.SeriesColumnMappings)).ToList()
            });
        }
        return result;
    }

    internal static void RestoreChartSeriesColumnMappings(
        Workbook workbook, List<ChartSeriesColumnMappingsWorkbookSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            var sheet = workbook.GetSheet(entry.HostSheet);
            if (sheet is null) continue;
            for (int i = 0; i < sheet.Charts.Count && i < entry.Charts.Count; i++)
                sheet.Charts[i].SeriesColumnMappings = entry.Charts[i];
        }
    }

    /// <summary>
    /// Shifts every mapped value column at or after <paramref name="start"/> up by
    /// <paramref name="count"/>, mirroring <see cref="ShiftRangeColumnsUp"/> for the chart's
    /// DataRange so a mapping still points at the same (now relocated) worksheet column.
    /// </summary>
    internal static void ShiftChartSeriesColumnMappingsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                if (chart.DataRange.Start.Sheet != sheetId || chart.SeriesColumnMappings.Count == 0)
                    continue;

                for (int i = 0; i < chart.SeriesColumnMappings.Count; i++)
                {
                    var mapping = chart.SeriesColumnMappings[i];
                    if (mapping.ValueColumn >= start)
                        chart.SeriesColumnMappings[i] = mapping with
                        {
                            ValueColumn = Math.Min(mapping.ValueColumn + count, CellAddress.MaxCol)
                        };
                }
            }
    }

    /// <summary>
    /// Shifts every mapped value column after the deleted span down by <paramref name="count"/>,
    /// mirroring <see cref="ShiftRangeColumnsDown"/>. A mapping whose column falls inside the
    /// deleted span itself is dropped (the worksheet column it named no longer exists), matching
    /// how an overlapping DataRange shrinks rather than keeping a stale column reference.
    /// </summary>
    internal static void ShiftChartSeriesColumnMappingsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        var end = start + count - 1;
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                if (chart.DataRange.Start.Sheet != sheetId || chart.SeriesColumnMappings.Count == 0)
                    continue;

                var shifted = new List<ChartSeriesColumnMapping>(chart.SeriesColumnMappings.Count);
                foreach (var mapping in chart.SeriesColumnMappings)
                {
                    if (mapping.ValueColumn > end)
                        shifted.Add(mapping with { ValueColumn = mapping.ValueColumn - count });
                    else if (mapping.ValueColumn < start)
                        shifted.Add(mapping);
                    // else: the mapped column itself was deleted — drop the mapping.
                }
                chart.SeriesColumnMappings = shifted;
            }
    }

    // ── Verbatim series formula / data-label formula shifting ─────────────────
    // VerbatimSeriesFormulas holds multi-area or non-rectangular series formula
    // strings that cannot be expressed as a single GridRange. They must also be
    // shifted on structural edits so the chart source references stay correct.
    // SeriesRangeDataLabels.Formula holds the c15:f source formula for "value
    // from cells" data labels and likewise needs shifting.
    //
    // EmbeddedSeriesData is purely cached numeric/string data (numCache/strCache)
    // with no reference strings — leave it untouched.

    /// <summary>
    /// Snapshot of a single chart's verbatim formula strings for undo.
    /// Keyed by chart index within <see cref="Sheet.Charts"/>.
    /// </summary>
    internal sealed class ChartVerbatimSnapshot
    {
        public List<ChartSeriesVerbatimFormulas>? VerbatimSeriesFormulas { get; init; }
        // Per-series data-label formula snapshot: (SeriesIndex, Formula?)
        public List<(int SeriesIndex, string? Formula)>? DataLabelFormulas { get; init; }
        // Custom error-bar plus/minus range-source formulas (R16-chart-datasource-editing-1).
        // ErrorBarsCaptured distinguishes "chart had error-bar formulas" from "not captured".
        public bool ErrorBarsCaptured { get; init; }
        public string? ErrorBarPlusRangeFormula { get; init; }
        public string? ErrorBarMinusRangeFormula { get; init; }
    }

    /// <summary>
    /// Workbook-wide snapshot of <see cref="ChartVerbatimSnapshot"/>s, keyed by the
    /// hosting sheet so <see cref="RestoreChartVerbatimFormulas(Workbook, List{ChartVerbatimWorkbookSnapshot}?)"/>
    /// can restore each chart on its own sheet regardless of which sheet triggered the edit.
    /// </summary>
    internal sealed class ChartVerbatimWorkbookSnapshot
    {
        public required SheetId HostSheet { get; init; }
        public required List<ChartVerbatimSnapshot?> Charts { get; init; }
    }

    /// <summary>
    /// Captures the verbatim series formulas and data-label source formulas for
    /// all charts on the sheet so they can be restored on undo.
    /// </summary>
    internal static List<ChartVerbatimSnapshot?> CaptureChartVerbatimFormulas(Sheet sheet)
    {
        var result = new List<ChartVerbatimSnapshot?>(sheet.Charts.Count);
        foreach (var chart in sheet.Charts)
        {
            List<ChartSeriesVerbatimFormulas>? verbatim = null;
            if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
            {
                verbatim = new List<ChartSeriesVerbatimFormulas>(vf.Count);
                foreach (var f in vf)
                    verbatim.Add(f); // records are immutable — safe to share
            }

            List<(int, string?)>? dlFormulas = null;
            if (chart.SeriesRangeDataLabels is { Count: > 0 } dl)
            {
                dlFormulas = new List<(int, string?)>(dl.Count);
                foreach (var d in dl)
                    dlFormulas.Add((d.SeriesIndex, d.Formula));
            }

            var hasErrorBars = chart.ErrorBarPlusRangeFormula is not null || chart.ErrorBarMinusRangeFormula is not null;

            result.Add(verbatim is not null || dlFormulas is not null || hasErrorBars
                ? new ChartVerbatimSnapshot
                {
                    VerbatimSeriesFormulas    = verbatim,
                    DataLabelFormulas         = dlFormulas,
                    ErrorBarsCaptured         = hasErrorBars,
                    ErrorBarPlusRangeFormula  = chart.ErrorBarPlusRangeFormula,
                    ErrorBarMinusRangeFormula = chart.ErrorBarMinusRangeFormula
                }
                : null);
        }
        return result;
    }

    /// <summary>
    /// Captures the verbatim series/data-label formulas for every chart on every sheet
    /// in the workbook (see <see cref="CaptureChartVerbatimFormulas(Sheet)"/>).
    /// </summary>
    internal static List<ChartVerbatimWorkbookSnapshot> CaptureChartVerbatimFormulas(Workbook workbook)
    {
        var result = new List<ChartVerbatimWorkbookSnapshot>(workbook.Sheets.Count);
        foreach (var s in workbook.Sheets)
        {
            if (s.Charts.Count == 0) continue;
            result.Add(new ChartVerbatimWorkbookSnapshot
            {
                HostSheet = s.Id,
                Charts    = CaptureChartVerbatimFormulas(s)
            });
        }
        return result;
    }

    /// <summary>
    /// Rewrites cell references inside verbatim series formulas and data-label
    /// source formulas for all charts on the sheet, using the same
    /// <see cref="FormulaRewriter"/> path used for cell formulas.
    /// </summary>
    internal static void RewriteChartVerbatimFormulas(
        Sheet sheet, RewriteOperation op, string hostSheetName)
    {
        foreach (var chart in sheet.Charts)
        {
            if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
            {
                for (int i = 0; i < vf.Count; i++)
                {
                    var entry = vf[i];
                    var newVal = RewriteVerbatimFormula(entry.ValFormula, op, hostSheetName);
                    var newCat = RewriteVerbatimFormula(entry.CatFormula, op, hostSheetName);
                    var newTx  = RewriteVerbatimFormula(entry.TxFormula,  op, hostSheetName);
                    if (!ReferenceEquals(newVal, entry.ValFormula) ||
                        !ReferenceEquals(newCat, entry.CatFormula) ||
                        !ReferenceEquals(newTx,  entry.TxFormula))
                    {
                        vf[i] = entry with
                        {
                            ValFormula = newVal,
                            CatFormula = newCat,
                            TxFormula  = newTx
                        };
                    }
                }
            }

            if (chart.SeriesRangeDataLabels is { Count: > 0 } dl)
            {
                for (int i = 0; i < dl.Count; i++)
                {
                    var entry = dl[i];
                    if (entry.Formula is not { } formula)
                        continue;
                    var rewritten = FormulaRewriter.Rewrite(formula, op, hostSheetName);
                    if (rewritten is not null && rewritten != formula)
                        dl[i] = entry with { Formula = rewritten };
                }
            }

            // R16-chart-datasource-editing-1: custom error-bar +/- range formulas track
            // structural edits just like the series value/category formulas above.
            var newPlus  = RewriteVerbatimFormula(chart.ErrorBarPlusRangeFormula,  op, hostSheetName);
            var newMinus = RewriteVerbatimFormula(chart.ErrorBarMinusRangeFormula, op, hostSheetName);
            if (!ReferenceEquals(newPlus, chart.ErrorBarPlusRangeFormula))
                chart.ErrorBarPlusRangeFormula = newPlus;
            if (!ReferenceEquals(newMinus, chart.ErrorBarMinusRangeFormula))
                chart.ErrorBarMinusRangeFormula = newMinus;
        }
    }

    /// <summary>
    /// Rewrites verbatim series/data-label formulas for every chart on every sheet in the
    /// workbook for a structural operation on <paramref name="op"/>'s sheet. Each chart's
    /// own hosting sheet name (not necessarily the edited sheet) is passed as the
    /// <c>hostSheetName</c> so unqualified refs inside a chart's verbatim formula — if any —
    /// resolve relative to the sheet the chart actually lives on, matching how
    /// <see cref="RewriteAllFormulas"/> uses each cell's own sheet.
    /// </summary>
    internal static void RewriteChartVerbatimFormulas(Workbook workbook, RewriteOperation op)
    {
        foreach (var s in workbook.Sheets)
            RewriteChartVerbatimFormulas(s, op, s.Name);
    }

    /// <summary>
    /// Restores verbatim series formulas and data-label source formulas from a
    /// snapshot captured by <see cref="CaptureChartVerbatimFormulas(Sheet)"/>.
    /// </summary>
    internal static void RestoreChartVerbatimFormulas(
        Sheet sheet, List<ChartVerbatimSnapshot?>? snapshot)
    {
        if (snapshot is null) return;
        for (int i = 0; i < sheet.Charts.Count && i < snapshot.Count; i++)
        {
            var snap = snapshot[i];
            if (snap is null) continue;
            var chart = sheet.Charts[i];

            if (snap.VerbatimSeriesFormulas is not null)
            {
                if (chart.VerbatimSeriesFormulas is null)
                    chart.VerbatimSeriesFormulas = new List<ChartSeriesVerbatimFormulas>(snap.VerbatimSeriesFormulas.Count);
                else
                    chart.VerbatimSeriesFormulas.Clear();
                chart.VerbatimSeriesFormulas.AddRange(snap.VerbatimSeriesFormulas);
            }

            if (snap.DataLabelFormulas is not null)
            {
                // Restore only the Formula field of each snapshotted entry; leave
                // PointCount and Points (cached display strings) untouched because
                // they are derived from data, not from cell references.
                var dlIndex = chart.SeriesRangeDataLabels
                    .Select((d, idx) => (d, idx))
                    .ToDictionary(t => t.d.SeriesIndex, t => t.idx);
                foreach (var (seriesIndex, formula) in snap.DataLabelFormulas)
                {
                    if (dlIndex.TryGetValue(seriesIndex, out var listIdx))
                    {
                        var entry = chart.SeriesRangeDataLabels[listIdx];
                        chart.SeriesRangeDataLabels[listIdx] = entry with { Formula = formula };
                    }
                }
            }

            // R16-chart-datasource-editing-1: undo restores the pre-edit error-bar range formulas.
            if (snap.ErrorBarsCaptured)
            {
                chart.ErrorBarPlusRangeFormula = snap.ErrorBarPlusRangeFormula;
                chart.ErrorBarMinusRangeFormula = snap.ErrorBarMinusRangeFormula;
            }
        }
    }

    /// <summary>
    /// Restores verbatim series/data-label formulas for every chart on every sheet from
    /// a snapshot captured by <see cref="CaptureChartVerbatimFormulas(Workbook)"/>.
    /// </summary>
    internal static void RestoreChartVerbatimFormulas(Workbook workbook, List<ChartVerbatimWorkbookSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            var sheet = workbook.GetSheet(entry.HostSheet);
            if (sheet is null) continue;
            RestoreChartVerbatimFormulas(sheet, entry.Charts);
        }
    }

    // Rewrites a single verbatim formula string that may contain a '=' prefix and/or
    // surrounding parentheses and/or a comma-separated multi-area union.
    //
    // The REAL OOXML <c:f> format for multi-area unions is:
    //   (Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)   — parentheses, NO leading '='
    // This parenthesised form is exactly why these formulas land on the verbatim path:
    // the leading '(' makes TryParseFormulaRange fail.  A formula that also happens to
    // carry a '=' prefix (an edge-case form) is handled too.
    //
    // FormulaRewriter handles single range/cell expressions but not top-level comma unions,
    // so we strip the wrapper characters, split on unquoted commas, rewrite each area
    // individually, then re-wrap with the original wrapper characters.
    private static string? RewriteVerbatimFormula(
        string? formula, RewriteOperation op, string hostSheetName)
    {
        if (formula is null) return null;

        // Strip optional leading '=' (not present in the <c:f> form, but kept for safety).
        bool hasPrefix = formula.Length > 0 && formula[0] == '=';
        var body = hasPrefix ? formula[1..] : formula;

        // Strip balanced surrounding parentheses — the OOXML multi-area form is
        // "(Area1,Area2,...)" with NO '='.  We detect this so SplitOnUnquotedCommas
        // receives clean area strings without unbalanced '(' or ')' fragments.
        bool hasParens = body.Length >= 2 && body[0] == '(' && body[^1] == ')';
        if (hasParens)
            body = body[1..^1];

        // Split on unquoted commas to handle multi-area unions.
        var areas = SplitOnUnquotedCommas(body);
        bool anyChanged = false;
        var rewrittenAreas = new string[areas.Length];
        for (int i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            var rw = FormulaRewriter.Rewrite(area, op, hostSheetName);
            if (rw is not null && rw != area)
            {
                rewrittenAreas[i] = rw;
                anyChanged = true;
            }
            else
            {
                rewrittenAreas[i] = area;
            }
        }

        if (!anyChanged)
            return formula; // no change — return original (same reference for caller)

        var newBody = string.Join(",", rewrittenAreas);
        if (hasParens)
            newBody = "(" + newBody + ")";
        return hasPrefix ? "=" + newBody : newBody;
    }

    // Splits a comma-separated area-union string on commas that are not inside
    // single-quoted sheet names (e.g. 'Sheet, Name'!A1 must not be split on the
    // comma inside the quotes).
    private static string[] SplitOnUnquotedCommas(string text)
    {
        var parts = new List<string>();
        int start = 0;
        bool inQuote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'')
            {
                // Two consecutive single-quotes inside a quoted name are an escape.
                if (inQuote && i + 1 < text.Length && text[i + 1] == '\'')
                    i++; // skip escaped quote
                else
                    inQuote = !inQuote;
            }
            else if (c == ',' && !inQuote)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts.ToArray();
    }
}
