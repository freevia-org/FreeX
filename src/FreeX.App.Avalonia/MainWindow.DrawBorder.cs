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
    private BorderDrawMode _borderDrawMode = BorderDrawMode.None;
    private BorderStyle _borderDrawStyle = BorderStyle.Thin;
    private CellColor _borderDrawColor = CellColor.Black;

    private bool IsBorderDrawModeActive => _borderDrawMode != BorderDrawMode.None;

    private void BeginBorderDrawMode(BorderDrawMode mode)
    {
        if (mode == BorderDrawMode.None)
            throw new ArgumentException("Border draw mode must be active.", nameof(mode));

        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        if (_session.IsFormatPainterActive)
            CancelFormatPainter();

        _borderDrawMode = mode;
        RefreshShell($"{BorderDrawPlanner.CommandTitle(mode)} — drag across cells; Esc cancels.");
    }

    internal void CancelBorderDrawMode()
    {
        if (!IsBorderDrawModeActive)
            return;

        _borderDrawMode = BorderDrawMode.None;
        RefreshShell(UiText.Get("DrawBorder_ModeCancelledStatus"));
    }

    internal void ApplyBorderDrawMode()
    {
        if (!IsBorderDrawModeActive)
            return;

        var mode = _borderDrawMode;
        _borderDrawMode = BorderDrawMode.None;

        var rangeReference = FormatRangeReference(_session.SelectedRange);
        var result = _session.SetSelectedRangeDrawBorder(mode, _borderDrawStyle, _borderDrawColor);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("MainLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("DrawBorder_FailedMessage"));
            return;
        }

        RefreshShell(UiText.Format("DrawBorder_AppliedStatusFormat", rangeReference));
    }

    private void SetBorderDrawStyle(BorderStyle style) =>
        _borderDrawStyle = style;

    private void SetBorderDrawColor(CellColor color) =>
        _borderDrawColor = color;
}
