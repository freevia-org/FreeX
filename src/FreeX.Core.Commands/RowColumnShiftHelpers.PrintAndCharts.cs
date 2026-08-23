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

    // ── Chart per-series formatting/override remap (R102: column insert/delete strictly inside a
    // chart's plotted range must not mis-attribute per-series formatting to the wrong series) ────
    // ChartModel.SeriesColumnMappings (shifted above) gives every series a COLUMN-INDEPENDENT
    // identity (the chart-XML <c:idx>, ChartSeriesColumnMapping.SeriesXmlIndex) whenever it is
    // populated -- SeriesFormats/PointFillColors/etc. already stay correctly attached in that case
    // because their SeriesIndex keys never needed to change.
    // But the common case (every freshly-created FreeX chart, and any Excel-authored chart with no
    // column gaps) leaves SeriesColumnMappings EMPTY, and both the live renderer
    // (ChartRenderer.SeriesFormatting.cs's GetSeriesIndex) and the XLSX writer
    // (XlsxChartXmlWriter.Series.cs's GetChartSeriesStripSequence) then derive a series' index
    // purely from its ORDINAL POSITION among the columns currently inside DataRange. Inserting or
    // deleting a column strictly inside that plotted span therefore creates/removes a series slot
    // in the middle and silently re-numbers every series after it -- exactly the class of bug
    // RemoveChartSeriesCommand (see its own precise per-list SeriesIndex remap) was hardened
    // against, but which a plain whole-column Insert/Delete never triggered.
    // Scoped like RemoveChartSeriesCommand: row-major charts (SeriesInRows) have no column-derived
    // series position to remap, and Bubble/Scatter's non-1-series-per-column layout is deliberately
    // out of scope (same exclusion RemoveChartSeriesCommand applies).

    /// <summary>Snapshot of a single chart's SeriesIndex-keyed formatting/override state for undo.</summary>
    internal sealed class ChartSeriesFormattingSnapshot
    {
        public required List<ChartSeriesOrderOverride> SeriesOrderOverrides { get; init; }
        public required List<ChartPointMarkerFormat> PointMarkerFormats { get; init; }
        public required List<int> SecondaryAxisSeriesIndexes { get; init; }
        public required List<int> ComboLineSeriesIndexes { get; init; }
        public required List<int> ComboScatterSeriesIndexes { get; init; }
        public required int TrendlineSeriesIndex { get; init; }
        public required int ErrorBarSeriesIndex { get; init; }
        public required bool ShowLinearTrendline { get; init; }
        public required bool ShowErrorBars { get; init; }
        public required List<ChartSeriesFormat> SeriesFormats { get; init; }
        public required List<ChartPointFillFormat> PointFillColors { get; init; }
        public required List<ChartSeriesDataLabelFormat> SeriesDataLabelFormats { get; init; }
        public required List<ChartPointDataLabelFormat> PointDataLabelFormats { get; init; }
        public required List<int> SeriesPlotOrder { get; init; }
        public required List<ChartLegendEntryModel> LegendEntries { get; init; }
        // R102: both the row-axis remap (ShiftChartSeriesFormattingRowsUp/Down) and the column-axis
        // remap (ShiftChartSeriesFormattingColumnsUp/Down) touch these -- captured/restored here so
        // BOTH axes' undo covers the full SeriesIndex-keyed set.
        public required List<ChartSeriesRawXmlEntry> MultiLevelCategoryXml { get; init; }
        public required List<ChartPointExplosion> ExplodedSlices { get; init; }
        public required List<ChartRangeDataLabel> RangeDataLabels { get; init; }
        public required List<ChartSeriesRangeDataLabels> SeriesRangeDataLabels { get; init; }
        public required List<ChartSeriesRawXmlEntry> AdditionalSeriesErrorBarsXml { get; init; }
        public required List<ChartSeriesRawXmlEntry> AdditionalSeriesTrendlinesXml { get; init; }
    }

    /// <summary>Workbook-wide snapshot of <see cref="ChartSeriesFormattingSnapshot"/>s, keyed by hosting sheet.</summary>
    internal sealed class ChartSeriesFormattingWorkbookSnapshot
    {
        public required SheetId HostSheet { get; init; }
        public required List<ChartSeriesFormattingSnapshot> Charts { get; init; }
    }

    internal static List<ChartSeriesFormattingWorkbookSnapshot> CaptureChartSeriesFormatting(Workbook workbook)
    {
        var result = new List<ChartSeriesFormattingWorkbookSnapshot>(workbook.Sheets.Count);
        foreach (var s in workbook.Sheets)
        {
            if (s.Charts.Count == 0) continue;
            result.Add(new ChartSeriesFormattingWorkbookSnapshot
            {
                HostSheet = s.Id,
                Charts = s.Charts.Select(c => new ChartSeriesFormattingSnapshot
                {
                    SeriesOrderOverrides       = new List<ChartSeriesOrderOverride>(c.SeriesOrderOverrides),
                    PointMarkerFormats         = new List<ChartPointMarkerFormat>(c.PointMarkerFormats),
                    SecondaryAxisSeriesIndexes = new List<int>(c.SecondaryAxisSeriesIndexes),
                    ComboLineSeriesIndexes     = new List<int>(c.ComboLineSeriesIndexes),
                    ComboScatterSeriesIndexes  = new List<int>(c.ComboScatterSeriesIndexes),
                    TrendlineSeriesIndex       = c.TrendlineSeriesIndex,
                    ErrorBarSeriesIndex        = c.ErrorBarSeriesIndex,
                    ShowLinearTrendline        = c.ShowLinearTrendline,
                    ShowErrorBars              = c.ShowErrorBars,
                    SeriesFormats              = new List<ChartSeriesFormat>(c.SeriesFormats),
                    PointFillColors            = new List<ChartPointFillFormat>(c.PointFillColors),
                    SeriesDataLabelFormats     = new List<ChartSeriesDataLabelFormat>(c.SeriesDataLabelFormats),
                    PointDataLabelFormats      = new List<ChartPointDataLabelFormat>(c.PointDataLabelFormats),
                    SeriesPlotOrder            = new List<int>(c.SeriesPlotOrder),
                    LegendEntries              = new List<ChartLegendEntryModel>(c.LegendEntries),
                    MultiLevelCategoryXml         = new List<ChartSeriesRawXmlEntry>(c.MultiLevelCategoryXml),
                    ExplodedSlices                = new List<ChartPointExplosion>(c.ExplodedSlices),
                    RangeDataLabels               = new List<ChartRangeDataLabel>(c.RangeDataLabels),
                    SeriesRangeDataLabels         = new List<ChartSeriesRangeDataLabels>(c.SeriesRangeDataLabels),
                    AdditionalSeriesErrorBarsXml  = new List<ChartSeriesRawXmlEntry>(c.AdditionalSeriesErrorBarsXml),
                    AdditionalSeriesTrendlinesXml = new List<ChartSeriesRawXmlEntry>(c.AdditionalSeriesTrendlinesXml)
                }).ToList()
            });
        }
        return result;
    }

    internal static void RestoreChartSeriesFormatting(Workbook workbook, List<ChartSeriesFormattingWorkbookSnapshot>? snapshot)
    {
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            var sheet = workbook.GetSheet(entry.HostSheet);
            if (sheet is null) continue;
            for (int i = 0; i < sheet.Charts.Count && i < entry.Charts.Count; i++)
            {
                var chart = sheet.Charts[i];
                var snap = entry.Charts[i];
                chart.SeriesOrderOverrides       = snap.SeriesOrderOverrides;
                chart.PointMarkerFormats         = snap.PointMarkerFormats;
                chart.SecondaryAxisSeriesIndexes = snap.SecondaryAxisSeriesIndexes;
                chart.ComboLineSeriesIndexes     = snap.ComboLineSeriesIndexes;
                chart.ComboScatterSeriesIndexes  = snap.ComboScatterSeriesIndexes;
                chart.TrendlineSeriesIndex       = snap.TrendlineSeriesIndex;
                chart.ErrorBarSeriesIndex        = snap.ErrorBarSeriesIndex;
                chart.ShowLinearTrendline        = snap.ShowLinearTrendline;
                chart.ShowErrorBars              = snap.ShowErrorBars;
                chart.SeriesFormats              = snap.SeriesFormats;
                chart.PointFillColors            = snap.PointFillColors;
                chart.SeriesDataLabelFormats     = snap.SeriesDataLabelFormats;
                chart.PointDataLabelFormats      = snap.PointDataLabelFormats;
                chart.SeriesPlotOrder            = snap.SeriesPlotOrder;
                chart.LegendEntries              = snap.LegendEntries;
                chart.MultiLevelCategoryXml         = snap.MultiLevelCategoryXml;
                chart.ExplodedSlices                = snap.ExplodedSlices;
                chart.RangeDataLabels                = snap.RangeDataLabels;
                chart.SeriesRangeDataLabels          = snap.SeriesRangeDataLabels;
                chart.AdditionalSeriesErrorBarsXml   = snap.AdditionalSeriesErrorBarsXml;
                chart.AdditionalSeriesTrendlinesXml  = snap.AdditionalSeriesTrendlinesXml;
            }
        }
    }

    /// <summary>
    /// Column insert: remaps every SeriesIndex-keyed collection on every chart hosted anywhere in
    /// the workbook whose plotted DataRange lives on <paramref name="sheetId"/> and whose insertion
    /// point at <paramref name="start"/> falls STRICTLY INSIDE the chart's plotted data-column span
    /// (not merely before/after it) -- the case that creates a brand-new series slot in the middle
    /// of the existing series instead of uniformly shifting the whole plotted block.
    /// Must be called with each chart's DataRange still at its PRE-insert value (i.e. before
    /// <see cref="ShiftChartColumnsUp"/> has run for this same edit).
    /// </summary>
    internal static void ShiftChartSeriesFormattingColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                RemapChartSeriesFormattingForColumnInsert(chart, sheetId, start, count);
                RemapChartPointFormattingForColumnInsert(chart, sheetId, start, count);
            }
    }

    private static void RemapChartSeriesFormattingForColumnInsert(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (chart.SeriesColumnMappings.Count > 0) return; // authoritative mapping already keeps SeriesIndex column-independent
        if (chart.SeriesInRows || chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startCol = chart.DataRange.Start.Col;
        var endCol = chart.DataRange.End.Col;
        var dataStartCol = chart.FirstColIsCategories && endCol > startCol ? startCol + 1 : startCol;
        if (dataStartCol > endCol) return; // no plotted series at all

        if (start <= startCol || start > endCol) return; // lands at/before the whole range or strictly after it -- not interior

        var boundary = (int)(start - dataStartCol);
        var delta = (int)count;

        chart.SeriesOrderOverrides = chart.SeriesOrderOverrides
            .Select(o => o.SeriesIndex >= boundary ? o with { SeriesIndex = o.SeriesIndex + delta } : o).ToList();
        chart.PointMarkerFormats = chart.PointMarkerFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.SeriesFormats = chart.SeriesFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.PointFillColors = chart.PointFillColors
            .Select(p => p.SeriesIndex >= boundary ? p with { SeriesIndex = p.SeriesIndex + delta } : p).ToList();
        chart.SeriesDataLabelFormats = chart.SeriesDataLabelFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.ComboScatterSeriesIndexes = chart.ComboScatterSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.MultiLevelCategoryXml = chart.MultiLevelCategoryXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();
        chart.ExplodedSlices = chart.ExplodedSlices
            .Select(s => s.SeriesIndex >= boundary ? s with { SeriesIndex = s.SeriesIndex + delta } : s).ToList();
        chart.RangeDataLabels = chart.RangeDataLabels
            .Select(l => l.SeriesIndex >= boundary ? l with { SeriesIndex = l.SeriesIndex + delta } : l).ToList();
        chart.SeriesRangeDataLabels = chart.SeriesRangeDataLabels
            .Select(l => l.SeriesIndex >= boundary ? l with { SeriesIndex = l.SeriesIndex + delta } : l).ToList();
        chart.AdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();
        chart.AdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();

        if (chart.TrendlineSeriesIndex >= boundary)
            chart.TrendlineSeriesIndex += delta;
        if (chart.ErrorBarSeriesIndex >= boundary)
            chart.ErrorBarSeriesIndex += delta;

        if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
        {
            for (var i = 0; i < vf.Count; i++)
            {
                var entry = vf[i];
                if (entry.SeriesIndex >= boundary)
                    vf[i] = entry with { SeriesIndex = entry.SeriesIndex + delta };
            }
        }

        if (chart.SeriesPlotOrder.Count == 0)
        {
            // Legacy case: declaration order equals idx order, so a LegendEntry's Index IS the
            // series idx directly (mirrors RemoveChartSeriesCommand.RemapPlotOrderAndLegendEntries).
            chart.LegendEntries = chart.LegendEntries
                .Select(e => e.Index >= boundary ? e with { Index = e.Index + delta } : e).ToList();
        }
        else
        {
            // SeriesPlotOrder is a list of series-index VALUES in declaration order; shift each
            // value exactly like the scalar indexes above. LegendEntries.Index is a legend-POSITION
            // (an index into this list), not a series index -- inserting a column only ADDS a new
            // series slot (which starts with no legend entry of its own); it never changes any
            // EXISTING series' already-declared position, so LegendEntries itself needs no remap.
            chart.SeriesPlotOrder = chart.SeriesPlotOrder
                .Select(i => i >= boundary ? i + delta : i).ToList();
        }
    }

    /// <summary>Column delete counterpart of <see cref="ShiftChartSeriesFormattingColumnsUp"/>. Any
    /// series whose plotted column falls entirely inside the deleted band has its formatting/
    /// override entries DROPPED (the series itself no longer exists); every surviving series after
    /// the deleted band has its SeriesIndex shifted down. Must be called with each chart's DataRange
    /// still at its PRE-delete value (i.e. before <see cref="ShiftChartColumnsDown"/> has run).</summary>
    internal static void ShiftChartSeriesFormattingColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                RemapChartSeriesFormattingForColumnDelete(chart, sheetId, start, count);
                RemapChartPointFormattingForColumnDelete(chart, sheetId, start, count);
            }
    }

    private static void RemapChartSeriesFormattingForColumnDelete(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (chart.SeriesColumnMappings.Count > 0) return;
        if (chart.SeriesInRows || chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startCol = chart.DataRange.Start.Col;
        var endCol = chart.DataRange.End.Col;
        var dataStartCol = chart.FirstColIsCategories && endCol > startCol ? startCol + 1 : startCol;
        if (dataStartCol > endCol) return;

        var deleteStart = start;
        var deleteEnd = start + count - 1;
        var overlapStart = Math.Max(deleteStart, dataStartCol);
        var overlapEnd = Math.Min(deleteEnd, endCol);
        if (overlapStart > overlapEnd) return; // deletion never touches a plotted series column

        var posLo = (int)(overlapStart - dataStartCol);
        var posHi = (int)(overlapEnd - dataStartCol);
        var removedCount = posHi - posLo + 1;

        chart.SeriesOrderOverrides = RemapForColumnDelete(chart.SeriesOrderOverrides, o => o.SeriesIndex, (o, v) => o with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointMarkerFormats = RemapForColumnDelete(chart.PointMarkerFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesFormats = RemapForColumnDelete(chart.SeriesFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointFillColors = RemapForColumnDelete(chart.PointFillColors, p => p.SeriesIndex, (p, v) => p with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesDataLabelFormats = RemapForColumnDelete(chart.SeriesDataLabelFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointDataLabelFormats = RemapForColumnDelete(chart.PointDataLabelFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SecondaryAxisSeriesIndexes = RemapIndexListForColumnDelete(chart.SecondaryAxisSeriesIndexes, posLo, posHi, removedCount);
        chart.ComboLineSeriesIndexes = RemapIndexListForColumnDelete(chart.ComboLineSeriesIndexes, posLo, posHi, removedCount);
        chart.ComboScatterSeriesIndexes = RemapIndexListForColumnDelete(chart.ComboScatterSeriesIndexes, posLo, posHi, removedCount);
        chart.MultiLevelCategoryXml = RemapForColumnDelete(chart.MultiLevelCategoryXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.ExplodedSlices = RemapForColumnDelete(chart.ExplodedSlices, s => s.SeriesIndex, (s, v) => s with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.RangeDataLabels = RemapForColumnDelete(chart.RangeDataLabels, l => l.SeriesIndex, (l, v) => l with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesRangeDataLabels = RemapForColumnDelete(chart.SeriesRangeDataLabels, l => l.SeriesIndex, (l, v) => l with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.AdditionalSeriesErrorBarsXml = RemapForColumnDelete(chart.AdditionalSeriesErrorBarsXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.AdditionalSeriesTrendlinesXml = RemapForColumnDelete(chart.AdditionalSeriesTrendlinesXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);

        if (chart.TrendlineSeriesIndex >= posLo && chart.TrendlineSeriesIndex <= posHi)
            chart.ShowLinearTrendline = false;
        else if (chart.TrendlineSeriesIndex > posHi)
            chart.TrendlineSeriesIndex -= removedCount;

        if (chart.ErrorBarSeriesIndex >= posLo && chart.ErrorBarSeriesIndex <= posHi)
            chart.ShowErrorBars = false;
        else if (chart.ErrorBarSeriesIndex > posHi)
            chart.ErrorBarSeriesIndex -= removedCount;

        if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
        {
            chart.VerbatimSeriesFormulas = vf
                .Where(v => v.SeriesIndex < posLo || v.SeriesIndex > posHi)
                .Select(v => v.SeriesIndex > posHi ? v with { SeriesIndex = v.SeriesIndex - removedCount } : v)
                .ToList();
        }

        if (chart.SeriesPlotOrder.Count == 0)
        {
            chart.LegendEntries = RemapForColumnDelete(chart.LegendEntries, e => e.Index, (e, v) => e with { Index = v }, posLo, posHi, removedCount);
        }
        else
        {
            // Removed series' declared positions must be dropped from SeriesPlotOrder too, and
            // every legend-POSITION reference in LegendEntries needs to track wherever its
            // series' declaration position ended up (or be dropped if that position itself was
            // removed) -- mirrors RemoveChartSeriesCommand.RemapPlotOrderAndLegendEntries,
            // generalized from "one removed series" to "a contiguous removed span".
            var oldPlotOrder = chart.SeriesPlotOrder;
            var removedPositions = new HashSet<int>();
            var positionRemap = new Dictionary<int, int>(oldPlotOrder.Count);
            var newPlotOrder = new List<int>(oldPlotOrder.Count);
            for (var oldPos = 0; oldPos < oldPlotOrder.Count; oldPos++)
            {
                var seriesIdx = oldPlotOrder[oldPos];
                if (seriesIdx >= posLo && seriesIdx <= posHi)
                {
                    removedPositions.Add(oldPos);
                    continue;
                }
                var newSeriesIdx = seriesIdx > posHi ? seriesIdx - removedCount : seriesIdx;
                positionRemap[oldPos] = newPlotOrder.Count;
                newPlotOrder.Add(newSeriesIdx);
            }
            chart.SeriesPlotOrder = newPlotOrder;
            chart.LegendEntries = chart.LegendEntries
                .Where(e => !removedPositions.Contains(e.Index))
                .Select(e => positionRemap.TryGetValue(e.Index, out var newPos) ? e with { Index = newPos } : e)
                .ToList();
        }
    }

    private static List<T> RemapForColumnDelete<T>(
        List<T> items, Func<T, int> getIndex, Func<T, int, T> withIndex, int posLo, int posHi, int removedCount)
    {
        var result = new List<T>(items.Count);
        foreach (var item in items)
        {
            var idx = getIndex(item);
            if (idx >= posLo && idx <= posHi)
                continue; // this series was removed
            result.Add(idx > posHi ? withIndex(item, idx - removedCount) : item);
        }
        return result;
    }

    private static List<int> RemapIndexListForColumnDelete(List<int> indexes, int posLo, int posHi, int removedCount) =>
        indexes
            .Where(i => i < posLo || i > posHi)
            .Select(i => i > posHi ? i - removedCount : i)
            .ToList();

    /// <summary>
    /// Row insert: the ROW-axis twin of <see cref="ShiftChartSeriesFormattingColumnsUp"/>, for a
    /// Excel "Switch Row/Column" chart (<see cref="ChartModel.SeriesInRows"/> == true), whose ROWS
    /// -- not columns -- are the plotted series. Remaps every SeriesIndex-keyed collection on every
    /// chart hosted anywhere in the workbook whose plotted DataRange lives on <paramref name="sheetId"/>
    /// and whose insertion point at <paramref name="start"/> falls STRICTLY INSIDE the chart's
    /// plotted data-row span. Must be called with each chart's DataRange still at its PRE-insert
    /// value (i.e. before <see cref="ShiftChartRowsUp"/> has run for this same edit).
    /// <para>
    /// This enumerates the FULL set of SeriesIndex-keyed members on <see cref="ChartModel"/> --
    /// including <see cref="ChartModel.MultiLevelCategoryXml"/>, <see cref="ChartModel.ExplodedSlices"/>,
    /// <see cref="ChartModel.RangeDataLabels"/>, <see cref="ChartModel.SeriesRangeDataLabels"/>,
    /// <see cref="ChartModel.AdditionalSeriesErrorBarsXml"/> and
    /// <see cref="ChartModel.AdditionalSeriesTrendlinesXml"/>, which <see cref="RemoveChartSeriesCommand"/>
    /// already treats as SeriesIndex-keyed (see its per-series remap). The sibling column-insert/
    /// delete remap (<see cref="RemapChartSeriesFormattingForColumnInsert"/> /
    /// <see cref="RemapChartSeriesFormattingForColumnDelete"/>) now covers this same full set too
    /// (R102 follow-up) -- both axes are kept in lockstep against RemoveChartSeriesCommand's
    /// authoritative enumeration rather than against each other. <see cref="ChartModel.SeriesColumnMappings"/> is
    /// column-based and documented as ignored while <see cref="ChartModel.SeriesInRows"/> is set, so
    /// it needs no guard or remap here. <see cref="ChartModel.EmbeddedSeriesData"/> is deliberately
    /// left untouched (see the comment above <see cref="ShiftChartColumnsUp"/>'s verbatim-formula
    /// section) -- it is opaque cached numCache/strCache data with no reference string, and every
    /// other command in this codebase (including RemoveChartSeriesCommand) leaves it alone too.
    /// <see cref="ChartModel.AdditionalPlotGroupDataLabels"/> is keyed by plot-GROUP index (a combo
    /// chart's declared type sub-groups), not by series identity, so a series-count change does not
    /// predictably move it either -- left untouched, matching the column-axis precedent.
    /// </para>
    /// </summary>
    internal static void ShiftChartSeriesFormattingRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                RemapChartSeriesFormattingForRowInsert(chart, sheetId, start, count);
                RemapChartPointFormattingForRowInsert(chart, sheetId, start, count);
            }
    }

    private static void RemapChartSeriesFormattingForRowInsert(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (!chart.SeriesInRows) return; // rows are only the series axis for a Switch-Row/Column chart
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startRow = chart.DataRange.Start.Row;
        var endRow = chart.DataRange.End.Row;
        // ChartModel.SeriesInRows's doc comment: with the flag set, series names come from the
        // first COLUMN and category labels from the first ROW -- i.e. FirstColIsCategories (not
        // FirstRowIsHeader) is the flag that ends up gating the first ROW once ChartRenderer.
        // BuildPlotModel transposes the cell lookup (TransposeChartCellLookup keeps the DataRange
        // corner anchored, so FirstColIsCategories's post-transpose column-skip lands on the
        // original ROW axis). This mirrors the column function's dataStartCol computation exactly,
        // just off the orthogonal axis and flag.
        var dataStartRow = chart.FirstColIsCategories && endRow > startRow ? startRow + 1 : startRow;
        if (dataStartRow > endRow) return; // no plotted series at all

        if (start <= startRow || start > endRow) return; // lands at/before the whole range or strictly after it -- not interior

        var boundary = (int)(start - dataStartRow);
        var delta = (int)count;

        chart.SeriesOrderOverrides = chart.SeriesOrderOverrides
            .Select(o => o.SeriesIndex >= boundary ? o with { SeriesIndex = o.SeriesIndex + delta } : o).ToList();
        chart.PointMarkerFormats = chart.PointMarkerFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.SeriesFormats = chart.SeriesFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.PointFillColors = chart.PointFillColors
            .Select(p => p.SeriesIndex >= boundary ? p with { SeriesIndex = p.SeriesIndex + delta } : p).ToList();
        chart.SeriesDataLabelFormats = chart.SeriesDataLabelFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Select(f => f.SeriesIndex >= boundary ? f with { SeriesIndex = f.SeriesIndex + delta } : f).ToList();
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.ComboScatterSeriesIndexes = chart.ComboScatterSeriesIndexes
            .Select(i => i >= boundary ? i + delta : i).ToList();
        chart.MultiLevelCategoryXml = chart.MultiLevelCategoryXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();
        chart.ExplodedSlices = chart.ExplodedSlices
            .Select(s => s.SeriesIndex >= boundary ? s with { SeriesIndex = s.SeriesIndex + delta } : s).ToList();
        chart.RangeDataLabels = chart.RangeDataLabels
            .Select(l => l.SeriesIndex >= boundary ? l with { SeriesIndex = l.SeriesIndex + delta } : l).ToList();
        chart.SeriesRangeDataLabels = chart.SeriesRangeDataLabels
            .Select(l => l.SeriesIndex >= boundary ? l with { SeriesIndex = l.SeriesIndex + delta } : l).ToList();
        chart.AdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();
        chart.AdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml
            .Select(x => x.SeriesIndex >= boundary ? x with { SeriesIndex = x.SeriesIndex + delta } : x).ToList();

        if (chart.TrendlineSeriesIndex >= boundary)
            chart.TrendlineSeriesIndex += delta;
        if (chart.ErrorBarSeriesIndex >= boundary)
            chart.ErrorBarSeriesIndex += delta;

        if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
        {
            for (var i = 0; i < vf.Count; i++)
            {
                var entry = vf[i];
                if (entry.SeriesIndex >= boundary)
                    vf[i] = entry with { SeriesIndex = entry.SeriesIndex + delta };
            }
        }

        if (chart.SeriesPlotOrder.Count == 0)
        {
            // Legacy case: declaration order equals idx order, so a LegendEntry's Index IS the
            // series idx directly (mirrors RemoveChartSeriesCommand.RemapPlotOrderAndLegendEntries).
            chart.LegendEntries = chart.LegendEntries
                .Select(e => e.Index >= boundary ? e with { Index = e.Index + delta } : e).ToList();
        }
        else
        {
            // SeriesPlotOrder is a list of series-index VALUES in declaration order; shift each
            // value exactly like the scalar indexes above. LegendEntries.Index is a legend-POSITION
            // (an index into this list), not a series index -- inserting a row only ADDS a new
            // series slot (which starts with no legend entry of its own); it never changes any
            // EXISTING series' already-declared position, so LegendEntries itself needs no remap.
            chart.SeriesPlotOrder = chart.SeriesPlotOrder
                .Select(i => i >= boundary ? i + delta : i).ToList();
        }
    }

    /// <summary>Row delete counterpart of <see cref="ShiftChartSeriesFormattingRowsUp"/>. Any series
    /// whose plotted row falls entirely inside the deleted band has its formatting/override entries
    /// DROPPED (the series itself no longer exists); every surviving series after the deleted band
    /// has its SeriesIndex shifted down. Must be called with each chart's DataRange still at its
    /// PRE-delete value (i.e. before <see cref="ShiftChartRowsDown"/> has run). See
    /// <see cref="ShiftChartSeriesFormattingRowsUp"/> for the full enumerated set of SeriesIndex-keyed
    /// collections this covers (including the ones the sibling column-delete remap does not yet).</summary>
    internal static void ShiftChartSeriesFormattingRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var s in workbook.Sheets)
            foreach (var chart in s.Charts)
            {
                RemapChartSeriesFormattingForRowDelete(chart, sheetId, start, count);
                RemapChartPointFormattingForRowDelete(chart, sheetId, start, count);
            }
    }

    private static void RemapChartSeriesFormattingForRowDelete(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (!chart.SeriesInRows) return;
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startRow = chart.DataRange.Start.Row;
        var endRow = chart.DataRange.End.Row;
        var dataStartRow = chart.FirstColIsCategories && endRow > startRow ? startRow + 1 : startRow;
        if (dataStartRow > endRow) return;

        var deleteStart = start;
        var deleteEnd = start + count - 1;
        var overlapStart = Math.Max(deleteStart, dataStartRow);
        var overlapEnd = Math.Min(deleteEnd, endRow);
        if (overlapStart > overlapEnd) return; // deletion never touches a plotted series row

        var posLo = (int)(overlapStart - dataStartRow);
        var posHi = (int)(overlapEnd - dataStartRow);
        var removedCount = posHi - posLo + 1;

        chart.SeriesOrderOverrides = RemapForColumnDelete(chart.SeriesOrderOverrides, o => o.SeriesIndex, (o, v) => o with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointMarkerFormats = RemapForColumnDelete(chart.PointMarkerFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesFormats = RemapForColumnDelete(chart.SeriesFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointFillColors = RemapForColumnDelete(chart.PointFillColors, p => p.SeriesIndex, (p, v) => p with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesDataLabelFormats = RemapForColumnDelete(chart.SeriesDataLabelFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.PointDataLabelFormats = RemapForColumnDelete(chart.PointDataLabelFormats, f => f.SeriesIndex, (f, v) => f with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SecondaryAxisSeriesIndexes = RemapIndexListForColumnDelete(chart.SecondaryAxisSeriesIndexes, posLo, posHi, removedCount);
        chart.ComboLineSeriesIndexes = RemapIndexListForColumnDelete(chart.ComboLineSeriesIndexes, posLo, posHi, removedCount);
        chart.ComboScatterSeriesIndexes = RemapIndexListForColumnDelete(chart.ComboScatterSeriesIndexes, posLo, posHi, removedCount);
        chart.MultiLevelCategoryXml = RemapForColumnDelete(chart.MultiLevelCategoryXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.ExplodedSlices = RemapForColumnDelete(chart.ExplodedSlices, s => s.SeriesIndex, (s, v) => s with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.RangeDataLabels = RemapForColumnDelete(chart.RangeDataLabels, l => l.SeriesIndex, (l, v) => l with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.SeriesRangeDataLabels = RemapForColumnDelete(chart.SeriesRangeDataLabels, l => l.SeriesIndex, (l, v) => l with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.AdditionalSeriesErrorBarsXml = RemapForColumnDelete(chart.AdditionalSeriesErrorBarsXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);
        chart.AdditionalSeriesTrendlinesXml = RemapForColumnDelete(chart.AdditionalSeriesTrendlinesXml, x => x.SeriesIndex, (x, v) => x with { SeriesIndex = v }, posLo, posHi, removedCount);

        if (chart.TrendlineSeriesIndex >= posLo && chart.TrendlineSeriesIndex <= posHi)
            chart.ShowLinearTrendline = false;
        else if (chart.TrendlineSeriesIndex > posHi)
            chart.TrendlineSeriesIndex -= removedCount;

        if (chart.ErrorBarSeriesIndex >= posLo && chart.ErrorBarSeriesIndex <= posHi)
            chart.ShowErrorBars = false;
        else if (chart.ErrorBarSeriesIndex > posHi)
            chart.ErrorBarSeriesIndex -= removedCount;

        if (chart.VerbatimSeriesFormulas is { Count: > 0 } vf)
        {
            chart.VerbatimSeriesFormulas = vf
                .Where(v => v.SeriesIndex < posLo || v.SeriesIndex > posHi)
                .Select(v => v.SeriesIndex > posHi ? v with { SeriesIndex = v.SeriesIndex - removedCount } : v)
                .ToList();
        }

        if (chart.SeriesPlotOrder.Count == 0)
        {
            chart.LegendEntries = RemapForColumnDelete(chart.LegendEntries, e => e.Index, (e, v) => e with { Index = v }, posLo, posHi, removedCount);
        }
        else
        {
            // Removed series' declared positions must be dropped from SeriesPlotOrder too, and
            // every legend-POSITION reference in LegendEntries needs to track wherever its
            // series' declaration position ended up (or be dropped if that position itself was
            // removed) -- mirrors RemoveChartSeriesCommand.RemapPlotOrderAndLegendEntries,
            // generalized from "one removed series" to "a contiguous removed span".
            var oldPlotOrder = chart.SeriesPlotOrder;
            var removedPositions = new HashSet<int>();
            var positionRemap = new Dictionary<int, int>(oldPlotOrder.Count);
            var newPlotOrder = new List<int>(oldPlotOrder.Count);
            for (var oldPos = 0; oldPos < oldPlotOrder.Count; oldPos++)
            {
                var seriesIdx = oldPlotOrder[oldPos];
                if (seriesIdx >= posLo && seriesIdx <= posHi)
                {
                    removedPositions.Add(oldPos);
                    continue;
                }
                var newSeriesIdx = seriesIdx > posHi ? seriesIdx - removedCount : seriesIdx;
                positionRemap[oldPos] = newPlotOrder.Count;
                newPlotOrder.Add(newSeriesIdx);
            }
            chart.SeriesPlotOrder = newPlotOrder;
            chart.LegendEntries = chart.LegendEntries
                .Where(e => !removedPositions.Contains(e.Index))
                .Select(e => positionRemap.TryGetValue(e.Index, out var newPos) ? e with { Index = newPos } : e)
                .ToList();
        }
    }

    // ── Per-POINT (PointIndex) chart formatting shifting ───────────────────────
    // The functions above only ever touch SeriesIndex -- they remap the axis that carries
    // *series* identity (columns by default, rows when SeriesInRows is set). PointIndex is
    // the point's 0-based position along the ORTHOGONAL (category) axis: literally
    // `row - dataStartRow` in the default orientation (see ChartRenderer.cs's
    // `var row = dataStartRow + (uint)pointIndex;`) or `col - dataStartCol` once SeriesInRows
    // transposes the plotted grid. A row insert/delete on a default (SeriesInRows == false)
    // chart therefore edits the POINT axis, not the series axis -- and vice versa for a
    // Switch-Row/Column (SeriesInRows == true) chart, where a COLUMN insert/delete is the point-
    // axis edit. Without this, ChartPointFillFormat/ChartPointMarkerFormat/
    // ChartPointDataLabelFormat/ChartPointExplosion/ChartRangeDataLabel/
    // ChartSeriesRangeDataLabels.Points stay pinned to their old numeric PointIndex and silently
    // reattach to the wrong data point after the shift.
    private static void RemapChartPointIndexedCollectionsForInsert(ChartModel chart, int boundary, int delta)
    {
        chart.PointFillColors = chart.PointFillColors
            .Select(p => p.PointIndex >= boundary ? p with { PointIndex = p.PointIndex + delta } : p).ToList();
        chart.PointMarkerFormats = chart.PointMarkerFormats
            .Select(f => f.PointIndex >= boundary ? f with { PointIndex = f.PointIndex + delta } : f).ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Select(f => f.PointIndex >= boundary ? f with { PointIndex = f.PointIndex + delta } : f).ToList();
        chart.ExplodedSlices = chart.ExplodedSlices
            .Select(s => s.PointIndex >= boundary ? s with { PointIndex = s.PointIndex + delta } : s).ToList();
        chart.RangeDataLabels = chart.RangeDataLabels
            .Select(l => l.PointIndex >= boundary ? l with { PointIndex = l.PointIndex + delta } : l).ToList();
        chart.SeriesRangeDataLabels = chart.SeriesRangeDataLabels
            .Select(entry => entry with
            {
                PointCount = entry.PointCount is int pc ? pc + delta : entry.PointCount,
                Points = entry.Points
                    .Select(p => p.PointIndex >= boundary ? p with { PointIndex = p.PointIndex + delta } : p)
                    .ToList()
            })
            .ToList();
    }

    /// <summary>Delete counterpart of <see cref="RemapChartPointIndexedCollectionsForInsert"/>. Any
    /// point-level override whose PointIndex falls inside the deleted band [posLo, posHi] is DROPPED
    /// (that data point no longer exists); every surviving point after the deleted band has its
    /// PointIndex shifted down by removedCount.</summary>
    private static void RemapChartPointIndexedCollectionsForDelete(ChartModel chart, int posLo, int posHi, int removedCount)
    {
        chart.PointFillColors = RemapForColumnDelete(chart.PointFillColors, p => p.PointIndex, (p, v) => p with { PointIndex = v }, posLo, posHi, removedCount);
        chart.PointMarkerFormats = RemapForColumnDelete(chart.PointMarkerFormats, f => f.PointIndex, (f, v) => f with { PointIndex = v }, posLo, posHi, removedCount);
        chart.PointDataLabelFormats = RemapForColumnDelete(chart.PointDataLabelFormats, f => f.PointIndex, (f, v) => f with { PointIndex = v }, posLo, posHi, removedCount);
        chart.ExplodedSlices = RemapForColumnDelete(chart.ExplodedSlices, s => s.PointIndex, (s, v) => s with { PointIndex = v }, posLo, posHi, removedCount);
        chart.RangeDataLabels = RemapForColumnDelete(chart.RangeDataLabels, l => l.PointIndex, (l, v) => l with { PointIndex = v }, posLo, posHi, removedCount);
        chart.SeriesRangeDataLabels = chart.SeriesRangeDataLabels
            .Select(entry => entry with
            {
                PointCount = entry.PointCount is int pc ? Math.Max(0, pc - removedCount) : entry.PointCount,
                Points = RemapForColumnDelete(entry.Points.ToList(), p => p.PointIndex, (p, v) => p with { PointIndex = v }, posLo, posHi, removedCount)
            })
            .ToList();
    }

    /// <summary>
    /// Row insert on a DEFAULT-orientation chart (SeriesInRows == false): rows are the POINT axis,
    /// so a row inserted strictly inside the plotted point span shifts every point-level override at
    /// or after it. Mirrors <see cref="RemapChartSeriesFormattingForColumnInsert"/>'s dataStartCol
    /// computation but off the row axis and gated on FirstRowIsHeader (the flag that gates the
    /// category-header ROW in the un-transposed, default orientation -- see ChartRenderer.cs:116).
    /// </summary>
    private static void RemapChartPointFormattingForRowInsert(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (chart.SeriesInRows) return; // rows are the point axis only for the default orientation
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startRow = chart.DataRange.Start.Row;
        var endRow = chart.DataRange.End.Row;
        var dataStartRow = chart.FirstRowIsHeader && endRow > startRow ? startRow + 1 : startRow;
        if (dataStartRow > endRow) return; // no plotted points at all

        if (start <= startRow || start > endRow) return; // not interior to the plotted point span

        var boundary = (int)(start - dataStartRow);
        RemapChartPointIndexedCollectionsForInsert(chart, boundary, (int)count);
    }

    /// <summary>Row delete counterpart of <see cref="RemapChartPointFormattingForRowInsert"/>.</summary>
    private static void RemapChartPointFormattingForRowDelete(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (chart.SeriesInRows) return;
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startRow = chart.DataRange.Start.Row;
        var endRow = chart.DataRange.End.Row;
        var dataStartRow = chart.FirstRowIsHeader && endRow > startRow ? startRow + 1 : startRow;
        if (dataStartRow > endRow) return;

        var deleteStart = start;
        var deleteEnd = start + count - 1;
        var overlapStart = Math.Max(deleteStart, dataStartRow);
        var overlapEnd = Math.Min(deleteEnd, endRow);
        if (overlapStart > overlapEnd) return; // deletion never touches a plotted point row

        var posLo = (int)(overlapStart - dataStartRow);
        var posHi = (int)(overlapEnd - dataStartRow);
        RemapChartPointIndexedCollectionsForDelete(chart, posLo, posHi, posHi - posLo + 1);
    }

    /// <summary>
    /// Column insert on a Switch-Row/Column chart (SeriesInRows == true): once
    /// <see cref="ChartRenderer"/> transposes the plotted grid (TransposeCoordinate keeps the
    /// DataRange corner anchored and swaps row/column offsets), columns become the POINT axis and
    /// FirstRowIsHeader -- not FirstColIsCategories -- is the flag that ends up gating the first
    /// COLUMN (symmetric to how <see cref="RemapChartSeriesFormattingForRowInsert"/> documents
    /// FirstColIsCategories gating the first ROW for that function's series axis). Mirrors
    /// <see cref="RemapChartPointFormattingForRowInsert"/> off the orthogonal axis.
    /// </summary>
    private static void RemapChartPointFormattingForColumnInsert(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (!chart.SeriesInRows) return; // columns are the point axis only for a Switch-Row/Column chart
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startCol = chart.DataRange.Start.Col;
        var endCol = chart.DataRange.End.Col;
        var dataStartCol = chart.FirstRowIsHeader && endCol > startCol ? startCol + 1 : startCol;
        if (dataStartCol > endCol) return;

        if (start <= startCol || start > endCol) return;

        var boundary = (int)(start - dataStartCol);
        RemapChartPointIndexedCollectionsForInsert(chart, boundary, (int)count);
    }

    /// <summary>Column delete counterpart of <see cref="RemapChartPointFormattingForColumnInsert"/>.</summary>
    private static void RemapChartPointFormattingForColumnDelete(ChartModel chart, SheetId sheetId, uint start, uint count)
    {
        if (chart.DataRange.Start.Sheet != sheetId) return;
        if (!chart.SeriesInRows) return;
        if (chart.Type is ChartType.Bubble or ChartType.Scatter) return;

        var startCol = chart.DataRange.Start.Col;
        var endCol = chart.DataRange.End.Col;
        var dataStartCol = chart.FirstRowIsHeader && endCol > startCol ? startCol + 1 : startCol;
        if (dataStartCol > endCol) return;

        var deleteStart = start;
        var deleteEnd = start + count - 1;
        var overlapStart = Math.Max(deleteStart, dataStartCol);
        var overlapEnd = Math.Min(deleteEnd, endCol);
        if (overlapStart > overlapEnd) return; // deletion never touches a plotted point column

        var posLo = (int)(overlapStart - dataStartCol);
        var posHi = (int)(overlapEnd - dataStartCol);
        RemapChartPointIndexedCollectionsForDelete(chart, posLo, posHi, posHi - posLo + 1);
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
                ChartFormulaFieldTransformer.Transform(
                    chart,
                    formula => RewriteWholeChartFormula(formula, op));
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
