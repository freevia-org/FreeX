using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity View tab toggles for the Avalonia shell.
///
/// This partial wires the View tab commands that were previously no-ops:
/// <list type="bullet">
///   <item><c>view.formulaBar</c> — show/hide the formula bar input (and its Name Box / cell-address
///   readout). This flips <see cref="Avalonia.Controls.Control.IsVisible"/> on the existing
///   <c>_formulaBox</c> TextBox and <c>_cellAddressText</c> TextBlock, so it is a genuine rendering
///   change, not a status message.</item>
///   <item><c>view.pageLayoutView</c> — show page boundaries and optional margin rulers over the editable
///   worksheet grid.</item>
/// </list>
///
/// Gridlines, Headings, Page Break Preview, Normal, and Split are already wired centrally (to
/// <c>ToggleShowGridlines</c>, <c>ToggleShowHeadings</c>, <c>TogglePageBreakPreview</c>,
/// <c>SetNormalView</c>, <c>SplitPanesAtActiveCell</c>) and are intentionally NOT touched here.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Backing flag for the formula-bar visibility toggle. Defaults to visible (matches the initial
    /// layout, where <c>_formulaBox</c> and <c>_cellAddressText</c> are shown).
    /// </summary>
    private bool _isFormulaBarHidden;

    /// <summary>
    /// View ▸ Show ▸ Formula Bar — show/hide the formula bar input and the cell-address (Name Box) readout.
    /// </summary>
    private void ToggleFormulaBarVisibility()
    {
        if (_isOpening || _isSaving)
            return;

        // Don't lose an in-progress edit when the user hides the bar.
        if (!TryCommitPendingFormulaEdit())
            return;

        var visible = _isFormulaBarHidden;
        ApplyFormulaBarVisibility(visible);

        RefreshShell(visible ? UiText.Get("ShellLoc_ShowingFormulaBar") : UiText.Get("ShellLoc_HidingFormulaBar"));
    }

    /// <summary>
    /// The one place the formula bar row is shown or hidden, shared by View ▸ Show ▸ Formula Bar and
    /// File ▸ Options ▸ Show formula bar (WPF collapses a single FormulaBarBorder from both). Options
    /// used to flip only the input and Name Box, so after View hid the whole row, re-enabling the bar
    /// in Options reported it visible while the row stayed collapsed.
    /// </summary>
    private void ApplyFormulaBarVisibility(bool visible)
    {
        _isFormulaBarHidden = !visible;
        _formulaBarHost.IsVisible = visible;
        _formulaBox.IsVisible = visible;
        _cellAddressText.IsVisible = visible;
    }

    /// <summary>
    /// View ▸ Workbook Views ▸ Page Layout — outlines printed pages and shows enabled rulers.
    /// </summary>
    private void SetPageLayoutView()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var result = _session.SetWorksheetViewMode(WorksheetViewMode.PageLayout);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("ShellLoc_PageLayoutView"));
            return;
        }

        RefreshShell(UiText.Get("ShellLoc_PageLayoutView"));
    }
}
