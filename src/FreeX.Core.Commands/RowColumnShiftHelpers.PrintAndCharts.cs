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

            result.Add(verbatim is not null || dlFormulas is not null
                ? new ChartVerbatimSnapshot
                {
                    VerbatimSeriesFormulas = verbatim,
                    DataLabelFormulas      = dlFormulas
                }
                : null);
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
        }
    }

    /// <summary>
    /// Restores verbatim series formulas and data-label source formulas from a
    /// snapshot captured by <see cref="CaptureChartVerbatimFormulas"/>.
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
