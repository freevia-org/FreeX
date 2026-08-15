using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.Filtering;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── Data-tab tools: Reapply, Circle Invalid Data / Clear Validation Circles, Get Data, Refresh All ──
    //
    private readonly WorksheetFilterWorkflowSession _filterWorkflowSession = new();

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
        var plan = _filterWorkflowSession.CreateReapplyPlan(sheet);
        if (plan is null)
        {
            RefreshShell(UiText.Get("TableLoc_NoReapplyableFilterOrSort"));
            return;
        }

        var result = _session.ExecuteWorksheetFilterReapplyPlan(plan, "Reapply Filters");
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ReapplyFilterFailed"));
            return;
        }

        // Reapply is a visibility mutation rather than a cell edit. SUBTOTAL/AGGREGATE formulas
        // must nevertheless see the new hidden-row set immediately, matching WPF's filter routes.
        RecalculateAfterAutoFilterMutation();

        RefreshShell(UiText.Format(
            plan.DefinitionCount == 1 ? "TableLoc_ReapplyedDefinitionsOne" : "TableLoc_ReapplyedDefinitionsMany",
            plan.DefinitionCount));
    }

    // ── Circle Invalid Data / Clear Validation Circles ───────────────────────────

    private void CircleInvalidData()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var result = WorkbookValidationCircleWorkflow.CircleInvalidData(
            _session.Workbook,
            _session.ActiveSheet);
        if (result.FirstCell is { } firstCell)
            EnsureValidationCircleAddressVisible(firstCell);

        RefreshShell(result.Outcome == WorkbookValidationCircleOutcome.NoInvalidData
            ? UiText.Get("TableLoc_NoInvalidDataFound")
            : UiText.Format(
                result.Cells.Count == 1 ? "TableLoc_CircledInvalidCellsOne" : "TableLoc_CircledInvalidCellsMany",
                result.Cells.Count));
    }

    private void ClearValidationCircles()
    {
        var result = WorkbookValidationCircleWorkflow.Clear(_session.ActiveSheet);
        RefreshShell(UiText.Get(result.Outcome == WorkbookValidationCircleOutcome.NothingToClear
            ? "TableLoc_NoValidationCirclesToClear"
            : "TableLoc_ClearedValidationCircles"));
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
        WorkbookValidationCircleWorkflow.Prune(_session.Workbook, _session.ActiveSheet);
    }

    // Called from BuildDrawingObjectOverlay so circles are painted onto the same overlay Canvas that hosts
    // charts / drawing objects / trace arrows, using the same coordinate mapping (TryGetDisplayedCellBounds).
    private void AddValidationCircleOverlay(Canvas overlay, ViewportModel viewport)
    {
        PruneCorrectedValidationCircles();

        var validationCircleCells = _session.ActiveSheet.ValidationCircleCells;
        if (validationCircleCells is not { Count: > 0 })
            return;

        var showHeadings = _session.IsShowingHeadings;
        var zoomFactor = GetActiveZoomFactor();
        var activeSheetId = _session.ActiveSheet.Id;

        var bounds = new List<Rect>();
        foreach (var address in validationCircleCells)
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

    private void EnsureValidationCircleAddressVisible(CellAddress address)
    {
        var rowVisible = _session.Viewport.RowMetrics.Any(metric => metric.Row == address.Row);
        var columnVisible = _session.Viewport.ColMetrics.Any(metric => metric.Col == address.Col);
        if (rowVisible && columnVisible)
            return;

        var topRow = rowVisible ? _session.ActiveSheet.ViewTopRow ?? 1 : address.Row;
        var leftColumn = columnVisible ? _session.ActiveSheet.ViewLeftCol ?? 1 : address.Col;
        _session.SetViewportOrigin(topRow, leftColumn);
    }

    // ── Get Data / Refresh All ────────────────────────────────────────────────────
    //
    // Get Data ▸ From Text/CSV (file-based import) lives in MainWindow.GetData.cs and Refresh re-imports the
    // remembered file source. There is still no external DB/web/query/connection engine
    // (XlsxConnectionQueryTableSchemaNormalizer only round-trips connection XML for file fidelity; it
    // executes nothing), so those connector surfaces remain out of scope.

    private sealed class ValidationCircleControl : Control
    {
        private static readonly IPen CirclePen = new ImmutablePen(
            new ImmutableSolidColorBrush(Color.FromRgb(
                ValidationCircleLayoutPlanner.StrokeColor.R,
                ValidationCircleLayoutPlanner.StrokeColor.G,
                ValidationCircleLayoutPlanner.StrokeColor.B)),
            ValidationCircleLayoutPlanner.StrokeThickness);

        private readonly IReadOnlyList<Rect> _cellBounds;

        public ValidationCircleControl(IReadOnlyList<Rect> cellBounds)
        {
            _cellBounds = cellBounds;
        }

        public override void Render(DrawingContext context)
        {
            foreach (var cell in _cellBounds)
            {
                var ellipse = ValidationCircleLayoutPlanner.CalculateEllipseBounds(
                    new LayoutRect(cell.X, cell.Y, cell.Width, cell.Height));
                var center = new Point(
                    ellipse.Left + (ellipse.Width / 2.0),
                    ellipse.Top + (ellipse.Height / 2.0));
                context.DrawEllipse(
                    null,
                    CirclePen,
                    center,
                    ellipse.Width / 2.0,
                    ellipse.Height / 2.0);
            }
        }
    }
}
