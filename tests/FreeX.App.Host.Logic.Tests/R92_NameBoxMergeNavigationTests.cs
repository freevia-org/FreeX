using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R92-commands-merge-edge-5-2 (src/FreeX.App.Host/MainWindow.Editing.cs, NavigateNameBoxTo /
/// src/FreeX.App.Host/MainWindow.Selection.cs, SetSelectionRange): Name Box / Go To navigation to a
/// cell COVERED by a merged region (not its anchor) used to land on the literal single covered cell
/// instead of expanding to the whole merge -- real Excel always selects the FULL merged range in this
/// situation, identical to clicking that covered cell with the mouse (a covered cell is never
/// independently selectable). <see cref="NavigateNameBoxTo"/> called <c>SetSelectionRange</c> directly
/// with no merge lookup, unlike <c>SetActiveCell</c> (mouse click) and <c>ExtendSelection</c> (drag),
/// which both snap/expand to the merge. The fix expands at <c>SetSelectionRange</c> itself -- the
/// single choke point every Name Box/Go To/hyperlink navigation call funnels through -- via the same
/// <c>ExpandRangeToFullyContainMerges</c> helper <c>ExtendSelection</c> already used, plus snapping the
/// active cell to the merge anchor when it lands on a covered cell.
///
/// Invokes the real product method (<c>NavigateNameBoxTo</c>) via the shared MainWindow test harness
/// rather than a hand-built selection model -- the nearest headless seam available for App.Host,
/// since MainWindow itself is a WPF window (StaTestRunner + R49MainWindowTestHarness are this project's
/// standard way to exercise it without a full interactive KeyEventArgs/ComboBox round trip).
/// </summary>
public sealed class R92_NameBoxMergeNavigationTests
{
    private static GridRange? GetSelectedRange(MainWindow window) =>
        ((SheetGridView)window.FindName("SheetGrid")!).SelectedRange;

    private static TextBox GetFormulaBar(MainWindow window) =>
        (TextBox)window.FindName("FormulaBar")!;

    [Fact]
    public void NavigateNameBoxTo_CellCoveredByMerge_SelectsWholeMergedRegionWithAnchorActive()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                // A1:C3 merged, anchor A1.
                var mergeRegion = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3));
                sheet.AddMergedRegion(mergeRegion);
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor-value"));

                // "B2" (row 2, col 2) is covered by the merge but is NOT its anchor.
                var coveredCell = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2));

                R49MainWindowTestHarness.Invoke(window, "NavigateNameBoxTo", coveredCell);
                R49MainWindowTestHarness.PumpDispatcher();

                GetSelectedRange(window).Should().Be(mergeRegion,
                    "navigating to a cell covered by a merge must select the WHOLE merge, exactly like a mouse click on the same cell does (SetActiveCell)");
                GetFormulaBar(window).Text.Should().Be("anchor-value",
                    "the active/formula-bar cell after landing inside a merge must be the merge's ANCHOR (A1), not the blank covered cell (B2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: navigating to a plain, non-merged cell must still select just that cell
    // (this fix's ExpandRangeToFullyContainMerges call is a documented no-op with no merges present).
    [Fact]
    public void NavigateNameBoxTo_PlainCell_StillSelectsSingleCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.Sheets[0];
                var target = new GridRange(new CellAddress(sheet.Id, 5, 5), new CellAddress(sheet.Id, 5, 5));

                R49MainWindowTestHarness.Invoke(window, "NavigateNameBoxTo", target);
                R49MainWindowTestHarness.PumpDispatcher();

                GetSelectedRange(window).Should().Be(target);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
