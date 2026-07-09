using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression test for R17-form-controls-linkedcell-1.
///
/// MainWindow.Viewport.cs's viewport-refresh hook (UpdateViewport) called
/// FormControlListResolver.PopulateSelectedText but never
/// FormControlInteractionService.SyncControlsFromLinkedCells, even though the Avalonia shell calls
/// both from its own per-refresh hook (MainWindow.FormControls.cs). As a result, a WPF
/// checkbox/spinner/scrollbar/list-box never re-derived its state when its linked cell changed via
/// a direct edit or a formula recalculation -- only a click on the control itself updated it.
///
/// This is a source-contract check: the fix is a one-line wiring in the object-data refresh path,
/// and the sync LOGIC itself (checkbox from a bool cell, spinner clamp, etc.) is covered directly
/// by the FormControlInteractionService tests. A headless STA MainWindow.UpdateViewport does not
/// reliably resolve its active sheet/viewport, so the wiring is verified by source inspection here,
/// matching how the codebase pins the rest of the shell's command wiring.
/// </summary>
public sealed class R17_formctrl_Tests
{
    [Fact]
    public void UpdateViewport_SyncsFormControlsFromLinkedCells_MatchingAvaloniaShell()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        // The WPF viewport refresh must now re-derive each control's state from its linked cell,
        // inside the object-data / FormControls block, mirroring the Avalonia shell.
        viewportSource.Should().Contain("sheet.FormControls.Count > 0");
        viewportSource.Should().Contain(
            "FormControlInteractionService.SyncControlsFromLinkedCells(sheet, _workbook)");
    }
}
