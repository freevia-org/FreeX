using System.Reflection;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R89-show-formulas-per-window-1: Show Formulas (Ctrl+`) was the last
/// remaining WPF-host View-tab toggle that still read/wrote the shared <see cref="Sheet.ShowFormulas"/>
/// field directly for its on-screen cell text -- unlike ViewMode/Zoom (R83), Gridlines/Headings/
/// Rulers (R87), and Freeze Panes/Window &gt; Split (R89-freeze-split-per-window-1), toggling it in
/// one Excel "View &gt; New Window" sibling instantly leaked the formula-vs-value display into every
/// other window viewing the same document, because <c>ViewportService.GetDisplayText</c> baked
/// <see cref="Sheet.ShowFormulas"/> straight into the rendered cell text with no override hook on
/// <c>ViewportRequest</c> at all.
///
/// The fix adds a <c>ShowFormulasOverride</c> to <c>ViewportRequest</c> (mirroring
/// <c>FrozenRowsOverride</c>/<c>SplitOverride</c>) that <c>GetDisplayText</c> consults instead of
/// unconditionally reading <see cref="Sheet.ShowFormulas"/>, extends <c>WorksheetViewStateSnapshot</c>
/// with a <c>ShowFormulas</c> field, and routes the WPF host's <c>CreateViewport</c>/
/// <c>ShowFormulasBtn_Click</c> through the existing per-window <c>WorksheetViewStateStore</c>/
/// <c>GetEffectiveViewState</c>/<c>SyncWindowViewState</c> mechanism.
///
/// These tests simulate two "New Window" siblings viewing the very same <see cref="Workbook"/>/
/// <see cref="Sheet"/> object graph, the same way R89_FreezeSplitPerWindowTests does: construct two
/// independent <see cref="MainWindow"/> instances via <see cref="R49MainWindowTestHarness"/> and
/// replace the second window's authoritative <see cref="WorkbookSession"/> with one over the
/// first window's actual (post-Loaded) workbook/sheet.
/// </summary>
public sealed class R89_ShowFormulasPerWindowTests
{
    [Fact]
    public void ShowFormulasBtn_Click_ChangedInOneWindow_DoesNotLeakIntoSiblingWindow()
    {
        StaTestRunner.Run(() =>
        {
            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window1);
                AdoptSameDocument(window2, workbook, sheetId);

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), CreateFormulaCell("1+1", 2));

                // Window 2 renders first, seeding its own per-window store from the (still
                // formulas-off) shared ShowFormulas field.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                GetDisplayedText(window2, 1, 1).Should().Be("2", "no window has Show Formulas on yet");

                // Window 1 turns Show Formulas on.
                InvokeShowFormulasBtnClick(window1);

                GetDisplayedText(window1, 1, 1).Should().Be("=1+1",
                    "the window that toggled Show Formulas must render the formula text");

                // Re-render window 2 (e.g. a later, unrelated redraw) -- before the fix this would
                // read the shared Sheet.ShowFormulas field window 1 just flipped on.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                GetDisplayedText(window2, 1, 1).Should().Be("2",
                    "a sibling New Window that never toggled Show Formulas itself must keep showing " +
                    "the value, exactly like Excel keeps Show Formulas per-window");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    [Fact]
    public void ShowFormulasBtn_Click_SingleWindow_StillTogglesItsOwnRenderingAndUpdatesTheSharedSheet()
    {
        // Sibling/no-regression: a single window (no sibling in play) must still see its own
        // Show Formulas toggle take effect immediately, AND the shared Sheet field must still be
        // updated so the setting round-trips through save (the per-window store is a display
        // overlay layered on top of Sheet.ShowFormulas, never a replacement).
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window);
                var sheet = workbook.GetSheet(sheetId)!;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), CreateFormulaCell("1+1", 2));
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                GetDisplayedText(window, 1, 1).Should().Be("2");
                sheet.ShowFormulas.Should().BeFalse();

                InvokeShowFormulasBtnClick(window);

                GetDisplayedText(window, 1, 1).Should().Be("=1+1",
                    "toggling Show Formulas must still take effect locally");
                sheet.ShowFormulas.Should().BeTrue(
                    "the shared Sheet field must still be updated so Show Formulas round-trips through save");

                InvokeShowFormulasBtnClick(window);

                GetDisplayedText(window, 1, 1).Should().Be("2",
                    "toggling Show Formulas back off must still take effect locally");
                sheet.ShowFormulas.Should().BeFalse();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static Cell CreateFormulaCell(string formulaText, double computedValue)
    {
        var cell = Cell.FromFormula(formulaText);
        cell.Value = new NumberValue(computedValue);
        return cell;
    }

    private static string? GetDisplayedText(MainWindow window, uint row, uint col)
    {
        var viewport = window.SheetGrid.Viewport;
        if (viewport is null)
            return null;

        foreach (var cell in viewport.Cells)
        {
            if (cell.Row == row && cell.Col == col)
                return cell.DisplayText;
        }

        return null;
    }

    private static void AdoptSameDocument(MainWindow window, Workbook workbook, SheetId sheetId)
    {
        R49MainWindowTestHarness.Invoke(
            window,
            "ReplaceWorkbookSession",
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        window.Session.SelectSheet(sheetId);
        typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, sheetId);
    }

    private static SheetId GetCurrentSheetId(MainWindow window) =>
        (SheetId)typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void InvokeShowFormulasBtnClick(MainWindow window) =>
        R49MainWindowTestHarness.Invoke(window, "ShowFormulasBtn_Click", null, new System.Windows.RoutedEventArgs());
}
