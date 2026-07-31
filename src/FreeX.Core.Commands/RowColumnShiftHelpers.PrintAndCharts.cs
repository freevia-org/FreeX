using System.Xml.Linq;
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
                {
                    var shifted = ShiftRangeRowsDown(chart.DataRange, start, count);
                    chart.DataRange = shifted ?? CollapseDeletedChartRowRange(chart.DataRange, start);
                }
    }

    /// <summary>
    /// ShiftRangeRowsDown returns <see langword="null"/> only when the deleted rows fully
    /// consumed <paramref name="original"/> (no row of it survives). <see cref="ChartModel.DataRange"/>
    /// is a non-nullable <see cref="GridRange"/> — unlike a cell formula, which can become a
    /// literal <c>#REF!</c> error — so there is no way to express "this chart's source is gone"
    /// in the current model (that would require a model-level change beyond this file's scope).
    /// Leaving <paramref name="original"/> completely unchanged (the prior behavior) is worse
    /// than this: because every row below the deleted band has since shifted up, the stale
    /// multi-row/-column extent would silently alias whatever unrelated data now occupies that
    /// entire coordinate window. Collapsing to a single row at the delete boundary — the first
    /// row now occupying that position after the shift — keeps <c>DataRange</c> valid and
    /// anchored at one deterministic location instead of a whole stale block
    /// (R44-commands-insert-delete-shift-3-3).
    /// </summary>
    private static GridRange CollapseDeletedChartRowRange(GridRange original, uint start)
    {
        var row = Math.Min(start, CellAddress.MaxRow);
        return new GridRange(
            new CellAddress(original.Start.Sheet, row, original.Start.Col),
            new CellAddress(original.End.Sheet,   row, original.End.Col));
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
                {
                    var shifted = ShiftRangeColumnsDown(chart.DataRange, start, count);
                    chart.DataRange = shifted ?? CollapseDeletedChartColumnRange(chart.DataRange, start);
                }
    }

    /// <summary>Column analogue of <see cref="CollapseDeletedChartRowRange"/>.</summary>
    private static GridRange CollapseDeletedChartColumnRange(GridRange original, uint start)
    {
        var col = Math.Min(start, CellAddress.MaxCol);
        return new GridRange(
            new CellAddress(original.Start.Sheet, original.Start.Row, col),
            new CellAddress(original.End.Sheet,   original.End.Row,   col));
    }

    // ── Chart drawing-position shifting (R86-commands-insert-move-refadjust-5-1) ─────────────────
    // Unlike DataRange (a cell reference, shifted above), ChartModel.Left/Top are absolute pixel
    // coordinates on the chart's OWN hosting sheet's canvas (see XlsxWorksheetChartWriter.ToAnchorMarker,
    // which converts them into a from-cell+offset marker by walking cumulative row/column sizes from
    // the sheet origin at SAVE time). Before this fix, no insert/delete command ever touched
    // Left/Top, so a chart anchored below/right of an inserted or deleted row/column band stayed
    // fixed on the canvas while the data it plots moved underneath it. A chart whose
    // DrawingAnchorKind is Absolute ("Don't move or size with cells") is deliberately excluded —
    // Excel keeps that kind fixed to the page, not to the underlying cells.
    // Only the chart's OWN hosting sheet matters here (unlike DataRange, Left/Top cannot reference
    // another sheet), so callers pass the single already-resolved Sheet, not the whole Workbook.

    internal sealed class ChartPositionSnapshot
    {
        public required ChartModel Chart { get; init; }
        public required double Left { get; init; }
        public required double Top { get; init; }
    }

    internal static List<ChartPositionSnapshot> CaptureChartPositions(Sheet sheet) =>
        sheet.Charts.Select(c => new ChartPositionSnapshot { Chart = c, Left = c.Left, Top = c.Top }).ToList();

    internal static void RestoreChartPositions(List<ChartPositionSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            entry.Chart.Left = entry.Left;
            entry.Chart.Top = entry.Top;
        }
    }

    /// <summary>
    /// Cumulative pixel size of every row/column strictly before <paramref name="index"/> (1-based),
    /// mirroring XlsxWorksheetChartWriter.ToAnchorMarker's cumulative walk closely enough to locate a
    /// chart relative to a structural-edit boundary (hidden-row/column zeroing is intentionally not
    /// modeled here — a documented simplification for this position-only shift, distinct from the
    /// writer's own save-time marker computation).
    /// </summary>
    private static double CumulativeSize(IEnumerable<KeyValuePair<uint, double>> customSizes, double defaultSize, uint index)
    {
        if (index <= 1) return 0;
        var total = (double)(index - 1) * defaultSize;
        foreach (var (i, size) in customSizes)
            if (i < index) total += size - defaultSize;
        return Math.Max(0, total);
    }

    /// <summary>Cumulative pixel height of every row strictly before <paramref name="row"/> (1-based) —
    /// the row's own top edge. Shared by ResizeSpanForShift (RowColumnShiftHelpers.AddressState.cs) for
    /// picture/shape/textbox "size with cells" resizing.</summary>
    private static double CumulativeRowTop(Sheet sheet, uint row) =>
        CumulativeSize(sheet.RowHeights, sheet.DefaultRowHeight, row);

    /// <summary>Column analogue of <see cref="CumulativeRowTop"/> — the column's own left edge, in the
    /// same *8 character-to-pixel unit as ChartModel.Left.</summary>
    private static double CumulativeColumnLeft(Sheet sheet, uint col) =>
        CumulativeSize(
            sheet.ColumnWidths.Select(kv => new KeyValuePair<uint, double>(kv.Key, kv.Value * 8)),
            sheet.DefaultColumnWidth * 8, col);

    /// <summary>
    /// Row insert: a chart anchored at/below <paramref name="start"/> (using the row heights as they
    /// stand at call time — rows before <paramref name="start"/> are untouched by a row insert, so
    /// this is safe to call either before or after sheet.RowHeights itself has been re-keyed) has its
    /// Top pushed down by the inserted band's height so it stays visually below the data that moved
    /// under it, matching Excel's "move and size with cells" twoCellAnchor behavior.
    /// </summary>
    internal static void ShiftChartPositionRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.Charts.Count == 0) return;
        var insertedHeight = count * sheet.DefaultRowHeight;
        if (insertedHeight <= 0) return;
        var boundary = CumulativeSize(sheet.RowHeights, sheet.DefaultRowHeight, start);
        foreach (var chart in sheet.Charts)
            if (chart.DrawingAnchorKind != ChartDrawingAnchorKind.Absolute && chart.Top >= boundary)
                chart.Top += insertedHeight;
    }

    /// <summary>
    /// Row delete: analogous to <see cref="ShiftChartPositionRowsUp"/>, but the deleted band's height
    /// must be measured from <paramref name="originalRowHeights"/>/<paramref name="originalDefaultRowHeight"/>
    /// captured BEFORE sheet.RowHeights was re-keyed for the delete (the deleted rows' own heights are
    /// gone from the live dictionary by the time callers can reach this). A chart anchored inside the
    /// deleted band collapses to the delete boundary — mirroring <see cref="CollapseDeletedChartRowRange"/>'s
    /// #REF!-vs-drop rationale, since Left/Top has no way to express "this chart's anchor is gone".
    /// </summary>
    internal static void ShiftChartPositionRowsDown(
        Sheet sheet, uint start, uint count,
        IEnumerable<KeyValuePair<uint, double>> originalRowHeights, double originalDefaultRowHeight)
    {
        if (sheet.Charts.Count == 0) return;
        var bandTop = CumulativeSize(originalRowHeights, originalDefaultRowHeight, start);
        var bandBottom = CumulativeSize(originalRowHeights, originalDefaultRowHeight, start + count);
        var removedHeight = bandBottom - bandTop;
        if (removedHeight <= 0) return;
        foreach (var chart in sheet.Charts)
        {
            if (chart.DrawingAnchorKind == ChartDrawingAnchorKind.Absolute) continue;
            if (chart.Top >= bandBottom)
                chart.Top -= removedHeight;
            else if (chart.Top >= bandTop)
                chart.Top = bandTop;
        }
    }

    /// <summary>Column analogue of <see cref="ShiftChartPositionRowsUp"/>. Widths use the same *8
    /// character-to-pixel factor as XlsxWorksheetChartWriter.ToAnchorMarker so the comparison against
    /// Left (already in that pixel unit) is consistent.</summary>
    internal static void ShiftChartPositionColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.Charts.Count == 0) return;
        var insertedWidth = count * sheet.DefaultColumnWidth * 8;
        if (insertedWidth <= 0) return;
        var boundary = CumulativeSize(
            sheet.ColumnWidths.Select(kv => new KeyValuePair<uint, double>(kv.Key, kv.Value * 8)),
            sheet.DefaultColumnWidth * 8, start);
        foreach (var chart in sheet.Charts)
            if (chart.DrawingAnchorKind != ChartDrawingAnchorKind.Absolute && chart.Left >= boundary)
                chart.Left += insertedWidth;
    }

    /// <summary>Column analogue of <see cref="ShiftChartPositionRowsDown"/>.</summary>
    internal static void ShiftChartPositionColumnsDown(
        Sheet sheet, uint start, uint count,
        IEnumerable<KeyValuePair<uint, double>> originalColumnWidths, double originalDefaultColumnWidth)
    {
        if (sheet.Charts.Count == 0) return;
        var scaledWidths = originalColumnWidths.Select(kv => new KeyValuePair<uint, double>(kv.Key, kv.Value * 8));
        var defaultWidth = originalDefaultColumnWidth * 8;
        var bandLeft = CumulativeSize(scaledWidths, defaultWidth, start);
        var bandRight = CumulativeSize(scaledWidths, defaultWidth, start + count);
        var removedWidth = bandRight - bandLeft;
        if (removedWidth <= 0) return;
        foreach (var chart in sheet.Charts)
        {
            if (chart.DrawingAnchorKind == ChartDrawingAnchorKind.Absolute) continue;
            if (chart.Left >= bandRight)
                chart.Left -= removedWidth;
            else if (chart.Left >= bandLeft)
                chart.Left = bandLeft;
        }
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
        // R100: snapshot of the verbatim multi-level category <c:cat> raw-XML entries so a
        // structural-edit undo restores the pre-edit <c:f> formula text alongside the other
        // verbatim collections (see RewriteMultiLevelCategoryXml).
        public List<ChartSeriesRawXmlEntry>? MultiLevelCategoryXml { get; init; }
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

            List<ChartSeriesRawXmlEntry>? multiLevelCategory = null;
            if (chart.MultiLevelCategoryXml is { Count: > 0 } mlc)
                multiLevelCategory = new List<ChartSeriesRawXmlEntry>(mlc); // records are immutable — safe to share

            result.Add(verbatim is not null || dlFormulas is not null || hasErrorBars || multiLevelCategory is not null
                ? new ChartVerbatimSnapshot
                {
                    VerbatimSeriesFormulas    = verbatim,
                    DataLabelFormulas         = dlFormulas,
                    ErrorBarsCaptured         = hasErrorBars,
                    ErrorBarPlusRangeFormula  = chart.ErrorBarPlusRangeFormula,
                    ErrorBarMinusRangeFormula = chart.ErrorBarMinusRangeFormula,
                    MultiLevelCategoryXml     = multiLevelCategory
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

            // R100: the verbatim <c:cat><c:multiLvlStrRef> raw-XML capture carries its own
            // <c:f> source-range formula, embedded inside the raw XML string rather than as a
            // scalar property, so it needs its own rewrite pass alongside VerbatimSeriesFormulas
            // above — otherwise a grouped/multi-level category axis keeps pointing at the
            // pre-edit cells after a structural row/column insert or delete.
            if (chart.MultiLevelCategoryXml is { Count: > 0 } mlc)
            {
                for (int i = 0; i < mlc.Count; i++)
                {
                    var entry = mlc[i];
                    var rewritten = RewriteMultiLevelCategoryXml(entry.RawXml, op, hostSheetName);
                    if (rewritten is not null && rewritten != entry.RawXml)
                        mlc[i] = entry with { RawXml = rewritten };
                }
            }
        }
    }

    private static readonly XNamespace ChartXmlNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// Rewrites the &lt;c:f&gt; source-range formula(s) embedded inside a captured
    /// &lt;c:cat&gt;&lt;c:multiLvlStrRef&gt; raw-XML string (see
    /// <see cref="ChartModel.MultiLevelCategoryXml"/>), using the same
    /// <see cref="FormulaRewriter"/> path used for every other chart formula. Returns null
    /// (leave untouched) if the payload fails to parse or contains no &lt;c:f&gt;.
    /// </summary>
    private static string? RewriteMultiLevelCategoryXml(string rawXml, RewriteOperation op, string hostSheetName)
    {
        XElement element;
        try
        {
            element = XElement.Parse(rawXml);
        }
        catch
        {
            // Malformed captured payload; leave it untouched rather than lose it.
            return null;
        }

        bool anyChanged = false;
        foreach (var f in element.Descendants(ChartXmlNamespace + "f").ToList())
        {
            var formula = f.Value;
            if (string.IsNullOrEmpty(formula))
                continue;

            var rewritten = RewriteVerbatimFormula(formula, op, hostSheetName);
            if (rewritten is not null && rewritten != formula)
            {
                f.Value = rewritten;
                anyChanged = true;
            }
        }

        return anyChanged ? element.ToString(SaveOptions.DisableFormatting) : null;
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

            // R100: undo restores the pre-edit multi-level category <c:cat> raw XML.
            if (snap.MultiLevelCategoryXml is not null)
            {
                chart.MultiLevelCategoryXml.Clear();
                chart.MultiLevelCategoryXml.AddRange(snap.MultiLevelCategoryXml);
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

    /// <summary>
    /// R100: rewrites every chart on every sheet in the workbook whose verbatim series
    /// formulas (Val/Cat/Tx/BubbleSize), series-range data-label source formulas, or
    /// error-bar range formulas reference the table <paramref name="op"/> is renaming --
    /// the chart-formula counterpart of <see cref="RewriteRuleFormulas"/> for CF/DV rules,
    /// used by <c>RenameStructuredTableCommand</c> so a table rename fixes every chart in
    /// the workbook, not just cell formulas and CF/DV rules.
    /// <para>
    /// Deliberately does NOT reuse <see cref="RewriteChartVerbatimFormulas(Sheet, RewriteOperation, string)"/>
    /// / <see cref="RewriteVerbatimFormula"/>: those pre-split each formula on every
    /// unquoted top-level comma to support OOXML's parenthesised multi-area range union
    /// syntax (e.g. <c>(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)</c>), but a structured reference
    /// like <c>Table1[[#Headers],[Values]]</c> contains an unquoted comma INSIDE its own
    /// bracket pair -- that splitter would corrupt it into two bogus fragments. A table-name
    /// rename can never touch a multi-area union in the first place (unions are plain cell
    /// ranges, never structured references), so this instead runs each formula through
    /// <see cref="FormulaRewriter.Rewrite"/> whole, with no pre-splitting -- mirroring how
    /// <c>DuplicateSheetDrawingCloner.RewriteClonedChartTableReferences</c> hit and solved
    /// the exact same trap for the single-sheet duplicate-sheet path. Revert is handled by
    /// the existing <see cref="CaptureChartVerbatimFormulas(Workbook)"/> /
    /// <see cref="RestoreChartVerbatimFormulas(Workbook, List{ChartVerbatimWorkbookSnapshot})"/>
    /// pair (they snapshot/restore whole records, so BubbleSizeFormula and error bars are
    /// covered automatically).
    /// </para>
    /// </summary>
    internal static void RewriteAllChartFormulasForTableRename(Workbook workbook, RenameTableOp op)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var chart in sheet.Charts)
            {
                if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
                {
                    for (var i = 0; i < vf.Count; i++)
                    {
                        var entry = vf[i];
                        var newVal = RewriteWholeChartFormula(entry.ValFormula, op);
                        var newCat = RewriteWholeChartFormula(entry.CatFormula, op);
                        var newTx = RewriteWholeChartFormula(entry.TxFormula, op);
                        var newBubble = RewriteWholeChartFormula(entry.BubbleSizeFormula, op);
                        if (!string.Equals(newVal, entry.ValFormula, StringComparison.Ordinal) ||
                            !string.Equals(newCat, entry.CatFormula, StringComparison.Ordinal) ||
                            !string.Equals(newTx, entry.TxFormula, StringComparison.Ordinal) ||
                            !string.Equals(newBubble, entry.BubbleSizeFormula, StringComparison.Ordinal))
                        {
                            vf[i] = entry with
                            {
                                ValFormula = newVal,
                                CatFormula = newCat,
                                TxFormula = newTx,
                                BubbleSizeFormula = newBubble
                            };
                        }
                    }
                }

                if (chart.SeriesRangeDataLabels is { Count: > 0 } dl)
                {
                    for (var i = 0; i < dl.Count; i++)
                    {
                        var entry = dl[i];
                        var rewritten = RewriteWholeChartFormula(entry.Formula, op);
                        if (!string.Equals(rewritten, entry.Formula, StringComparison.Ordinal))
                            dl[i] = entry with { Formula = rewritten };
                    }
                }

                var newPlus = RewriteWholeChartFormula(chart.ErrorBarPlusRangeFormula, op);
                var newMinus = RewriteWholeChartFormula(chart.ErrorBarMinusRangeFormula, op);
                if (!string.Equals(newPlus, chart.ErrorBarPlusRangeFormula, StringComparison.Ordinal))
                    chart.ErrorBarPlusRangeFormula = newPlus;
                if (!string.Equals(newMinus, chart.ErrorBarMinusRangeFormula, StringComparison.Ordinal))
                    chart.ErrorBarMinusRangeFormula = newMinus;
            }
        }
    }

    /// <summary>
    /// Runs a single chart verbatim-formula string through <see cref="FormulaRewriter.Rewrite"/>
    /// whole (no comma pre-splitting -- see <see cref="RewriteAllChartFormulasForTableRename"/>).
    /// The host-sheet-name parameter ordinary structural rewrites need is irrelevant for a
    /// <see cref="RenameTableOp"/>: it matches purely by table name, with no
    /// sheet-qualification concept, so any non-null placeholder is safe to pass (mirrors
    /// <c>DuplicateSheetCommand.RewriteFormulaForTableRenames</c>).
    /// </summary>
    private static string? RewriteWholeChartFormula(string? formulaText, RenameTableOp op)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;

        return FormulaRewriter.Rewrite(formulaText, op, string.Empty) ?? formulaText;
    }
}
