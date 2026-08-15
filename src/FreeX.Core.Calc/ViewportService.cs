using System.Text;

using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc;

/// <summary>
/// Implementation of IViewportService that prepares data for the UI.
/// Handles coordinate mapping, sparse data retrieval, and conditional formatting.
/// </summary>
public sealed partial class ViewportService : IViewportService
{
    private const int MaxViewportListCapacityHint = 65_536;
    private const int MaxChartDataCellsPerViewport = 10_000;

    public (uint LastVisibleRow, IReadOnlyList<OutlineGroupRange> RowOutlineGroups)
        ComputeRowMetricsSummary(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet is null)
            return (0u, []);

        var rowMetrics = BuildFrozenAwareRowMetrics(
            sheet,
            request.TopRow,
            request.AvailableHeight,
            request.FrozenRowsOverride ?? sheet.FrozenRows);
        var lastVisibleRow = rowMetrics.Count > 0 ? rowMetrics[^1].Row : 0u;
        var rowOutlineGroups = sheet.ShowOutlineSymbols == false
            ? (IReadOnlyList<OutlineGroupRange>)[]
            : BuildRowOutlineGroups(sheet);
        return (lastVisibleRow, rowOutlineGroups);
    }

    public ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request)
    {
        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null)
        {
            return new ViewportModel([], [], [], null, [], ChartDataCells: [], DrawingObjects: []);
        }

        var effectiveFrozenRows = request.FrozenRowsOverride ?? sheet.FrozenRows;
        var effectiveFrozenCols = request.FrozenColsOverride ?? sheet.FrozenCols;
        var rowMetrics = BuildFrozenAwareRowMetrics(sheet, request.TopRow, request.AvailableHeight, effectiveFrozenRows);
        var colMetrics = BuildFrozenAwareColMetrics(sheet, request.LeftCol, request.AvailableWidth, effectiveFrozenCols);
        var hasAnyCellComments = HasAnyCellComments(sheet);
        // R136-io-worksheet-props-col-row-default-style: a sheet can have NO legacy per-cell
        // style-only entries at all (HasStyleOnlyCells false) yet still need GetStyleOnly consulted
        // for empty cells, because GetStyleOnly now also falls back to a whole-row/-column default
        // style (Sheet.RowStyles/ColumnStyles) -- without this, the viewport's fast-path guard below
        // would skip calling GetStyleOnly entirely for such a sheet, so a Currency-formatted empty
        // column would render as unformatted in the live grid despite resolving correctly everywhere
        // else (display formatters, cell-entry seeding, print/PDF layout).
        var hasAnyStyleOnlyCells = sheet.HasStyleOnlyCells || sheet.ColumnStyles.Count != 0 || sheet.RowStyles.Count != 0;

        // Pre-compute CF rule order and aggregates once per frame rather than per cell.
        var cfContext = BuildConditionalFormatContext(sheet, workbook);
        var hasConditionalStyles = HasConditionalStyleRules(cfContext);
        var hasConditionalIcons = cfContext.IconRulesByPriority.Count != 0;
        var hasConditionalDataBars = HasConditionalDataBarRules(cfContext);
        var styleCache = new ViewportStyleCache();
        var visibleCellSlots = EstimateVisibleCellSlots(rowMetrics.Count, colMetrics.Count);
        var scanOccupiedViewportCells = ShouldScanOccupiedViewportCells(
            visibleCellSlots,
            sheet,
            hasAnyCellComments,
            hasAnyStyleOnlyCells,
            hasConditionalStyles,
            cfContext,
            rowMetrics,
            colMetrics);
        var occupiedScanUsedRangeOverlapsViewport = scanOccupiedViewportCells &&
            UsedRangeOverlapsVisibleMetrics(sheet, rowMetrics, colMetrics);
        if (!scanOccupiedViewportCells || occupiedScanUsedRangeOverlapsViewport)
        {
            rowMetrics = MaterializeRowMetrics(rowMetrics);
            colMetrics = MaterializeColMetrics(colMetrics);
        }

        var cells = new List<DisplayCell>(
            scanOccupiedViewportCells
                ? occupiedScanUsedRangeOverlapsViewport
                    ? EstimateOccupiedScanDisplayCellCapacity(rowMetrics, colMetrics, sheet)
                    : 0
                : EstimateDisplayCellCapacity(
                    rowMetrics.Count,
                    colMetrics.Count,
                    sheet,
                    hasAnyCellComments,
                    hasAnyStyleOnlyCells));

        // Calculate Row Metrics — iterate until we've filled the available height, skipping hidden rows
        // Calculate Column Metrics — iterate until we've filled the available width
        // Retrieve Cells in Viewport
        if (scanOccupiedViewportCells)
        {
            if (occupiedScanUsedRangeOverlapsViewport)
            {
                AddOccupiedViewportCells(
                    cells,
                    workbook,
                    sheet,
                    sheetId,
                    rowMetrics,
                    colMetrics,
                    request.IncludeFormulas,
                    request.ShowFormulasOverride,
                    cfContext,
                    hasConditionalStyles,
                    hasConditionalIcons,
                    hasConditionalDataBars,
                    hasAnyCellComments,
                    ref styleCache);
            }
        }
        else
        {
            HashSet<(uint Row, uint Col)>? seen = null;
            foreach (var rowMetric in rowMetrics)
            {
                foreach (var colMetric in colMetrics)
                    AddDisplayCell(
                        cells,
                        ref seen,
                        false,
                        workbook,
                        sheet,
                        sheetId,
                        rowMetric.Row,
                        colMetric.Col,
                        EstimateCharacterWidth(GetMergeAwareTargetWidthPixels(sheet, sheetId, rowMetric.Row, colMetric.Col, colMetric.Width)),
                        request.IncludeFormulas,
                        request.ShowFormulasOverride,
                        cfContext,
                        hasAnyCellComments,
                        hasAnyStyleOnlyCells,
                        hasConditionalStyles,
                        hasConditionalIcons,
                        hasConditionalDataBars,
                        ref styleCache);
            }
        }

        var frozenPanes = (effectiveFrozenRows > 0 || effectiveFrozenCols > 0)
            ? new FrozenPaneState(effectiveFrozenRows, effectiveFrozenCols)
            : null;
        // A caller (WorkbookSession, one per open view) supplies its own effective split boundary
        // via request.SplitOverride so a Window > Split set in one view never leaks into another
        // sibling view's split bands; callers that don't track per-view split state (SplitOverride
        // left null) keep reading the shared Sheet.SplitRow/SplitColumn fields directly, exactly as
        // before.
        var effectiveSplitRow = request.SplitOverride is { } splitOverride ? splitOverride.SplitRow : sheet.SplitRow;
        var effectiveSplitCol = request.SplitOverride is { } colOverride ? colOverride.SplitCol : sheet.SplitColumn;
        var splitTopRows = effectiveSplitRow is { } splitRow
            ? BuildRowMetrics(sheet, 1, splitRow - 1, request.AvailableHeight)
            : [];
        var splitLeftColumns = effectiveSplitCol is { } splitColumn
            ? BuildColMetrics(sheet, 1, splitColumn - 1, request.AvailableWidth)
            : [];
        // Excel's split model gives the bottom row-band (bottom-left + bottom-right) and the right
        // column-band (top-right + bottom-right) exactly ONE shared scrollbar each -- the same
        // main VerticalScroll/HorizontalScroll that already drives rowMetrics/colMetrics for the
        // main (bottom-right) pane. TopRight and BottomLeft therefore always mirror the main
        // pane's current position; they have no independent scroll offset of their own (r56 fix
        // -- the previous SplitPaneOffsets.TopRightLeftCol/BottomLeftTopRow override let these two
        // panes desync permanently from the main pane, with no way to ever resync them).
        var topRightColumns = effectiveSplitCol.HasValue
            ? BuildColMetrics(sheet, request.LeftCol, CellAddress.MaxCol, request.AvailableWidth)
            : colMetrics;
        var bottomLeftRows = effectiveSplitRow.HasValue
            ? BuildRowMetrics(sheet, request.TopRow, CellAddress.MaxRow, request.AvailableHeight)
            : rowMetrics;
        var splitPanes = (effectiveSplitRow.HasValue || effectiveSplitCol.HasValue)
            ? new SplitPaneState(
                effectiveSplitRow,
                effectiveSplitCol,
                splitTopRows,
                splitLeftColumns,
                BuildSplitPaneCells(workbook, sheet, sheetId, splitTopRows, splitLeftColumns, bottomLeftRows, topRightColumns, request.IncludeFormulas, request.ShowFormulasOverride, cfContext, hasAnyCellComments, hasConditionalDataBars, ref styleCache),
                topRightColumns,
                bottomLeftRows)
            : null;

        var chartDataCells = request.IncludeObjects
            ? BuildChartDataCells(workbook, sheet, ref styleCache)
            : [];

        var drawingObjects = request.IncludeObjects
            ? BuildDrawingObjectBounds(sheet, workbook.Theme, rowMetrics, colMetrics)
            : [];
        var rowOutlineGroups = sheet.ShowOutlineSymbols == false
            ? []
            : BuildRowOutlineGroups(sheet);
        var columnOutlineGroups = sheet.ShowOutlineSymbols == false
            ? []
            : BuildColumnOutlineGroups(sheet);
        var borderFringe = BuildBorderFringe(workbook, sheet, rowMetrics, colMetrics, ref styleCache);

        return new ViewportModel(
            cells,
            rowMetrics,
            colMetrics,
            frozenPanes,
            [],
            splitPanes,
            chartDataCells,
            drawingObjects,
            rowOutlineGroups,
            columnOutlineGroups,
            borderFringe);
    }

    /// <summary>
    /// Collects borders authored on cells that sit just OUTSIDE the rendered viewport window
    /// (<paramref name="rowMetrics"/>/<paramref name="colMetrics"/>) but whose facing edge is
    /// shared with a still-visible boundary cell -- e.g. scrolling down by one row moves the
    /// cell carrying a BorderBottom off the top of the viewport, yet the seam it authors is still
    /// physically on-screen (the top edge of the new topmost visible row). The renderer's own
    /// shared-edge precedence lookup (GridView.Rendering.cs's borderStyleLookup) is built solely
    /// from <see cref="ViewportModel.Cells"/>, so without this it can never see that off-screen
    /// author and the line silently vanishes purely as a function of scroll position. Only the
    /// four true viewport edges are ever considered (an interior scroll boundary always has both
    /// neighboring rows/columns loaded already), and only when the off-screen neighbor isn't
    /// itself hidden (an adjacent hidden row/column's border is instead migrated onto the nearest
    /// visible neighbor by <see cref="ApplyHiddenNeighborBorderMigration"/>, so re-surfacing it
    /// here would double it up).
    /// </summary>
    private static IReadOnlyDictionary<(uint Row, uint Col), BorderFringeEdges>? BuildBorderFringe(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        ref ViewportStyleCache styleCache)
    {
        if (rowMetrics.Count == 0 || colMetrics.Count == 0)
            return null;

        Dictionary<(uint Row, uint Col), BorderFringeEdges>? fringe = null;

        var topRow = rowMetrics[0].Row;
        var bottomRow = rowMetrics[^1].Row;
        var leftCol = colMetrics[0].Col;
        var rightCol = colMetrics[^1].Col;

        if (topRow > 1 && !IsRowHidden(sheet, topRow - 1))
        {
            foreach (var colMetric in colMetrics)
            {
                if (GetRawCellStyleForBorderMigration(workbook, sheet, topRow - 1, colMetric.Col, ref styleCache) is { } aboveStyle &&
                    aboveStyle.BorderBottom.Style != BorderStyle.None)
                {
                    AddBorderFringeEdge(ref fringe, topRow, colMetric.Col, top: aboveStyle.BorderBottom);
                }
            }
        }

        if (bottomRow < CellAddress.MaxRow && !IsRowHidden(sheet, bottomRow + 1))
        {
            foreach (var colMetric in colMetrics)
            {
                if (GetRawCellStyleForBorderMigration(workbook, sheet, bottomRow + 1, colMetric.Col, ref styleCache) is { } belowStyle &&
                    belowStyle.BorderTop.Style != BorderStyle.None)
                {
                    AddBorderFringeEdge(ref fringe, bottomRow, colMetric.Col, bottom: belowStyle.BorderTop);
                }
            }
        }

        if (leftCol > 1 && !sheet.IsColEffectivelyHidden(leftCol - 1))
        {
            foreach (var rowMetric in rowMetrics)
            {
                if (GetRawCellStyleForBorderMigration(workbook, sheet, rowMetric.Row, leftCol - 1, ref styleCache) is { } leftStyle &&
                    leftStyle.BorderRight.Style != BorderStyle.None)
                {
                    AddBorderFringeEdge(ref fringe, rowMetric.Row, leftCol, left: leftStyle.BorderRight);
                }
            }
        }

        if (rightCol < CellAddress.MaxCol && !sheet.IsColEffectivelyHidden(rightCol + 1))
        {
            foreach (var rowMetric in rowMetrics)
            {
                if (GetRawCellStyleForBorderMigration(workbook, sheet, rowMetric.Row, rightCol + 1, ref styleCache) is { } rightStyle &&
                    rightStyle.BorderLeft.Style != BorderStyle.None)
                {
                    AddBorderFringeEdge(ref fringe, rowMetric.Row, rightCol, right: rightStyle.BorderLeft);
                }
            }
        }

        return fringe;
    }

    private static void AddBorderFringeEdge(
        ref Dictionary<(uint Row, uint Col), BorderFringeEdges>? fringe,
        uint row,
        uint col,
        CellBorder? top = null,
        CellBorder? bottom = null,
        CellBorder? left = null,
        CellBorder? right = null)
    {
        fringe ??= new Dictionary<(uint Row, uint Col), BorderFringeEdges>();
        var key = (row, col);
        var existing = fringe.TryGetValue(key, out var current) ? current : new BorderFringeEdges();
        fringe[key] = existing with
        {
            Top = top ?? existing.Top,
            Bottom = bottom ?? existing.Bottom,
            Left = left ?? existing.Left,
            Right = right ?? existing.Right,
        };
    }

    private static IReadOnlyList<RowMetric> MaterializeRowMetrics(IReadOnlyList<RowMetric> metrics)
    {
        if (metrics.Count == 0 || metrics is List<RowMetric>)
            return metrics;

        var materialized = new List<RowMetric>(metrics.Count);
        for (var i = 0; i < metrics.Count; i++)
            materialized.Add(metrics[i]);

        return materialized;
    }

    private static IReadOnlyList<ColMetric> MaterializeColMetrics(IReadOnlyList<ColMetric> metrics)
    {
        if (metrics.Count == 0 || metrics is List<ColMetric>)
            return metrics;

        var materialized = new List<ColMetric>(metrics.Count);
        for (var i = 0; i < metrics.Count; i++)
            materialized.Add(metrics[i]);

        return materialized;
    }

    private static int EstimateDisplayCellCapacity(
        int rowMetricCount,
        int colMetricCount,
        Sheet sheet,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells)
    {
        var visibleSlots = EstimateVisibleCellSlots(rowMetricCount, colMetricCount);
        if (visibleSlots == 0)
            return 0;

        if (hasAnyStyleOnlyCells)
            return ClampCapacityHint(visibleSlots);

        var possibleCells = sheet.CellCount;
        if (hasAnyCellComments)
            possibleCells = SaturatingAdd(possibleCells, sheet.Comments.Count + sheet.ThreadedComments.Count);

        return ClampCapacityHint(Math.Min(visibleSlots, possibleCells));
    }

    private static int EstimateVisibleCellSlots(int rowMetricCount, int colMetricCount)
    {
        if (rowMetricCount <= 0 || colMetricCount <= 0)
            return 0;

        var slots = (long)rowMetricCount * colMetricCount;
        return ClampCapacityHint(slots > int.MaxValue ? int.MaxValue : (int)slots);
    }

    private static int SaturatingAdd(int left, int right)
    {
        var result = (long)left + right;
        return result > int.MaxValue ? int.MaxValue : (int)result;
    }

    private static int ClampCapacityHint(int capacity) =>
        Math.Clamp(capacity, 0, MaxViewportListCapacityHint);

    private static bool ShouldScanOccupiedViewportCells(
        int visibleCellSlots,
        Sheet sheet,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells,
        bool hasConditionalStyles,
        CfEvaluationContext cfContext,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics) =>
        visibleCellSlots > 0 &&
        (long)sheet.CellCount * 4 < visibleCellSlots &&
        !hasAnyCellComments &&
        !hasAnyStyleOnlyCells &&
        // The occupied-cell scan never visits blank slots, so it cannot be used when a
        // style-producing conditional format could fill blank cells inside the viewport.
        (!hasConditionalStyles || !StyleRuleRangesOverlapVisibleMetrics(cfContext, rowMetrics, colMetrics));

    private static bool StyleRuleRangesOverlapVisibleMetrics(
        CfEvaluationContext cfContext,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics)
    {
        var ranges = cfContext.StyleRuleRanges;
        if (ranges.Count == 0 || rowMetrics.Count == 0 || colMetrics.Count == 0)
            return false;

        var firstRow = rowMetrics[0].Row;
        var lastRow = rowMetrics[^1].Row;
        var firstCol = colMetrics[0].Col;
        var lastCol = colMetrics[^1].Col;
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            if (RangesOverlap(range.Start.Row, range.End.Row, firstRow, lastRow) &&
                RangesOverlap(range.Start.Col, range.End.Col, firstCol, lastCol))
            {
                return true;
            }
        }

        return false;
    }

    private static int EstimateOccupiedScanDisplayCellCapacity(
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        Sheet sheet)
    {
        return ClampCapacityHint(Math.Min(
            EstimateVisibleCellSlots(rowMetrics.Count, colMetrics.Count),
            sheet.CellCount));
    }

    private static bool UsedRangeOverlapsVisibleMetrics(
        Sheet sheet,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics)
    {
        if (rowMetrics.Count == 0 ||
            colMetrics.Count == 0 ||
            sheet.GetUsedRange() is not { } usedRange)
        {
            return false;
        }

        return RangesOverlap(usedRange.Start.Row, usedRange.End.Row, rowMetrics[0].Row, rowMetrics[^1].Row) &&
            RangesOverlap(usedRange.Start.Col, usedRange.End.Col, colMetrics[0].Col, colMetrics[^1].Col);
    }

    private static bool RangesOverlap(uint firstStart, uint firstEnd, uint secondStart, uint secondEnd) =>
        firstStart <= secondEnd && secondStart <= firstEnd;

    private static void AddOccupiedViewportCells(
        List<DisplayCell> cells,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        IReadOnlyList<RowMetric> rowMetrics,
        IReadOnlyList<ColMetric> colMetrics,
        bool includeFormulas,
        bool? showFormulasOverride,
        CfEvaluationContext cfContext,
        bool hasConditionalStyles,
        bool hasConditionalIcons,
        bool hasConditionalDataBars,
        bool hasAnyCellComments,
        ref ViewportStyleCache styleCache)
    {
        foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
        {
            if (!IsWithinVisibleColumnRange(colMetrics, col) ||
                !IsWithinVisibleRowRange(rowMetrics, row) ||
                !TryGetVisibleColumnTargetWidth(sheet, sheetId, row, colMetrics, col, out var targetWidthCharacters))
            {
                continue;
            }

            if (!IsVisibleMetricRow(rowMetrics, row))
                continue;

            // A row/column present in the metrics list even though it is hidden is only there
            // because it anchors a merge with a visible remainder (see
            // IsHiddenMergeAnchorRowWithVisibleRemainder/...ColWithVisibleRemainder). The merge's
            // value/style live solely on its anchor cell, so any OTHER cell in that hidden row/col
            // must not leak into the viewport.
            var rowHidden = IsRowHidden(sheet, row);
            var colHidden = sheet.IsColEffectivelyHidden(col);
            if ((rowHidden || colHidden) &&
                !IsExposedHiddenMergeAnchorCell(sheet, row, col, rowHidden, colHidden))
            {
                continue;
            }

            AddCellDisplayCell(
                cells,
                workbook,
                sheet,
                sheetId,
                row,
                col,
                cell,
                targetWidthCharacters,
                includeFormulas,
                showFormulasOverride,
                cfContext,
                hasConditionalStyles,
                hasConditionalIcons,
                hasConditionalDataBars,
                hasAnyCellComments,
                ref styleCache);
        }
    }

    private static bool IsWithinVisibleRowRange(IReadOnlyList<RowMetric> rowMetrics, uint row) =>
        rowMetrics.Count != 0 &&
        row >= rowMetrics[0].Row &&
        row <= rowMetrics[^1].Row;

    private static bool IsWithinVisibleColumnRange(IReadOnlyList<ColMetric> colMetrics, uint col) =>
        colMetrics.Count != 0 &&
        col >= colMetrics[0].Col &&
        col <= colMetrics[^1].Col;

    private static bool IsVisibleMetricRow(IReadOnlyList<RowMetric> rowMetrics, uint row)
    {
        if (rowMetrics.Count == 0 || row < rowMetrics[0].Row || row > rowMetrics[^1].Row)
            return false;

        var low = 0;
        var high = rowMetrics.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metricRow = rowMetrics[mid].Row;
            if (metricRow == row)
                return true;
            if (metricRow < row)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return false;
    }

    private static bool TryGetVisibleColumnTargetWidth(
        Sheet sheet,
        SheetId sheetId,
        uint row,
        IReadOnlyList<ColMetric> colMetrics,
        uint col,
        out int targetWidthCharacters)
    {
        targetWidthCharacters = 0;
        if (colMetrics.Count == 0 || col < colMetrics[0].Col || col > colMetrics[^1].Col)
            return false;

        var low = 0;
        var high = colMetrics.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var metric = colMetrics[mid];
            if (metric.Col == col)
            {
                targetWidthCharacters = EstimateCharacterWidth(GetMergeAwareTargetWidthPixels(sheet, sheetId, row, col, metric.Width));
                return true;
            }

            if (metric.Col < col)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return false;
    }

    /// <summary>
    /// Excel sizes a merged cell's displayed value against the merged range's COMBINED pixel
    /// width, not just the anchor column alone -- so a number that would overflow a single narrow
    /// column can still fit (or fall back to fewer significant digits / ### less aggressively)
    /// once merged across several columns. Only the merge's top-left (anchor) cell ever carries a
    /// displayed value (see Sheet.Merges.cs), so non-anchor cells and unmerged cells keep using
    /// the single column's own width unchanged.
    /// </summary>
    private static double GetMergeAwareTargetWidthPixels(Sheet sheet, SheetId sheetId, uint row, uint col, double anchorColumnWidthPixels)
    {
        if (sheet.GetMergeRegion(new CellAddress(sheetId, row, col)) is not { } merge ||
            merge.Start.Row != row || merge.Start.Col != col ||
            merge.Start.Col == merge.End.Col)
        {
            return anchorColumnWidthPixels;
        }

        double total = 0;
        for (var c = merge.Start.Col; c <= merge.End.Col; c++)
            total += GetColumnWidthPixels(sheet, c);
        return total;
    }

    private static void AddCellDisplayCell(
        List<DisplayCell> cells,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        Cell cell,
        int targetWidthCharacters,
        bool includeFormulas,
        bool? showFormulasOverride,
        CfEvaluationContext cfContext,
        bool hasConditionalStyles,
        bool hasConditionalIcons,
        bool hasConditionalDataBars,
        bool hasAnyCellComments,
        ref ViewportStyleCache styleCache)
    {
        var style = styleCache.Get(workbook, cell.StyleId);
        ConditionalFormatIcon? cfIcon = null;
        ConditionalFormatDataBar? cfDataBar = null;
        var hasComment = false;
        if (hasConditionalStyles || hasConditionalIcons || hasConditionalDataBars || hasAnyCellComments)
            ApplyConditionalVisualsAndComments(
                sheet,
                sheetId,
                row,
                col,
                cell.Value,
                workbook,
                cfContext,
                cell.StyleId == StyleId.Default,
                hasConditionalStyles,
                hasConditionalIcons,
                hasConditionalDataBars,
                hasAnyCellComments,
                ref style,
                out cfIcon,
                out cfDataBar,
                out hasComment);

        var displayText = cfIcon?.ShowValue == false || cfDataBar?.ShowValue == false
            ? ""
            : GetDisplayText(workbook, sheet, cell, row, col, ref style, targetWidthCharacters, showFormulasOverride);
        var commentDisplay = hasComment
            ? CreateCellCommentDisplay(sheet, new CellAddress(sheetId, row, col))
            : null;
        hasComment = commentDisplay is not null;

        if (!IsRowHidden(sheet, row) && !sheet.IsColEffectivelyHidden(col))
            ApplyHiddenNeighborBorderMigration(workbook, sheet, row, col, ref style, ref styleCache);

        cells.Add(new DisplayCell(
            row,
            col,
            cell.Value,
            displayText,
            includeFormulas ? cell.FormulaText : null,
            cell.StyleId,
            null,
            style,
            cfIcon,
            hasComment,
            cfDataBar,
            commentDisplay));
    }

    /// <summary>
    /// Excel does not erase a border set on a hidden row/column: hiding zeroes the row's/column's
    /// height/width, so the hidden cell's border visually fuses onto the boundary between whichever
    /// visible neighbors it now sits directly between (the "collapsed seam"). This mutates
    /// <paramref name="style"/> (cloning it first — it may be a shared cached instance) so that any
    /// edge whose immediate neighbor is hidden gets the heaviest border found across the collapsed
    /// run, matching GridView.Rendering's own "heaviest style wins" shared-edge precedence
    /// (<c>ResolveBorderEdgeWinner</c>) so the seam resolves identically from either visible side.
    /// A directly-adjacent-VISIBLE edge is left completely untouched — that case is already handled
    /// correctly by the renderer's own neighbor lookup and must not be second-guessed here.
    /// </summary>
    private static void ApplyHiddenNeighborBorderMigration(
        Workbook workbook, Sheet sheet, uint row, uint col, ref CellStyle style, ref ViewportStyleCache styleCache)
    {
        var migratedTop = MigrateHiddenRowBorder(workbook, sheet, row, col, -1, style.BorderTop, ref styleCache);
        var migratedBottom = MigrateHiddenRowBorder(workbook, sheet, row, col, 1, style.BorderBottom, ref styleCache);
        var migratedLeft = MigrateHiddenColBorder(workbook, sheet, row, col, -1, style.BorderLeft, ref styleCache);
        var migratedRight = MigrateHiddenColBorder(workbook, sheet, row, col, 1, style.BorderRight, ref styleCache);

        if (migratedTop == style.BorderTop && migratedBottom == style.BorderBottom &&
            migratedLeft == style.BorderLeft && migratedRight == style.BorderRight)
        {
            return;
        }

        style = style.Clone();
        style.BorderTop = migratedTop;
        style.BorderBottom = migratedBottom;
        style.BorderLeft = migratedLeft;
        style.BorderRight = migratedRight;
    }

    /// <summary>
    /// Walks from (row, col) across a run of hidden rows in the <paramref name="rowStep"/>
    /// direction (-1 = up/toward BorderTop, +1 = down/toward BorderBottom) to the first visible
    /// row, folding every border that touches the collapsed seam: this cell's own facing edge
    /// (<paramref name="own"/>), both edges of every hidden row's cell in the run, and the far
    /// visible neighbor's own facing edge. Returns <paramref name="own"/> unchanged when the
    /// immediate neighbor row isn't hidden, so an ordinary adjacent-visible-cell edge is never
    /// touched.
    /// </summary>
    private static CellBorder MigrateHiddenRowBorder(
        Workbook workbook, Sheet sheet, uint row, uint col, int rowStep, CellBorder own, ref ViewportStyleCache styleCache)
    {
        var neighborRow = (long)row + rowStep;
        if (neighborRow < 1 || neighborRow > CellAddress.MaxRow || !IsRowHidden(sheet, (uint)neighborRow))
            return own;

        var winner = own;
        var r = (uint)neighborRow;
        while (IsRowHidden(sheet, r))
        {
            if (GetRawCellStyleForBorderMigration(workbook, sheet, r, col, ref styleCache) is { } hiddenStyle)
            {
                winner = ResolveHeavierBorder(winner, hiddenStyle.BorderTop);
                winner = ResolveHeavierBorder(winner, hiddenStyle.BorderBottom);
            }

            var next = (long)r + rowStep;
            if (next < 1 || next > CellAddress.MaxRow)
                return winner;
            r = (uint)next;
        }

        // r is the far VISIBLE row this hidden run collapses onto; fold in its own facing edge too
        // so both sides of the seam agree on the same final winner regardless of processing order.
        if (GetRawCellStyleForBorderMigration(workbook, sheet, r, col, ref styleCache) is { } farStyle)
        {
            var farFacing = rowStep < 0 ? farStyle.BorderBottom : farStyle.BorderTop;
            winner = ResolveHeavierBorder(winner, farFacing);
        }

        return winner;
    }

    /// <summary>Column counterpart of <see cref="MigrateHiddenRowBorder"/>.</summary>
    private static CellBorder MigrateHiddenColBorder(
        Workbook workbook, Sheet sheet, uint row, uint col, int colStep, CellBorder own, ref ViewportStyleCache styleCache)
    {
        var neighborCol = (long)col + colStep;
        if (neighborCol < 1 || neighborCol > CellAddress.MaxCol || !sheet.IsColEffectivelyHidden((uint)neighborCol))
            return own;

        var winner = own;
        var c = (uint)neighborCol;
        while (sheet.IsColEffectivelyHidden(c))
        {
            if (GetRawCellStyleForBorderMigration(workbook, sheet, row, c, ref styleCache) is { } hiddenStyle)
            {
                winner = ResolveHeavierBorder(winner, hiddenStyle.BorderLeft);
                winner = ResolveHeavierBorder(winner, hiddenStyle.BorderRight);
            }

            var next = (long)c + colStep;
            if (next < 1 || next > CellAddress.MaxCol)
                return winner;
            c = (uint)next;
        }

        if (GetRawCellStyleForBorderMigration(workbook, sheet, row, c, ref styleCache) is { } farStyle)
        {
            var farFacing = colStep < 0 ? farStyle.BorderRight : farStyle.BorderLeft;
            winner = ResolveHeavierBorder(winner, farFacing);
        }

        return winner;
    }

    /// <summary>
    /// Reads the raw stored style for an arbitrary cell (regardless of hidden state or whether it
    /// carries a value) for border-migration purposes: a real cell's style, else a style-only blank
    /// run's style, else null (no border contribution).
    /// </summary>
    private static CellStyle? GetRawCellStyleForBorderMigration(
        Workbook workbook, Sheet sheet, uint row, uint col, ref ViewportStyleCache styleCache)
    {
        var cell = sheet.GetCell(row, col);
        if (cell is not null)
            return styleCache.Get(workbook, cell.StyleId);

        var styleOnlyId = sheet.GetStyleOnly(row, col);
        return styleOnlyId.HasValue ? styleCache.Get(workbook, styleOnlyId.Value) : null;
    }

    // Ranked heaviest/most-prominent first, mirroring GridView.Rendering's BorderEdgePrecedence —
    // kept as an independent copy here since Core.Calc cannot reference the App.UI renderer.
    private static readonly BorderStyle[] BorderMigrationPrecedence =
    {
        BorderStyle.Double,
        BorderStyle.Thick,
        BorderStyle.Medium,
        BorderStyle.MediumDashDotDot,
        BorderStyle.MediumDashDot,
        BorderStyle.MediumDashed,
        BorderStyle.SlantDashDot,
        BorderStyle.Thin,
        BorderStyle.DashDotDot,
        BorderStyle.DashDot,
        BorderStyle.Dashed,
        BorderStyle.Dotted,
        BorderStyle.Hair,
        BorderStyle.None,
    };

    private static int BorderMigrationPrecedenceRank(BorderStyle style)
    {
        var index = Array.IndexOf(BorderMigrationPrecedence, style);
        return index < 0 ? BorderMigrationPrecedence.Length : index;
    }

    private static CellBorder ResolveHeavierBorder(CellBorder mine, CellBorder other)
    {
        if (mine.Style == BorderStyle.None) return other;
        if (other.Style == BorderStyle.None) return mine;
        return BorderMigrationPrecedenceRank(mine.Style) <= BorderMigrationPrecedenceRank(other.Style) ? mine : other;
    }

    private struct ViewportStyleCache
    {
        private Dictionary<StyleId, CellStyle>? _styles;

        public CellStyle Get(Workbook workbook, StyleId styleId)
        {
            if (_styles is not null && _styles.TryGetValue(styleId, out var style))
                return style;

            style = workbook.GetStyle(styleId);
            (_styles ??= new Dictionary<StyleId, CellStyle>(4)).Add(styleId, style);
            return style;
        }
    }

    private static void ApplyConditionalVisualsAndComments(
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext,
        bool baseStyleIsDefault,
        bool hasConditionalStyles,
        bool hasConditionalIcons,
        bool hasConditionalDataBars,
        bool hasAnyCellComments,
        ref CellStyle style,
        out ConditionalFormatIcon? cfIcon,
        out ConditionalFormatDataBar? cfDataBar,
        out bool hasComment)
    {
        cfIcon = null;
        cfDataBar = null;
        hasComment = false;

        var addr = new CellAddress(sheetId, row, col);
        if (hasConditionalStyles)
        {
            var cfStyle = EvaluateConditionalFormats(sheet, addr, value, workbook, cfContext);
            if (cfStyle is { } result)
            {
                style = baseStyleIsDefault && result.CanUseAsDefaultMergedStyle
                    ? result.Style
                    : MergeStyles(style, result.Style);
            }
        }

        if (hasConditionalIcons)
            cfIcon = EvaluateConditionalIcon(sheet, addr, value, workbook, cfContext);

        if (hasConditionalDataBars)
            cfDataBar = EvaluateConditionalDataBar(sheet, addr, value, workbook, cfContext);

        if (hasAnyCellComments)
            hasComment = HasCellComment(sheet, addr, hasAnyCellComments);
    }

    private static bool HasConditionalStyleRules(CfEvaluationContext cfContext)
    {
        for (var i = 0; i < cfContext.RulesByPriority.Count; i++)
        {
            var rule = cfContext.RulesByPriority[i];
            if (rule.RuleType == CfRuleType.ColorScale || rule.FormatIfTrue is not null)
                return true;
        }

        return false;
    }

    private static bool HasConditionalDataBarRules(CfEvaluationContext cfContext)
    {
        for (var i = 0; i < cfContext.RulesByPriority.Count; i++)
        {
            if (cfContext.RulesByPriority[i].RuleType == CfRuleType.DataBar)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<ChartDataCell> BuildChartDataCells(
        Workbook workbook,
        Sheet sheet,
        ref ViewportStyleCache styleCache)
    {
        if (sheet.Charts.Count == 0)
            return [];

        var chartCells = new List<ChartDataCell>();
        var seen = new HashSet<(SheetId SheetId, uint Row, uint Col)>();
        foreach (var chart in sheet.Charts)
        {
            if (!chart.IsVisible)
                continue;

            var sourceSheet = workbook.GetSheet(chart.DataRange.Start.Sheet);
            if (sourceSheet is null)
                continue;

            var sampledCells = 0;
            for (uint row = chart.DataRange.Start.Row; row <= chart.DataRange.End.Row; row++)
            {
                for (uint col = chart.DataRange.Start.Col; col <= chart.DataRange.End.Col; col++)
                {
                    if (sampledCells++ >= MaxChartDataCellsPerViewport)
                        return chartCells;

                    if (!chart.ShowDataInHiddenRowsAndColumns &&
                        (sourceSheet.IsRowEffectivelyHidden(row) || sourceSheet.IsColEffectivelyHidden(col)))
                    {
                        continue;
                    }

                    if (!seen.Add((sourceSheet.Id, row, col)))
                        continue;

                    var cell = sourceSheet.GetCell(row, col);
                    if (cell is null)
                    {
                        chartCells.Add(new ChartDataCell(sourceSheet.Id, row, col, "", BlankValue.Instance));
                        continue;
                    }

                    var style = styleCache.Get(workbook, cell.StyleId);
                    chartCells.Add(new ChartDataCell(
                        sourceSheet.Id,
                        row,
                        col,
                        GetDisplayText(
                            workbook,
                            sourceSheet,
                            cell,
                            row,
                            col,
                            ref style,
                            EstimateCharacterWidth(ColumnWidthToPixels(
                                sourceSheet.ColumnWidths.GetValueOrDefault(col, sourceSheet.DefaultColumnWidth)))),
                        cell.Value));
                }
            }
        }

        return chartCells;
    }

    private static List<DisplayCell> BuildSplitPaneCells(
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns,
        bool includeFormulas,
        bool? showFormulasOverride,
        CfEvaluationContext cfContext,
        bool hasAnyCellComments,
        bool hasConditionalDataBars,
        ref ViewportStyleCache styleCache)
    {
        var dedupeCells = SplitPaneRegionsCanOverlap(topRows, leftColumns, bottomLeftRows, topRightColumns);
        HashSet<(uint Row, uint Col)>? seen = null;
        // R136-io-worksheet-props-col-row-default-style: a sheet can have NO legacy per-cell
        // style-only entries at all (HasStyleOnlyCells false) yet still need GetStyleOnly consulted
        // for empty cells, because GetStyleOnly now also falls back to a whole-row/-column default
        // style (Sheet.RowStyles/ColumnStyles) -- without this, the viewport's fast-path guard below
        // would skip calling GetStyleOnly entirely for such a sheet, so a Currency-formatted empty
        // column would render as unformatted in the live grid despite resolving correctly everywhere
        // else (display formatters, cell-entry seeding, print/PDF layout).
        var hasAnyStyleOnlyCells = sheet.HasStyleOnlyCells || sheet.ColumnStyles.Count != 0 || sheet.RowStyles.Count != 0;
        var hasConditionalStyles = HasConditionalStyleRules(cfContext);
        var hasConditionalIcons = cfContext.IconRulesByPriority.Count != 0;
        var cells = new List<DisplayCell>(EstimateSplitPaneCellCapacity(
            topRows,
            leftColumns,
            bottomLeftRows,
            topRightColumns,
            sheet,
            hasAnyCellComments,
            hasAnyStyleOnlyCells));

        foreach (var row in topRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(GetMergeAwareTargetWidthPixels(sheet, sheetId, row.Row, column.Col, column.Width)), includeFormulas, showFormulasOverride, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalStyles, hasConditionalIcons, hasConditionalDataBars, ref styleCache);
            foreach (var column in topRightColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(GetMergeAwareTargetWidthPixels(sheet, sheetId, row.Row, column.Col, column.Width)), includeFormulas, showFormulasOverride, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalStyles, hasConditionalIcons, hasConditionalDataBars, ref styleCache);
        }

        foreach (var row in bottomLeftRows)
        {
            foreach (var column in leftColumns)
                AddDisplayCell(cells, ref seen, dedupeCells, workbook, sheet, sheetId, row.Row, column.Col, EstimateCharacterWidth(GetMergeAwareTargetWidthPixels(sheet, sheetId, row.Row, column.Col, column.Width)), includeFormulas, showFormulasOverride, cfContext, hasAnyCellComments, hasAnyStyleOnlyCells, hasConditionalStyles, hasConditionalIcons, hasConditionalDataBars, ref styleCache);
        }

        return cells;
    }

    private static int EstimateSplitPaneCellCapacity(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns,
        Sheet sheet,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells)
    {
        var visibleSlots = SaturatingAdd(
            EstimateVisibleCellSlots(topRows.Count, SaturatingAdd(leftColumns.Count, topRightColumns.Count)),
            EstimateVisibleCellSlots(bottomLeftRows.Count, leftColumns.Count));
        if (visibleSlots == 0)
            return 0;

        if (hasAnyStyleOnlyCells)
            return ClampCapacityHint(visibleSlots);

        var possibleCells = sheet.CellCount;
        if (hasAnyCellComments)
            possibleCells = SaturatingAdd(possibleCells, sheet.Comments.Count + sheet.ThreadedComments.Count);

        return ClampCapacityHint(Math.Min(visibleSlots, possibleCells));
    }

    private static void AddDisplayCell(
        List<DisplayCell> cells,
        ref HashSet<(uint Row, uint Col)>? seen,
        bool dedupeCells,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        int targetWidthCharacters,
        bool includeFormulas,
        bool? showFormulasOverride,
        CfEvaluationContext cfContext,
        bool hasAnyCellComments,
        bool hasAnyStyleOnlyCells,
        bool hasConditionalStyles,
        bool hasConditionalIcons,
        bool hasConditionalDataBars,
        ref ViewportStyleCache styleCache)
    {
        if (dedupeCells && !AddSeenCell(ref seen, row, col))
            return;

        // A row/column reaching this point despite being hidden is only in the metrics list
        // because it anchors a merge with a visible remainder (see
        // IsHiddenMergeAnchorRowWithVisibleRemainder/...ColWithVisibleRemainder). The merge's
        // value/style live solely on its anchor cell, so any OTHER cell in that hidden row/col
        // must not leak into the viewport.
        var rowHidden = IsRowHidden(sheet, row);
        var colHidden = sheet.IsColEffectivelyHidden(col);
        if ((rowHidden || colHidden) &&
            !IsExposedHiddenMergeAnchorCell(sheet, row, col, rowHidden, colHidden))
        {
            return;
        }

        var cell = sheet.GetCell(row, col);
        if (cell is null)
        {
            if (!hasAnyStyleOnlyCells && !hasAnyCellComments && !hasConditionalStyles)
                return;

            var styleOnlyId = hasAnyStyleOnlyCells ? sheet.GetStyleOnly(row, col) : null;
            if (!styleOnlyId.HasValue)
            {
                var address = new CellAddress(sheetId, row, col);
                if (hasConditionalStyles && IsWithinStyleRuleRanges(cfContext, address))
                {
                    AddBlankConditionalDisplayCell(
                        cells,
                        workbook,
                        sheet,
                        sheetId,
                        row,
                        col,
                        cfContext,
                        hasAnyCellComments,
                        ref styleCache);
                    return;
                }

                if (hasAnyCellComments &&
                    HasCellComment(sheet, address, hasAnyCellComments))
                {
                    var commentOnlyDisplay = CreateCellCommentDisplay(sheet, address);
                    cells.Add(new DisplayCell(
                        row,
                        col,
                        BlankValue.Instance,
                        "",
                        null,
                        StyleId.Default,
                        null,
                        styleCache.Get(workbook, StyleId.Default),
                        null,
                        commentOnlyDisplay is not null,
                        null,
                        commentOnlyDisplay));
                }

                return;
            }

            var style = styleCache.Get(workbook, styleOnlyId.Value);
            ConditionalFormatIcon? cfIcon = null;
            ConditionalFormatDataBar? cfDataBar = null;
            var hasComment = false;
            if (hasConditionalStyles || hasConditionalIcons || hasConditionalDataBars || hasAnyCellComments)
                ApplyConditionalVisualsAndComments(
                    sheet,
                    sheetId,
                    row,
                    col,
                    BlankValue.Instance,
                    workbook,
                    cfContext,
                    styleOnlyId.Value == StyleId.Default,
                    hasConditionalStyles,
                    hasConditionalIcons,
                    hasConditionalDataBars,
                    hasAnyCellComments,
                    ref style,
                    out cfIcon,
                    out cfDataBar,
                    out hasComment);
            var commentDisplay = hasComment
                ? CreateCellCommentDisplay(sheet, new CellAddress(sheetId, row, col))
                : null;
            hasComment = commentDisplay is not null;

            if (!rowHidden && !colHidden)
                ApplyHiddenNeighborBorderMigration(workbook, sheet, row, col, ref style, ref styleCache);

            cells.Add(new DisplayCell(
                row,
                col,
                BlankValue.Instance,
                "",
                null,
                styleOnlyId.Value,
                null,
                style,
                cfIcon,
                hasComment,
                cfDataBar,
                commentDisplay));
            return;
        }

        AddCellDisplayCell(
            cells,
            workbook,
            sheet,
            sheetId,
            row,
            col,
            cell,
            targetWidthCharacters,
            includeFormulas,
            showFormulasOverride,
            cfContext,
            hasConditionalStyles,
            hasConditionalIcons,
            hasConditionalDataBars,
            hasAnyCellComments,
            ref styleCache);
    }

    private static bool IsWithinStyleRuleRanges(CfEvaluationContext cfContext, CellAddress address)
    {
        var ranges = cfContext.StyleRuleRanges;
        for (var i = 0; i < ranges.Count; i++)
        {
            if (ranges[i].Contains(address))
                return true;
        }

        return false;
    }

    // A fully blank slot (no cell, no style-only entry) inside a conditional-format range still
    // renders CF fills in Excel (e.g. highlight-blanks rules, =$A1="" formula rules), so evaluate
    // the style rules for a blank value and materialize a DisplayCell only when one matches.
    private static void AddBlankConditionalDisplayCell(
        List<DisplayCell> cells,
        Workbook workbook,
        Sheet sheet,
        SheetId sheetId,
        uint row,
        uint col,
        CfEvaluationContext cfContext,
        bool hasAnyCellComments,
        ref ViewportStyleCache styleCache)
    {
        var defaultStyle = styleCache.Get(workbook, StyleId.Default);
        var style = defaultStyle;
        ApplyConditionalVisualsAndComments(
            sheet,
            sheetId,
            row,
            col,
            BlankValue.Instance,
            workbook,
            cfContext,
            baseStyleIsDefault: true,
            hasConditionalStyles: true,
            hasConditionalIcons: false,
            hasConditionalDataBars: false,
            hasAnyCellComments,
            ref style,
            out _,
            out _,
            out var hasComment);

        var commentDisplay = hasComment
            ? CreateCellCommentDisplay(sheet, new CellAddress(sheetId, row, col))
            : null;
        if (ReferenceEquals(style, defaultStyle) && commentDisplay is null)
            return;

        cells.Add(new DisplayCell(
            row,
            col,
            BlankValue.Instance,
            "",
            null,
            StyleId.Default,
            null,
            style,
            null,
            commentDisplay is not null,
            null,
            commentDisplay));
    }

    private static bool AddSeenCell(ref HashSet<(uint Row, uint Col)>? seen, uint row, uint col)
    {
        seen ??= [];
        return seen.Add((row, col));
    }

    private static bool SplitPaneRegionsCanOverlap(
        IReadOnlyList<RowMetric> topRows,
        IReadOnlyList<ColMetric> leftColumns,
        IReadOnlyList<RowMetric> bottomLeftRows,
        IReadOnlyList<ColMetric> topRightColumns) =>
        (topRows.Count > 0 && ColumnsOverlap(leftColumns, topRightColumns)) ||
        (leftColumns.Count > 0 && RowsOverlap(topRows, bottomLeftRows));

    private static bool RowsOverlap(IReadOnlyList<RowMetric> first, IReadOnlyList<RowMetric> second)
    {
        for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
        {
            var row = first[firstIndex].Row;
            for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
            {
                var otherRow = second[secondIndex].Row;
                if (otherRow == row)
                    return true;
                if (otherRow > row)
                    break;
            }
        }

        return false;
    }

    private static bool ColumnsOverlap(IReadOnlyList<ColMetric> first, IReadOnlyList<ColMetric> second)
    {
        for (var firstIndex = 0; firstIndex < first.Count; firstIndex++)
        {
            var col = first[firstIndex].Col;
            for (var secondIndex = 0; secondIndex < second.Count; secondIndex++)
            {
                var otherCol = second[secondIndex].Col;
                if (otherCol == col)
                    return true;
                if (otherCol > col)
                    break;
            }
        }

        return false;
    }

    private static bool HasAnyCellComments(Sheet sheet) =>
        sheet.Comments.Count != 0 ||
        sheet.ThreadedComments.Count != 0;

    private static bool HasCellComment(Sheet sheet, CellAddress address, bool hasAnyCellComments) =>
        hasAnyCellComments &&
        (sheet.Comments.ContainsKey(address) ||
         sheet.ThreadedComments.ContainsKey(address));

    private static CellCommentDisplay? CreateCellCommentDisplay(Sheet sheet, CellAddress address)
    {
        var hasNote = sheet.Comments.TryGetValue(address, out var note);
        var hasThreadedComment = sheet.ThreadedComments.TryGetValue(address, out var threadedComment);

        if (hasThreadedComment && hasNote && IsLegacyMirrorOfThreadedComment(note, threadedComment!))
        {
            // Excel mirrors a threaded comment's root text into a legacy comments1.xml/VML "note"
            // for backward compatibility with older readers. That mirror is never surfaced to the
            // user in real Excel -- only the threaded conversation is shown -- so when the note's
            // text is exactly that backward-compat mirror, display only the threaded comment (no
            // bogus duplicate "Note").
            hasNote = false;
        }

        if (hasThreadedComment && hasNote)
        {
            // A genuine, independently-authored legacy Note coexists with a threaded comment
            // (e.g. a note added before threaded comments existed, or added separately by a
            // reader that doesn't understand threads). Excel shows both in this case, so combine
            // them into a single Mixed preview rather than dropping the note.
            var resolvedTitle = threadedComment!.IsResolved ? "Resolved comment" : "Comment";
            var mixedBody = new StringBuilder();
            mixedBody.Append("Note:").AppendLine().Append(note).AppendLine().AppendLine();
            mixedBody.Append("Comment:").AppendLine().Append(FormatThreadedComment(threadedComment));

            return new CellCommentDisplay(
                CellCommentDisplayKind.Mixed,
                $"{resolvedTitle} and note",
                mixedBody.ToString(),
                threadedComment.IsResolved);
        }

        if (hasThreadedComment)
        {
            return new CellCommentDisplay(
                CellCommentDisplayKind.ThreadedComment,
                threadedComment!.IsResolved ? "Resolved comment" : "Comment",
                FormatThreadedComment(threadedComment),
                threadedComment.IsResolved);
        }

        return hasNote
            ? new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", note ?? string.Empty)
            : null;
    }

    // Real Excel 365 writes its legacy comments1.xml/VML "note" mirror of a threaded comment's
    // root text using a fixed compatibility banner, never "{Author}:\n{RootText}". The banner
    // always starts with this literal line; the legacy author Excel assigns to the mirror is
    // always literally "tc={GUID}" (never a real display name). XlsxWorksheetCommentReader
    // already filters this shim out before it ever reaches Sheet.Comments, but this check stays
    // as defense-in-depth for any other load path (e.g. a workbook round-tripped through the
    // native JSON format, or constructed directly in-memory) that might still carry it.
    private const string LegacyThreadedCommentBanner = "[Threaded comment]";

    /// <summary>
    /// Detects whether <paramref name="note"/> is the backward-compat mirror that Excel writes
    /// into the legacy comments1.xml/VML "note" part for a threaded comment's root text (so
    /// pre-2018 readers still see something), rather than a genuine, independently-authored Note.
    /// Excel's real mirror is the fixed "[Threaded comment]" compatibility banner (never
    /// "{Author}:\n{RootText}", despite what earlier revisions of this method assumed). Anything
    /// else -- including a note that merely happens to repeat the comment text -- is treated as a
    /// real Note so it is never silently dropped from the display.
    /// </summary>
    private static bool IsLegacyMirrorOfThreadedComment(string? note, ThreadedComment threadedComment)
    {
        if (string.IsNullOrEmpty(note))
            return false;

        if (note.TrimStart().StartsWith(LegacyThreadedCommentBanner, StringComparison.Ordinal))
            return true;

        // Older/non-Excel producers may still write the previously-assumed "{Author}:\n{RootText}"
        // form; keep recognizing it too so those files don't regress to showing a bogus Mixed note.
        var expectedMirror = string.IsNullOrWhiteSpace(threadedComment.Author)
            ? threadedComment.Text
            : $"{threadedComment.Author.Trim()}:\n{threadedComment.Text}";

        return string.Equals(
            NormalizeLineEndings(note),
            NormalizeLineEndings(expectedMirror),
            StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Trim();

    private static string FormatThreadedComment(ThreadedComment comment)
    {
        var body = new StringBuilder();
        AppendCommentLine(body, comment.Author, comment.CreatedAtUtc, comment.Text);
        foreach (var reply in comment.Replies)
        {
            body.AppendLine();
            body.AppendLine();
            AppendCommentLine(body, reply.Author, reply.CreatedAtUtc, reply.Text);
        }

        return body.ToString();
    }

    private static void AppendCommentLine(StringBuilder body, string? author, DateTimeOffset? createdAtUtc, string text)
    {
        var heading = FormatCommentMessageHeading(author, createdAtUtc);
        if (!string.IsNullOrEmpty(heading))
            body.Append(heading).Append(": ");

        body.Append(text);
    }

    /// <summary>
    /// Builds the "{Author} - yyyy-MM-dd HH:mm UTC" heading for one message in the hover-preview
    /// popup, matching FreeX.App.Presentation.Comments.ThreadedCommentDialogPlanner.FormatMessageHeading
    /// (the format the inline comment editor uses for the very same thread). Core.Calc cannot
    /// reference App.Presentation, so the format is intentionally duplicated here rather than
    /// shared -- the two must be kept in sync by hand so the hover popup and the editor agree on
    /// every message's author/timestamp heading instead of the hover view silently dropping the
    /// timestamp.
    /// </summary>
    private static string FormatCommentMessageHeading(string? author, DateTimeOffset? createdAtUtc)
    {
        var label = author?.Trim() ?? string.Empty;
        if (createdAtUtc is null)
            return label;

        var formatted = createdAtUtc.Value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(label) ? formatted : $"{label} - {formatted}";
    }

    // ── Conditional format evaluation ─────────────────────────────────────────

    /// <summary>
    /// Evaluates all conditional format rules that cover <paramref name="addr"/> (ordered by
    /// Priority ascending = highest precedence first). Returns the first matching rule's style,
    /// or null when no rule fires.
    /// </summary>
    private static string GetDisplayText(
        Workbook workbook,
        Sheet sheet,
        Cell cell,
        uint row,
        uint col,
        ref CellStyle style,
        int targetWidthCharacters,
        bool? showFormulasOverride = null)
    {
        var showFormulas = showFormulasOverride ?? sheet.ShowFormulas;
        if (showFormulas && cell.FormulaText is not null &&
            !(sheet.IsProtected && IsEffectivelyHidden(workbook, sheet, cell, row, col)))
        {
            return "=" + cell.FormulaText;
        }

        // Excel's "Show a zero in cells that have zero value" sheet option (sheetView
        // showZeros): when off, a cell whose value is literally 0 renders blank instead of
        // "0" -- UNLESS the cell's own number format defines an explicit third (zero) section,
        // in which case that section's own rendering (which may itself already be blank, via
        // e.g. "0;-0;;@") governs and the sheet-level preference is not consulted.
        if (!sheet.ShowZeros &&
            cell.Value is NumberValue { Value: 0.0 } &&
            !NumberFormatHasExplicitZeroSection(style.NumberFormat))
        {
            return string.Empty;
        }

        // Excel never shows the width-based '#' overflow indicator when ShrinkToFit is on --
        // the real value shrinks (font-wise, in GridView's render pass) to fit the column
        // instead. Suppressing the indicator here lets the real formatted text reach that
        // shrink-font path unmolested; ShrinkToFit off keeps the normal '#' overflow behavior.
        var result = NumberFormatter.FormatWithColor(
            cell.Value,
            style.NumberFormat,
            targetWidthCharacters,
            workbook.IndexedColors,
            workbook.Theme,
            workbook.Uses1904DateSystem,
            suppressWidthOverflowIndicator: style.ShrinkToFit);
        if (TryParseHexColor(result.ColorHex, out var color))
        {
            if (style.FontColor != color)
            {
                style = style.Clone();
                style.FontColor = color;
            }
        }

        return result.Text;
    }

    /// <summary>
    /// Mirrors <c>SpreadsheetDisplayFormatter.IsHidden</c>: resolves the cell's effective style
    /// (falling back to the row/column style-only run when the cell has no explicit style) and
    /// returns whether Format Cells &gt; Protection &gt; Hidden is set. Used to keep Show Formulas
    /// from disclosing formulas of Hidden cells while the sheet is protected (Excel parity).
    /// </summary>
    private static bool IsEffectivelyHidden(Workbook workbook, Sheet sheet, Cell cell, uint row, uint col)
    {
        var styleId = cell.StyleId != StyleId.Default
            ? cell.StyleId
            : sheet.GetStyleOnly(row, col) ?? StyleId.Default;
        return workbook.GetStyle(styleId).Hidden;
    }

    /// <summary>
    /// True when <paramref name="numberFormat"/> defines a third (zero-specific) section --
    /// e.g. "#,##0;(#,##0);\"-\"" -- meaning the format itself dictates how a zero value
    /// renders and the sheet's ShowZeros preference must not override it. Sections are
    /// separated by top-level ';' characters (not inside a quoted literal or a [bracketed]
    /// directive); an empty/General format has a single (implicit) section.
    /// </summary>
    private static bool NumberFormatHasExplicitZeroSection(string? numberFormat)
    {
        if (string.IsNullOrEmpty(numberFormat))
            return false;

        return NumberFormatSectionTokenizer.Count(numberFormat) >= 3;
    }

    private static bool TryParseHexColor(string? hex, out CellColor color)
    {
        color = default;
        if (hex is null ||
            hex.Length != 7 ||
            hex[0] != '#' ||
            !byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = CellColor.FromArgb(r, g, b);
        return true;
    }

    private static double GetDefaultColumnWidthPixels(Sheet sheet) =>
        Math.Max(1, ColumnWidthToPixels(sheet.DefaultColumnWidth));

    private static double GetColumnWidthPixels(Sheet sheet, uint col) =>
        Math.Max(1, ColumnWidthToPixels(sheet.ColumnWidths.GetValueOrDefault(col, sheet.DefaultColumnWidth)));

    private static double ColumnWidthToPixels(double width)
        => ColumnWidthPixelMapper.ColumnWidthToPixels(width);

    private static int EstimateCharacterWidth(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= 0)
            return 1;

        var width = pixelWidth <= 12
            ? pixelWidth / 12.0
            : (pixelWidth - 5.0) / 7.0;
        return Math.Max(1, (int)Math.Round(width, MidpointRounding.AwayFromZero));
    }

}
