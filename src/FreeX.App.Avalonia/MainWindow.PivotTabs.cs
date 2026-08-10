using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real handlers for the PivotTable Analyze / Design contextual tabs (activation key <c>pivot.active</c>).
/// Every command here operates on the active PivotTable resolved through
/// <see cref="PivotSourceContext.FindActivePivot"/> (falling back to <see cref="ResolveInsertControlPivot"/>
/// when the selection has drifted off the report, matching the Insert ▸ Slicer/Timeline behavior). Commands
/// that Core genuinely supports are wired end-to-end through the shared review-command path
/// (<see cref="WorkbookSession.ExecuteReviewCommand"/>): Refresh (<see cref="RefreshPivotTableCommand"/>),
/// Insert Slicer / Insert Timeline (reusing the existing <see cref="ShowInsertSlicerDialogAsync"/> /
/// <see cref="ShowInsertTimelineDialogAsync"/> pickers), the Design layout commands (Grand Totals, Subtotals,
/// Report Layout, Blank Rows) and the Design style-option toggles (Banded Rows/Columns, Row/Column Headers,
/// Field Headers) — all of which round-trip through <see cref="ConfigurePivotTableOptionsCommand"/>. The
/// Field List button toggles the shell's pivot field pane. Commands Core does not yet model (PivotTable
/// Name/Options dialog, Field Settings, Group/Ungroup field, Change Data Source, calculated field/item,
/// PivotChart, etc.) report an honest "not yet available" status rather than no-op.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Recomputes the PivotTable contextual-tab state from the active cell. This mirrors the WPF
    /// viewport refresh, which reevaluates pivot context whenever selection/navigation changes.
    /// </summary>
    private void RefreshPivotContextualTab()
    {
        RefreshPivotFieldPane();
        _ribbonContextSource.OnPivotActive(
            _selectedDrawingObjectKind is null &&
            PivotSourceContext.FindActivePivot(_session.ActiveSheet, _session.ActiveCell) is not null);
    }

    /// <summary>
    /// When true the user has explicitly closed the PivotTable field pane via the Analyze ▸ Field List
    /// toggle, so the pane stays hidden even when the active cell is inside a pivot. Cleared when the user
    /// toggles it back on. <see cref="RefreshPivotFieldPane"/> honors this flag (see wiring note in the
    /// deliverables) so the choice survives selection moves.
    /// </summary>
    private bool _pivotFieldPaneUserHidden;

    // ── Analyze ▸ Data ─────────────────────────────────────────────────────────

    /// <summary>Refresh PivotTable — re-materializes the active pivot from its cache via the Core command.</summary>
    private void RefreshActivePivotTable()
    {
        if (!TryResolvePivotApplicationTarget(
                out var target,
                missingMessage: UiText.Get("PivotLoc_SelectCellToRefresh")))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanRefresh(target));
    }

    // ── Analyze ▸ Filter (reuse the existing Insert Slicer/Timeline pickers) ─────

    /// <summary>Insert Slicer — reuses the shared Insert-tab slicer field picker for the active pivot.</summary>
    private void InsertSlicerForActivePivot() => InsertSlicer();

    /// <summary>Insert Timeline — reuses the shared Insert-tab timeline field picker for the active pivot.</summary>
    private void InsertTimelineForActivePivot() => InsertTimeline();

    // ── Analyze ▸ Show ───────────────────────────────────────────────────────────

    /// <summary>
    /// Field List — toggles the PivotTable field pane. Only meaningful when a pivot is active; when the cell
    /// is not inside a pivot the pane is hidden anyway and the command explains that.
    /// </summary>
    private void TogglePivotFieldList()
    {
        var pivot = PivotSourceContext.FindActivePivot(_session.ActiveSheet, _session.ActiveCell);
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotLoc_SelectCellToShowFieldList"));
            return;
        }

        _pivotFieldPaneUserHidden = !_pivotFieldPaneUserHidden;
        RefreshPivotFieldPane();
        RefreshShell(_pivotFieldPaneUserHidden ? UiText.Get("PivotLoc_FieldListHidden") : UiText.Get("PivotLoc_FieldListShown"));
    }

    /// <summary>Field Headers — toggles <see cref="PivotTableModel.ShowFieldHeaders"/> on the active pivot.</summary>
    private void TogglePivotFieldHeaders()
        => ApplyPivotOption(
            pivot => !pivot.ShowFieldHeaders,
            (pivot, value) => pivot with { ShowFieldHeaders = value },
            value => value ? UiText.Get("PivotLoc_FieldHeadersShown") : UiText.Get("PivotLoc_FieldHeadersHidden"));

    // ── Design ▸ Layout ──────────────────────────────────────────────────────────

    /// <summary>Grand Totals — toggles both row and column grand totals on the active pivot.</summary>
    private void TogglePivotGrandTotals()
        => ApplyPivotOption(
            pivot => !(pivot.ShowRowGrandTotals || pivot.ShowColumnGrandTotals),
            (pivot, value) => pivot with { ShowRowGrandTotals = value, ShowColumnGrandTotals = value },
            value => value ? UiText.Get("PivotLoc_GrandTotalsOn") : UiText.Get("PivotLoc_GrandTotalsOff"));

    /// <summary>Subtotals — toggles <see cref="PivotTableModel.ShowSubtotals"/> on the active pivot.</summary>
    private void TogglePivotSubtotals()
        => ApplyPivotOption(
            pivot => !pivot.ShowSubtotals,
            (pivot, value) => pivot with { ShowSubtotals = value },
            value => value ? UiText.Get("PivotLoc_SubtotalsShown") : UiText.Get("PivotLoc_SubtotalsHidden"));

    /// <summary>Blank Rows — toggles <see cref="PivotTableModel.BlankLineAfterItems"/> on the active pivot.</summary>
    private void TogglePivotBlankRows()
        => ApplyPivotOption(
            pivot => !pivot.BlankLineAfterItems,
            (pivot, value) => pivot with { BlankLineAfterItems = value },
            value => value ? UiText.Get("PivotLoc_BlankRowInserted") : UiText.Get("PivotLoc_BlankRowsRemoved"));

    /// <summary>Report Layout — cycles Compact → Outline → Tabular on the active pivot.</summary>
    private void CyclePivotReportLayout()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var next = pivot!.ReportLayout switch
        {
            PivotReportLayout.Compact => PivotReportLayout.Outline,
            PivotReportLayout.Outline => PivotReportLayout.Tabular,
            _ => PivotReportLayout.Compact,
        };

        var options = CapturePivotOptions(pivot) with { ReportLayout = next };
        ExecutePivotTabCommand(
            BuildPivotOptionsCommand(pivot, options),
            UiText.Format("PivotLoc_ReportLayoutStatus", PivotOptionsPlanner.GetReportLayoutLabel(next)));
    }

    // ── Design ▸ Style Options ──────────────────────────────────────────────────

    /// <summary>Banded Rows — toggles <see cref="PivotTableModel.ShowRowStripes"/> on the active pivot.</summary>
    private void TogglePivotBandedRows()
        => ApplyPivotOption(
            pivot => !pivot.ShowRowStripes,
            (pivot, value) => pivot with { ShowRowStripes = value },
            value => value ? UiText.Get("PivotLoc_BandedRowsOn") : UiText.Get("PivotLoc_BandedRowsOff"));

    /// <summary>Banded Columns — toggles <see cref="PivotTableModel.ShowColumnStripes"/> on the active pivot.</summary>
    private void TogglePivotBandedColumns()
        => ApplyPivotOption(
            pivot => !pivot.ShowColumnStripes,
            (pivot, value) => pivot with { ShowColumnStripes = value },
            value => value ? UiText.Get("PivotLoc_BandedColumnsOn") : UiText.Get("PivotLoc_BandedColumnsOff"));

    /// <summary>Row Headers — toggles <see cref="PivotTableModel.ShowRowHeaders"/> on the active pivot.</summary>
    private void TogglePivotRowHeaders()
        => ApplyPivotOption(
            pivot => !pivot.ShowRowHeaders,
            (pivot, value) => pivot with { ShowRowHeaders = value },
            value => value ? UiText.Get("PivotLoc_RowHeadersShown") : UiText.Get("PivotLoc_RowHeadersHidden"));

    /// <summary>Column Headers — toggles <see cref="PivotTableModel.ShowColumnHeaders"/> on the active pivot.</summary>
    private void TogglePivotColumnHeaders()
        => ApplyPivotOption(
            pivot => !pivot.ShowColumnHeaders,
            (pivot, value) => pivot with { ShowColumnHeaders = value },
            value => value ? UiText.Get("PivotLoc_ColumnHeadersShown") : UiText.Get("PivotLoc_ColumnHeadersHidden"));

    // ── Shared option-mutation plumbing ──────────────────────────────────────────

    /// <summary>
    /// Toggles a single boolean PivotTable option: computes the new value via <paramref name="nextValue"/>,
    /// applies it onto a captured snapshot via <paramref name="mutate"/>, then runs the resulting
    /// <see cref="ConfigurePivotTableOptionsCommand"/> and reports the outcome via <paramref name="status"/>.
    /// </summary>
    private void ApplyPivotOption(
        Func<PivotTableModel, bool> nextValue,
        Func<PivotOptionValues, bool, PivotOptionValues> mutate,
        Func<bool, string> status)
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var value = nextValue(pivot!);
        var options = mutate(CapturePivotOptions(pivot!), value);
        ExecutePivotTabCommand(BuildPivotOptionsCommand(pivot!, options), status(value));
    }

    /// <summary>
    /// Guards a Design/Show option command: rejects while opening/saving or with a pending edit, and resolves
    /// the active pivot (status-reporting when none). Returns false when the command should not proceed.
    /// </summary>
    private bool TryBeginPivotOption(out PivotTableModel? pivot)
    {
        pivot = null;
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return false;

        pivot = ResolveInsertControlPivot();
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotLoc_SelectCellToChangeLayout"));
            return false;
        }

        return true;
    }

    /// <summary>Snapshots the current option flags so a single toggle preserves everything else verbatim.</summary>
    private static PivotOptionValues CapturePivotOptions(PivotTableModel pivot) => new(
        ShowRowGrandTotals: pivot.ShowRowGrandTotals,
        ShowColumnGrandTotals: pivot.ShowColumnGrandTotals,
        ShowSubtotals: pivot.ShowSubtotals,
        SubtotalPlacement: pivot.SubtotalPlacement,
        RepeatItemLabels: pivot.RepeatItemLabels,
        BlankLineAfterItems: pivot.BlankLineAfterItems,
        StyleName: pivot.StyleName,
        ReportLayout: pivot.ReportLayout,
        ShowRowHeaders: pivot.ShowRowHeaders,
        ShowColumnHeaders: pivot.ShowColumnHeaders,
        ShowRowStripes: pivot.ShowRowStripes,
        ShowColumnStripes: pivot.ShowColumnStripes,
        ShowFieldHeaders: pivot.ShowFieldHeaders);

    /// <summary>
    /// Builds a <see cref="ConfigurePivotTableOptionsCommand"/> for the active pivot from a (possibly mutated)
    /// snapshot. Only the layout/style flags this tab exposes are carried; the command leaves all other
    /// (cache/print/alt-text/tooltip) options untouched by passing their "no update" defaults.
    /// </summary>
    private ConfigurePivotTableOptionsCommand BuildPivotOptionsCommand(
        PivotTableModel pivot,
        PivotOptionValues o,
        bool? showExpandCollapseButtons = null)
        => new(
            _session.ActiveSheet.Id,
            pivot.Name,
            showRowGrandTotals: o.ShowRowGrandTotals,
            showColumnGrandTotals: o.ShowColumnGrandTotals,
            showSubtotals: o.ShowSubtotals,
            subtotalPlacement: o.SubtotalPlacement,
            repeatItemLabels: o.RepeatItemLabels,
            blankLineAfterItems: o.BlankLineAfterItems,
            styleName: o.StyleName,
            showRowHeaders: o.ShowRowHeaders,
            showColumnHeaders: o.ShowColumnHeaders,
            showRowStripes: o.ShowRowStripes,
            showColumnStripes: o.ShowColumnStripes,
            reportLayout: o.ReportLayout,
            showFieldHeaders: o.ShowFieldHeaders,
            showExpandCollapseButtons: showExpandCollapseButtons);

    /// <summary>
    /// Runs a pivot command through the shared review path, surfacing failures on the status bar and forcing
    /// a field-pane rebuild on success (the layout/identity signature may not move for a pure option change).
    /// </summary>
    private void ExecutePivotTabCommand(IWorkbookCommand command, string successStatus)
    {
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("PivotLoc_UpdateFailed"));
            return;
        }

        _pivotPaneSignature = null;
        RefreshShell(successStatus);
    }

    /// <summary>Reports that a PivotTable contextual command is not yet backed by Core.</summary>
    private void ReportPivotNotYetAvailable(string commandLabel)
        => RefreshShell(UiText.Format("PivotLoc_NotYetAvailable", commandLabel));

    /// <summary>Carrier for the layout/style flags this contextual tab can mutate (so one toggle keeps the rest).</summary>
    private readonly record struct PivotOptionValues(
        bool ShowRowGrandTotals,
        bool ShowColumnGrandTotals,
        bool ShowSubtotals,
        PivotSubtotalPlacement SubtotalPlacement,
        bool RepeatItemLabels,
        bool BlankLineAfterItems,
        string StyleName,
        PivotReportLayout ReportLayout,
        bool ShowRowHeaders,
        bool ShowColumnHeaders,
        bool ShowRowStripes,
        bool ShowColumnStripes,
        bool ShowFieldHeaders);
}
