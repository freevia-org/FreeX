using Avalonia.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Interactive border drawing by click/drag over cells.
///
/// Mirrors the WPF shell's border-draw mode (MainWindow.HomeFormatting.cs / MainWindow.Selection.cs):
/// while active, pointer-down begins a drag-selection and pointer-up applies the selected mode,
/// line style, and color through
/// <see cref="FreeX.App.Services.WorkbookSession.SetSelectedRangeDrawBorder"/>.
/// </summary>
public partial class MainWindow
{
    private readonly BorderPickerSession _borderPickerSession = new();

    private bool IsBorderDrawModeActive => _borderPickerSession.IsDrawModeActive;

    private void BeginBorderDrawMode(BorderDrawMode mode)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsFormatPainterActive)
            CancelFormatPainter();

        _borderPickerSession.BeginDrawMode(mode);
        RefreshShell($"{BorderDrawPlanner.CommandTitle(mode)} — drag across cells; Esc cancels.");
    }

    internal void CancelBorderDrawMode()
    {
        if (!IsBorderDrawModeActive)
            return;

        _borderPickerSession.CancelDrawMode();
        RefreshShell(UiText.Get("DrawBorder_ModeCancelledStatus"));
    }

    internal void ApplyBorderDrawMode()
    {
        if (!_borderPickerSession.TryConsumeDrawPlan(out var plan))
            return;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeDrawBorder(plan.Mode, plan.Style, plan.Color);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("DrawBorder_FailedMessage"));
            return;
        }

        RefreshShell(UiText.Format("DrawBorder_AppliedStatusFormat", rangeReference));
    }
}
