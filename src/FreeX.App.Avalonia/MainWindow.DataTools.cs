using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using PresentationAdvancedFilterReapplyPlanner = FreeX.App.Presentation.Filtering.AdvancedFilterReapplyPlanner;
using PresentationAdvancedFilterReapplyState = FreeX.App.Presentation.Filtering.AdvancedFilterReapplyState;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── Data-tab tools: Reapply, Circle Invalid Data / Clear Validation Circles, Get Data, Refresh All ──
    //
    // Validation circles are an in-memory overlay only (same lifetime model as formula-auditing trace
    // arrows): we keep the active invalid-cell set in this field and repaint it on every overlay rebuild
    // (RefreshShell -> BuildSheetGrid -> BuildDrawingObjectOverlay). Clearing empties the set and refreshes.
    private readonly List<CellAddress> _validationCircleCells = new();
    private PresentationAdvancedFilterReapplyState? _lastInPlaceAdvancedFilter;

    // ── Reapply ─────────────────────────────────────────────────────────────────
    //
    // AutoFilter reapply comes from durable worksheet metadata. In-place Advanced Filter keeps its
    // list/criteria intent in the shared presentation reapply contract because that definition is not
    // persisted as worksheet metadata. Reapply deliberately does not replay SortState: WPF treats
    // Reapply as filter-only, and sorting is a separate user action.
    private void ReapplyCurrentFilterSort()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;
        var selectedRange = _session.SelectedRange;
        var selectedRanges = _session.SelectedRanges;
        var activeCell = _session.ActiveCell;
        var commands = new List<IWorkbookCommand>();

        // AdvancedFilterCommand replaces the visibility decisions in its list range. Appending the
        // AutoFilter commands after it lets FilterCommand's ownership-aware recompute add the active
        // AutoFilter exclusions without un-hiding rows that Advanced Filter just excluded. The
        // resulting FilterHiddenRows set is therefore the AND of both mechanisms.
        if (_lastInPlaceAdvancedFilter is not null)
        {
            var plan = PresentationAdvancedFilterReapplyPlanner.CreatePlan(_lastInPlaceAdvancedFilter);
            commands.Add(plan.CreateCommand());
        }

        if (TryGetAutoFilterReapplyRange(sheet, out var filterRange))
        {
            foreach (var column in sheet.AutoFilter!.FilterColumns.ToArray())
            {
                if (column.ColumnId < 0 || (uint)column.ColumnId >= filterRange.ColCount)
                    continue;

                if (TryBuildAutoFilterColumnReapplyCommand(sheetId, filterRange, column) is { } columnCommand)
                    commands.Add(columnCommand);
            }
        }

        if (commands.Count == 0)
        {
            RefreshShell(UiText.Get("TableLoc_NoReapplyableFilterOrSort"));
            return;
        }

        var result = _session.ExecuteReviewCommand(
            new CompositeWorkbookCommand("Reapply Filters", commands),
            activeCell);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ReapplyFilterFailed"));
            return;
        }

        // Reapply is a visibility mutation rather than a cell edit. SUBTOTAL/AGGREGATE formulas
        // must nevertheless see the new hidden-row set immediately, matching WPF's filter routes.
        RecalculateAfterAutoFilterMutation();

        // ExecuteReviewCommand intentionally collapses ordinary command selections. Reapply is
        // view-preserving in WPF, so restore the exact selection (including multi-area selections)
        // after the atomic command succeeds.
        _session.SelectRanges(selectedRange, selectedRanges, activeCell);
        RefreshShell(UiText.Format(
            commands.Count == 1 ? "TableLoc_ReapplyedDefinitionsOne" : "TableLoc_ReapplyedDefinitionsMany",
            commands.Count));
    }

    private static bool TryGetAutoFilterReapplyRange(Sheet sheet, out GridRange range)
    {
        range = default;
        if (sheet.AutoFilter is not { } autoFilter ||
            autoFilter.FilterColumns.Count == 0 ||
            string.IsNullOrWhiteSpace(autoFilter.Reference))
        {
            return false;
        }

        try
        {
            range = GridRange.Parse(autoFilter.Reference, sheet.Id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// R120-avalonia-datatools-reapply-1: builds the ONE live command that replays a single AutoFilter
    /// column's currently-active criterion against the sheet's current data, dispatching on whichever
    /// of the persisted <see cref="WorksheetAutoFilterColumnModel"/> fields is populated -- mirrors
    /// WPF's <c>_activeAutoFilterColumnFactories</c> (MainWindow.DataFilterCommands.cs) replaying
    /// value-list, Top 10/Above-Average, custom-condition, and color criteria alike, except this reads
    /// durable worksheet metadata instead of an in-session factory. Excel allows only one active
    /// criterion per column, so at most one branch below should ever apply to any real column; returns
    /// null (skip the column) for a button-only column with nothing to filter, or one whose criterion
    /// this shell has no live command for (an icon-set filter, the default date-grouped Year/Month/Day
    /// checklist, or unrecognized native-only filter data -- see
    /// XlsxWorksheetAutoFilterMaterializer.BuildFilters' identical "unsupported" bucket for the same
    /// two cases at file-load time).
    /// </summary>
    private static IWorkbookCommand? TryBuildAutoFilterColumnReapplyCommand(
        SheetId sheetId,
        GridRange filterRange,
        WorksheetAutoFilterColumnModel column)
    {
        var columnOffset = (uint)column.ColumnId;

        // R120-avalonia-datatools-reapply-1: a plain value-list filter's allowed set may include an
        // allowed blank, represented (mirroring FilterCommand.SplitBlankSentinel) as a literal ""
        // sentinel appended to the non-blank allowed values -- omitting it here (as the previous
        // implementation did) would silently stop allowing blanks through on Reapply.
        if (column.Values.Count > 0 || column.IncludeBlank)
        {
            var allowedValues = column.IncludeBlank && !column.Values.Contains("")
                ? [.. column.Values, ""]
                : column.Values;
            return new FilterCommand(sheetId, filterRange, columnOffset, allowedValues);
        }

        if (column.Top10 is { } top10)
        {
            var count = (uint)Math.Max(0, top10.Value ?? 10);
            return top10.Percent
                ? TopBottomFilterCommand.Percent(sheetId, filterRange, columnOffset, count, top10.Top)
                : new TopBottomFilterCommand(sheetId, filterRange, columnOffset, count, top10.Top);
        }

        if (column.DynamicFilter is { } dynamicFilter && TryGetAverageDirection(dynamicFilter, out var above))
            return new AverageFilterCommand(sheetId, filterRange, columnOffset, above);

        if (column.ColorFilter is { } colorFilter)
            return TryBuildColorFilterReapplyCommand(sheetId, filterRange, columnOffset, colorFilter);

        if (column.CustomFilters.Count > 0 &&
            CustomFilterModelReconstructor.Reconstruct(column.CustomFilters, column.CustomFiltersAnd) is { } criterion)
        {
            return new FilterConditionCommand(sheetId, filterRange, columnOffset, criterion);
        }

        return null;
    }

    /// <summary>
    /// FreeX only ever writes a "dynamicFilter" for Above/Below Average (AverageFilterCommand) -- any
    /// other dynamicFilter type (e.g. a native file's "today"/"nextMonth"/quarter-relative filter) has
    /// no live command here, mirroring XlsxWorksheetAutoFilterMaterializer.IsAverageDynamicFilter's
    /// identical load-time gate.
    /// </summary>
    private static bool TryGetAverageDirection(WorksheetAutoFilterDynamicFilterModel dynamicFilter, out bool above)
    {
        above = true;
        if (string.Equals(dynamicFilter.Type, "aboveAverage", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(dynamicFilter.Type, "belowAverage", StringComparison.OrdinalIgnoreCase))
        {
            above = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mirrors the exact (CellColor, Color) shapes CellFillColorFilterCommand/CellNoFillColorFilterCommand/
    /// CellFontColorFilterCommand persist (FreeX.Core.Commands/FilterCommand.cs) back into the matching
    /// live command. CellColor:false with a null Color ("no font color") has no dedicated command --
    /// Excel's AutoFilter color checklist never offers it as a font-color option -- so that shape is
    /// left unsupported like any other native-only filter kind.
    /// </summary>
    private static IWorkbookCommand? TryBuildColorFilterReapplyCommand(
        SheetId sheetId,
        GridRange filterRange,
        uint columnOffset,
        WorksheetAutoFilterColorFilterModel colorFilter)
    {
        if (colorFilter.CellColor)
        {
            return colorFilter.Color is { } fillColor
                ? new CellFillColorFilterCommand(sheetId, filterRange, columnOffset, fillColor)
                : new CellNoFillColorFilterCommand(sheetId, filterRange, columnOffset);
        }

        return colorFilter.Color is { } fontColor
            ? new CellFontColorFilterCommand(sheetId, filterRange, columnOffset, fontColor)
            : null;
    }

    // ── Circle Invalid Data / Clear Validation Circles ───────────────────────────

    private void CircleInvalidData()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var invalid = DataValidationCirclePlanner.FindInvalidDataCells(_session.Workbook, sheet);

        _validationCircleCells.Clear();
        _validationCircleCells.AddRange(invalid);

        RefreshShell(invalid.Count == 0
            ? UiText.Get("TableLoc_NoInvalidDataFound")
            : UiText.Format(
                invalid.Count == 1 ? "TableLoc_CircledInvalidCellsOne" : "TableLoc_CircledInvalidCellsMany",
                invalid.Count));
    }

    private void ClearValidationCircles()
    {
        if (_validationCircleCells.Count == 0)
        {
            RefreshShell(UiText.Get("TableLoc_NoValidationCirclesToClear"));
            return;
        }

        _validationCircleCells.Clear();
        RefreshShell(UiText.Get("TableLoc_ClearedValidationCircles"));
    }

    // Excel auto-clears a cell's red "invalid data" circle the instant the flagged value is
    // corrected -- the user never has to manually re-run Data > Circle Invalid Data. This overlay
    // (and therefore this prune) runs on every RefreshShell, which fires after every cell-edit
    // commit (CommitFormulaBox / CommitEditAcrossSelection / spelling and symbol inserts all call
    // RefreshShell on success), so re-checking the active sheet's still-invalid set here keeps the
    // on-screen circles in sync with the corrected data without needing a dedicated edit-commit hook.
    // The actual re-check is the shared WorkbookSession.PruneCorrectedValidationCircles helper so the
    // WPF host's equivalent overlay (MainWindow.DataCommands.cs) applies the identical rule.
    private void PruneCorrectedValidationCircles()
    {
        if (_validationCircleCells.Count == 0)
            return;

        var pruned = WorkbookSession.PruneCorrectedValidationCircles(
            _session.Workbook, _session.ActiveSheet, _validationCircleCells);

        if (ReferenceEquals(pruned, _validationCircleCells))
            return;

        _validationCircleCells.Clear();
        _validationCircleCells.AddRange(pruned);
    }

    // Called from BuildDrawingObjectOverlay so circles are painted onto the same overlay Canvas that hosts
    // charts / drawing objects / trace arrows, using the same coordinate mapping (TryGetDisplayedCellBounds).
    private void AddValidationCircleOverlay(Canvas overlay, ViewportModel viewport)
    {
        PruneCorrectedValidationCircles();

        if (_validationCircleCells.Count == 0)
            return;

        var showHeadings = _session.IsShowingHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var activeSheetId = _session.ActiveSheet.Id;

        var bounds = new List<Rect>();
        foreach (var address in _validationCircleCells)
        {
            // The viewport renders a single sheet; only circle cells that belong to it.
            if (address.Sheet != activeSheetId)
                continue;

            if (!TryGetDisplayedCellBounds(viewport, address, showHeadings, zoomFactor,
                    out var left, out var top, out var width, out var height))
                continue;

            bounds.Add(new Rect(left, top, width, height));
        }

        if (bounds.Count == 0)
            return;

        var circleVisual = new ValidationCircleControl(bounds)
        {
            Width = overlay.Width,
            Height = overlay.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(circleVisual, 0);
        Canvas.SetTop(circleVisual, 0);
        overlay.Children.Add(circleVisual);
    }

    // ── Get Data / Refresh All ────────────────────────────────────────────────────
    //
    // Get Data ▸ From Text/CSV (file-based import) lives in MainWindow.GetData.cs and Refresh re-imports the
    // remembered file source. There is still no external DB/web/query/connection engine
    // (XlsxConnectionQueryTableSchemaNormalizer only round-trips connection XML for file fidelity; it
    // executes nothing), so those connector surfaces remain out of scope.

    private sealed class ValidationCircleControl : Control
    {
        private static readonly IPen CirclePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00)), 2);
        private const double Inset = 1.0;

        private readonly IReadOnlyList<Rect> _cellBounds;

        public ValidationCircleControl(IReadOnlyList<Rect> cellBounds)
        {
            _cellBounds = cellBounds;
        }

        public override void Render(DrawingContext context)
        {
            foreach (var cell in _cellBounds)
            {
                // Excel draws a red oval bounding the cell. Inset slightly so the stroke stays inside the
                // cell rectangle, and bound the radius so very tall/wide cells stay oval rather than degenerate.
                var center = new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
                var radiusX = Math.Max(2, cell.Width / 2 - Inset);
                var radiusY = Math.Max(2, cell.Height / 2 - Inset);
                context.DrawEllipse(null, CirclePen, center, radiusX, radiusY);
            }
        }
    }
}
