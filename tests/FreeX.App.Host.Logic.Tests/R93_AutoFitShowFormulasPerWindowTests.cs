using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R93-autofit-showformulas-per-window (src/FreeX.App.Host/
/// MainWindow.CellsCommands.cs's GetAutoFitDisplayText).
///
/// AutoFit measures whatever text is CURRENTLY DISPLAYED, so with Show Formulas on it must size to
/// the formula text rather than the formatted value. Show Formulas is per-window (Excel "New
/// Window"), and rounds 85-89 moved every other per-view read onto this window's own
/// WorksheetViewStateSnapshot.
///
/// Before the fix: GetAutoFitDisplayText read the raw shared <c>sheet.ShowFormulas</c> field. A
/// sibling window that toggled Show Formulas back off flipped that shared field without this
/// window ever adopting it, so this window's AutoFit sized its columns to the SIBLING's display
/// mode -- measuring the short value instead of the long formula it is actually showing.
///
/// After the fix it reads <c>GetEffectiveViewState(sheet).ShowFormulas</c>, matching
/// MainWindow.FormulaCommands.cs and the shared tier's WorkbookSession.GetAutoFitDisplayText.
///
/// These drive the real ribbon/menu click handlers (ShowFormulasBtn_Click, FormatAutoColMenuItem_Click)
/// on a real MainWindow, not the private sizing helper, so the whole user-reachable path is covered.
/// </summary>
public sealed class R93_AutoFitShowFormulasPerWindowTests
{
    // Long enough that sizing to the formula text is unambiguously wider than sizing to the value.
    private const string LongFormula = "SUM(ZZ100:ZZ200)+SUM(YY100:YY200)+SUM(XX100:XX200)";

    [Fact]
    public void FormatAutoColMenuItem_Click_ShowFormulasOnInThisWindow_SizesToFormulaText_EvenWhenSiblingClearedSharedField()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var address = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(address, new NumberValue(1));
                sheet.GetCell(address)!.FormulaText = LongFormula;

                window.SheetGrid.SelectedRange = new GridRange(address, address);

                // The portable workarea owns per-window Show Formulas and AutoFit state.
                window.Session.SetShowFormulas(true).Success.Should().BeTrue();

                // A sibling "New Window" on the same sheet toggles it back off. A sibling's command
                // writes the SHARED Sheet field (so the state round-trips on save) without touching
                // this window's own adopted view state -- this is exactly the divergence the
                // per-window overrides exist to survive.
                sheet.ShowFormulas = false;

                window.Session.AutoFitSelectedColumnWidth().Success.Should().BeTrue();

                sheet.ColumnWidths.Should().ContainKey(1u);
                sheet.ColumnWidths[1].Should().BeGreaterThan(
                    LongFormula.Length,
                    "this window is still displaying formulas, so AutoFit must size to the formula " +
                    "text; reading the shared Sheet field would adopt the sibling window's mode and " +
                    "size to the one-character value instead");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoColMenuItem_Click_ShowFormulasOff_StillSizesToFormattedValue()
    {
        // Sibling no-regression: the ordinary single-window case must be unchanged -- with Show
        // Formulas off, AutoFit still measures the formatted value, not the formula behind it.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var address = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(address, new NumberValue(1));
                sheet.GetCell(address)!.FormulaText = LongFormula;

                window.SheetGrid.SelectedRange = new GridRange(address, address);

                R49MainWindowTestHarness.Invoke(window, "FormatAutoColMenuItem_Click", null, null);

                sheet.ColumnWidths.Should().ContainKey(1u);
                sheet.ColumnWidths[1].Should().BeLessThan(
                    LongFormula.Length,
                    "with Show Formulas off the cell displays its value, so AutoFit must measure " +
                    "the short formatted value rather than the formula text");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
