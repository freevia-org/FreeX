using Avalonia.Input;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Draw Border pen mode — interactive border-drawing by click/drag over cells.
///
/// Mirrors the WPF shell's border-draw mode (MainWindow.HomeFormatting.cs / MainWindow.Selection.cs):
/// while active, pointer-down on any cell begins a drag-selection; pointer-up applies the outline
/// border (only the boundary edges of the selected range receive a border line, matching WPF's
/// <c>BorderDrawMode.Draw</c> / <c>BorderShortcutService.GetOutlineBorderDiff</c>).  Escape cancels.
///
/// Unlike Format Painter (which lives in WorkbookSession), this is pure shell-side toggle state:
/// the mode flag is stored here; the actual apply call goes to
/// <see cref="FreeX.App.Services.WorkbookSession.SetSelectedRangeDrawBorder"/>.
/// </summary>
public partial class MainWindow
{
    // ── Mode state ────────────────────────────────────────────────────────────

    /// <summary>Whether the Draw Border pen mode is currently active.</summary>
    private bool _borderDrawModeActive;

    // ── Activation / cancellation ─────────────────────────────────────────────

    /// <summary>Activates Draw Border pen mode (toggling off if already active).</summary>
    private void BeginBorderDrawMode()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        // Toggle: a second click on the toolbar button exits the mode.
        if (_borderDrawModeActive)
        {
            CancelBorderDrawMode();
            return;
        }

        _borderDrawModeActive = true;
        RefreshShell(UiText.Get("DrawBorder_ModeActiveStatus"));
    }

    /// <summary>Cancels Draw Border mode without applying any border.</summary>
    internal void CancelBorderDrawMode()
    {
        if (!_borderDrawModeActive)
            return;

        _borderDrawModeActive = false;
        RefreshShell(UiText.Get("DrawBorder_ModeCancelledStatus"));
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the drag-select ends while in Draw Border mode.
    /// Applies the outline border to the current selected range and exits the mode.
    /// </summary>
    internal void ApplyBorderDrawMode()
    {
        if (!_borderDrawModeActive)
            return;

        _borderDrawModeActive = false;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeDrawBorder();
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("DrawBorder_FailedMessage"));
            return;
        }

        RefreshShell(UiText.Format("DrawBorder_AppliedStatusFormat", rangeReference));
    }
}
