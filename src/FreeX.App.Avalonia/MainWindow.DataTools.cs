using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── Data-tab tools: Reapply, Circle Invalid Data / Clear Validation Circles, Get Data, Refresh All ──
    //
    // Validation circles are an in-memory overlay only (same lifetime model as formula-auditing trace
    // arrows): we keep the active invalid-cell set in this field and repaint it on every overlay rebuild
    // (RefreshShell -> BuildSheetGrid -> BuildDrawingObjectOverlay). Clearing empties the set and refreshes.
    private readonly List<CellAddress> _validationCircleCells = new();

    // ── Reapply ─────────────────────────────────────────────────────────────────
    //
    // The Avalonia AutoFilter UI runs FilterCommand / SortCommand directly against the live sheet and does
    // not record an in-session "active criteria" object we could replay. The durable record of intent is the
    // worksheet metadata the model already persists: Sheet.AutoFilter (per-column allowed values) and
    // Sheet.SortState (sort conditions). Reapply re-executes those persisted definitions, which is exactly
    // what "re-run the current filter/sort" means for a sheet loaded from a workbook or toggled via AutoFilter.
    // When neither persisted definition carries replayable criteria, we report that honestly rather than
    // pretending work happened.
    private void ReapplyCurrentFilterSort()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;
        var applied = 0;

        if (TryGetAutoFilterReapplyRange(sheet, out var filterRange))
        {
            foreach (var column in sheet.AutoFilter!.FilterColumns.ToArray())
            {
                if (column.ColumnId < 0 || (uint)column.ColumnId >= filterRange.ColCount)
                    continue;

                // Only plain value filters carry a directly replayable allowed-value set; richer
                // criteria (custom/dynamic/color/top-10) are not reconstructable from the model here.
                if (column.Values.Count == 0)
                    continue;

                var result = _session.ExecuteReviewCommand(
                    new FilterCommand(sheetId, filterRange, (uint)column.ColumnId, column.Values));
                if (!result.Success)
                {
                    ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ReapplyFilterFailed"));
                    return;
                }

                applied++;
            }
        }

        if (TryGetSortReapplyPlan(sheet, out var sortRange, out var sortKeys))
        {
            var result = _session.ExecuteReviewCommand(new SortCommand(sheetId, sortRange, sortKeys));
            if (!result.Success)
            {
                ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ReapplySortFailed"));
                return;
            }

            applied++;
        }

        RefreshShell(applied == 0
            ? UiText.Get("TableLoc_NoReapplyableFilterOrSort")
            : UiText.Format(
                applied == 1 ? "TableLoc_ReapplyedDefinitionsOne" : "TableLoc_ReapplyedDefinitionsMany",
                applied));
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

    private static bool TryGetSortReapplyPlan(Sheet sheet, out GridRange range, out IReadOnlyList<SortKey> keys)
    {
        range = default;
        keys = [];
        if (sheet.SortState is not { } sortState ||
            sortState.Conditions.Count == 0 ||
            string.IsNullOrWhiteSpace(sortState.Reference))
        {
            return false;
        }

        GridRange sortRange;
        try
        {
            sortRange = GridRange.Parse(sortState.Reference, sheet.Id);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var sortKeys = new List<SortKey>();
        foreach (var condition in sortState.Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.Reference))
                continue;

            GridRange conditionRange;
            try
            {
                conditionRange = GridRange.Parse(condition.Reference, sheet.Id);
            }
            catch (FormatException)
            {
                continue;
            }
            catch (ArgumentException)
            {
                continue;
            }

            // The condition reference points at the sorted column; its offset within the sort range
            // is the SortKey column offset. Sort metadata persists Descending; SortKey wants ascending.
            if (conditionRange.Start.Col < sortRange.Start.Col)
                continue;

            var offset = conditionRange.Start.Col - sortRange.Start.Col;
            if (offset >= sortRange.ColCount)
                continue;

            sortKeys.Add(new SortKey(offset, condition.Descending != true));
        }

        if (sortKeys.Count == 0)
            return false;

        range = sortRange;
        keys = sortKeys;
        return true;
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

    // Called from BuildDrawingObjectOverlay so circles are painted onto the same overlay Canvas that hosts
    // charts / drawing objects / trace arrows, using the same coordinate mapping (TryGetDisplayedCellBounds).
    private void AddValidationCircleOverlay(Canvas overlay, ViewportModel viewport)
    {
        if (_validationCircleCells.Count == 0)
            return;

        var showHeadings = _session.ActiveSheet.ShowHeadings;
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
