using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using System.Diagnostics;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real handlers for the lower-effort PivotTable Analyze contextual-tab commands that reuse existing Core
/// commands without a new dialog: Field Settings (the value-field dialog targeting the active pivot's first
/// value field), Show Details, Clear, Select (move the selection onto the pivot's target range), and the +/-
/// Buttons display toggle. Each resolves the active pivot through
/// <see cref="MainWindow.ResolveInsertControlPivot"/> (the same fallback the other Analyze handlers use) and
/// reports an honest status when no pivot/value cell applies, mirroring the WPF host's
/// PivotTable{Clear,Select,ShowDetails,Field}Btn handlers.
/// </summary>
public sealed partial class MainWindow
{
    // ── Analyze ▸ Active Field ▸ Field Settings ──────────────────────────────────

    /// <summary>
    /// Field Settings — opens the Value Field Settings dialog for the active pivot's first value (data) field.
    /// The ribbon button has no per-field selection, so it targets the first value field (Excel falls back the
    /// same way when the cursor is not on a specific field). Reuses the header-dropdown dialog verbatim by
    /// synthesizing the corresponding <see cref="PivotHeaderDropdownTargetModel"/>.
    /// </summary>
    private void OpenActivePivotFieldSettings()
    {
        if (!TryResolvePivotApplicationTarget(out var applicationTarget))
            return;

        var pivot = applicationTarget.PivotTable;
        if (pivot.DataFields.Count == 0)
        {
            RefreshShell(UiText.Get("PivotAnalyze_FieldSettingsNoValueField"));
            return;
        }

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot));
        var field = pivot.DataFields[0];
        var caption = string.IsNullOrWhiteSpace(field.Name)
            ? PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex)
            : field.Name;

        var target = new PivotHeaderDropdownTargetModel(
            pivot.Name,
            caption,
            field.SourceFieldIndex,
            PivotHeaderArea.Value,
            IsActive: false,
            DataFieldIndex: 0);

        _ = TryOpenPivotFieldSettings(pivot, headers, target, PivotHeaderMenuAction.ValueFieldSettings);
    }

    // ── Analyze ▸ Actions ▸ Clear / Select ───────────────────────────────────────

    /// <summary>Clear — empties the active pivot's rendered layout through the shared application session.</summary>
    private void ClearActivePivotTable()
    {
        if (!TryResolvePivotApplicationTarget(out var target))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanClear(target));
    }

    /// <summary>Select — moves the selection onto the active pivot's full target range.</summary>
    private void SelectActivePivotTable()
    {
        if (!TryResolvePivotApplicationTarget(out var target))
            return;

        ApplyPivotApplicationPlan(PivotApplication.PlanSelect(target));
    }

    // ── Analyze ▸ Active Field ▸ Show Details ────────────────────────────────────

    /// <summary>
    /// Show Details — drills the active cell into a new detail worksheet. The command behind the shared plan
    /// adds the detail sheet and returns its anchor, so
    /// the shared application session switches to it automatically.
    /// </summary>
    private void ShowActivePivotDetails()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var plan = PivotApplication.PlanShowDetails(
            _session.ActiveSheet.Id,
            _session.SelectedRange);
        if (!plan.CanApply)
        {
            RefreshShell(UiText.Get("PivotAnalyze_ShowDetailsPrompt"));
            return;
        }

        ApplyPivotApplicationPlan(plan);
    }

    /// <summary>
    /// Mirrors the WPF grid's double-click precedence: a value cell inside a PivotTable drills into a
    /// new detail worksheet before the ordinary inline-cell editor is opened. Returns false when the
    /// active selection is not a PivotTable cell so the caller can continue into inline editing.
    /// </summary>
    private bool TryShowPivotTableDetailsFromDoubleClick(CellAddress? pointerAddress = null)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return false;

        var plan = PivotApplication.PlanShowDetails(
            _session.ActiveSheet.Id,
            _session.SelectedRange);
        if (!plan.CanApply)
            return false;

        var outcome = PivotApplication.Execute(plan);
        if (!outcome.Success)
        {
            RefreshShell(outcome.Message?.Detail ?? UiText.Get("PivotLoc_UpdateFailed"));
            return false;
        }

        if (pointerAddress is { } address)
        {
            _pivotDetailsDoubleClickHandledAddress = address;
            _pivotDetailsDoubleClickHandledTimestamp = Stopwatch.GetTimestamp();
        }

        ApplyPivotApplicationOutcome(outcome);
        return true;
    }

    private bool ConsumePivotDetailsDoubleClickSuppression(CellAddress address)
    {
        if (_pivotDetailsDoubleClickHandledAddress != address)
            return false;

        var elapsed = Stopwatch.GetElapsedTime(_pivotDetailsDoubleClickHandledTimestamp);
        _pivotDetailsDoubleClickHandledAddress = null;
        _pivotDetailsDoubleClickHandledTimestamp = 0;
        return elapsed <= TimeSpan.FromMilliseconds(500);
    }

    /// <summary>Test seam for the real WPF-parity double-click precedence route.</summary>
    internal bool TryShowPivotTableDetailsFromDoubleClickForTest() =>
        TryShowPivotTableDetailsFromDoubleClick();

    // ── Analyze ▸ Show ▸ +/- Buttons ─────────────────────────────────────────────

    /// <summary>
    /// +/- Buttons — toggles <see cref="PivotTableModel.ShowExpandCollapseButtons"/> through a shared
    /// design-options plan while carrying the layout/style flags untouched.
    /// </summary>
    private void TogglePivotExpandCollapseButtons()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var value = !pivot!.ShowExpandCollapseButtons;
        ApplyPivotApplicationPlan(
            PlanPivotDesignOptions(
                pivot,
                PivotOptionsPlanner.CaptureDesignValues(pivot),
                showExpandCollapseButtons: value),
            value ? UiText.Get("PivotAnalyze_PlusMinusOn") : UiText.Get("PivotAnalyze_PlusMinusOff"));
    }
}
