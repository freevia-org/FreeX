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
///   <item><c>view.pageLayoutView</c> — best-effort "Page Layout" view. A full paginated/WYSIWYG page
///   layout (rendering each printed page with margins and rulers) is out of scope for this shell, which
///   has no paginated canvas. Instead, selecting Page Layout turns ON the existing page-break overlay
///   (page boundaries, out-of-print-area masks, automatic break lines, and "Page N" watermarks) so the
///   user sees where pages fall. This reuses <c>_isPageBreakPreviewActive</c> and the overlay built by
///   <c>BuildPageBreakPreviewOverlay</c>. This is NOT Excel page-layout fidelity — it is the page-break
///   visualization, surfaced under the Page Layout button. <c>view.normal</c> (already wired) clears it.</item>
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

        _isFormulaBarHidden = !_isFormulaBarHidden;
        var visible = !_isFormulaBarHidden;

        _formulaBarHost.IsVisible = visible;
        _formulaBox.IsVisible = visible;
        _cellAddressText.IsVisible = visible;

        RefreshShell(visible ? UiText.Get("ShellLoc_ShowingFormulaBar") : UiText.Get("ShellLoc_HidingFormulaBar"));
    }

    /// <summary>
    /// View ▸ Workbook Views ▸ Page Layout — best-effort page-layout view.
    ///
    /// This shell has no paginated WYSIWYG canvas, so "Page Layout" turns ON the page-break overlay
    /// (the same visualization as Page Break Preview) to show where pages fall. It is not Excel-grade
    /// page-layout fidelity. Use Normal (view.normal) to return to the unannotated grid.
    /// </summary>
    private void SetPageLayoutView()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        if (!_isPageBreakPreviewActive)
            _isPageBreakPreviewActive = true;

        RefreshShell(UiText.Get("ShellLoc_PageLayoutView"));
    }
}
