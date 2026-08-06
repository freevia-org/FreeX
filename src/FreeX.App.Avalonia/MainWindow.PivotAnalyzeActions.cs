using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using System.Diagnostics;

namespace FreeX.App.Avalonia;

/// <summary>
/// Real handlers for the lower-effort PivotTable Analyze contextual-tab commands that reuse existing Core
/// commands without a new dialog: Field Settings (the value-field dialog targeting the active pivot's first
/// value field), Show Details (<see cref="DrillDownPivotTableCommand"/>), Clear
/// (<see cref="ClearPivotTableViewCommand"/>), Select (move the selection onto the pivot's target range), and
/// the +/- Buttons display toggle (<see cref="ConfigurePivotTableOptionsCommand"/>'s
/// <c>showExpandCollapseButtons</c>). Each resolves the active pivot through
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
        if (!TryBeginPivotOption(out var pivot))
            return;

        if (pivot!.DataFields.Count == 0)
        {
            RefreshShell(UiText.Get("PivotAnalyze_FieldSettingsNoValueField"));
            return;
        }

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
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

    /// <summary>Clear — empties the active pivot's rendered layout via <see cref="ClearPivotTableViewCommand"/>.</summary>
    private void ClearActivePivotTable()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        ExecutePivotTabCommand(
            new ClearPivotTableViewCommand(_session.ActiveSheet.Id, pivot!.Name),
            UiText.Format("PivotAnalyze_Cleared", pivot.Name));
    }

    /// <summary>Select — moves the selection onto the active pivot's full target range.</summary>
    private void SelectActivePivotTable()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var source = pivot!.LastRenderedRange ?? pivot.TargetRange;
        // Re-anchor onto the active sheet id: a loaded pivot range may carry a placeholder sheet id.
        var sheetId = _session.ActiveSheet.Id;
        var range = new GridRange(
            new CellAddress(sheetId, source.Start.Row, source.Start.Col),
            new CellAddress(sheetId, source.End.Row, source.End.Col));
        _session.SelectRange(range);
        _pivotPaneSignature = null;
        RefreshShell(UiText.Format("PivotAnalyze_Selected", pivot.Name));
    }

    // ── Analyze ▸ Active Field ▸ Show Details ────────────────────────────────────

    /// <summary>
    /// Show Details — drills the active cell into a new detail worksheet via
    /// <see cref="DrillDownPivotTableCommand"/>. The command adds the detail sheet and returns its anchor, so
    /// the shared review path (<see cref="ExecutePivotTabCommand"/>) switches to it automatically.
    /// </summary>
    private void ShowActivePivotDetails()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var pivot = PivotSourceContext.FindActivePivot(_session.ActiveSheet, _session.ActiveCell);
        if (pivot is null)
        {
            RefreshShell(UiText.Get("PivotAnalyze_ShowDetailsPrompt"));
            return;
        }

        ExecutePivotTabCommand(
            new DrillDownPivotTableCommand(_session.ActiveSheet.Id, pivot.Name, _session.ActiveCell),
            UiText.Format("PivotAnalyze_ShowDetailsDone", pivot.Name));
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

        var target = PivotUiPlanner.ResolveShowDetailsTarget(_session.ActiveSheet, _session.SelectedRange);
        if (target is null)
            return false;

        var result = _session.ExecuteReviewCommand(
            new DrillDownPivotTableCommand(_session.ActiveSheet.Id, target.PivotTableName, target.PivotCell));
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("PivotLoc_UpdateFailed"));
            return false;
        }

        if (pointerAddress is { } address)
        {
            _pivotDetailsDoubleClickHandledAddress = address;
            _pivotDetailsDoubleClickHandledTimestamp = Stopwatch.GetTimestamp();
        }

        _pivotPaneSignature = null;
        RefreshShell(UiText.Format("PivotAnalyze_ShowDetailsDone", target.PivotTableName));
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
    /// +/- Buttons — toggles <see cref="PivotTableModel.ShowExpandCollapseButtons"/> via
    /// <see cref="ConfigurePivotTableOptionsCommand"/> (carrying the layout/style flags untouched).
    /// </summary>
    private void TogglePivotExpandCollapseButtons()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var value = !pivot!.ShowExpandCollapseButtons;
        // Carry the layout/style snapshot untouched (BuildPivotOptionsCommand) and add the expand/collapse flag.
        var command = BuildPivotOptionsCommand(pivot, CapturePivotOptions(pivot), showExpandCollapseButtons: value);
        ExecutePivotTabCommand(
            command,
            value ? UiText.Get("PivotAnalyze_PlusMinusOn") : UiText.Get("PivotAnalyze_PlusMinusOff"));
    }
}
