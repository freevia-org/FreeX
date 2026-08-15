using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R136 (parity gap): the Avalonia shell's Fill ▸ Series dialog (<c>ShowFillSeriesDialogAsync</c> in
/// MainWindow.FillSeries.cs) built its <c>EditCellsCommand</c> against ONLY the active sheet, unlike
/// the WPF host's <c>FillSeriesMenuItem_Click</c> (MainWindow.HomeEditing.cs), which fans the same
/// computed edits out to every sheet <c>CurrentGroupedEditSheetIds()</c> returns via
/// <c>GroupedEditCellsCommand</c> when sheet tabs are grouped. With multiple sheets grouped, the same
/// Fill ▸ Series gesture therefore filled every grouped sheet on Windows but only the active sheet on
/// Linux/macOS -- a functional divergence that silently produces different workbooks between shells.
///
/// The fix builds the same <c>GroupedEditCellsCommand</c> fan-out in the Avalonia dialog's OK handler,
/// gated on <c>_session.GetCurrentGroupedEditSheetIds()</c> (mirrors <c>EditCellsCommand</c> byte-for-byte
/// in the single/ungrouped-sheet case, matching R130's established grouped-sheet test pattern).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R136_FillSeriesGroupedSheetTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FillSeries_GroupedSheets_FillsEveryGroupedSheetWithTheSameValues()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("FillSeriesGroupedFanout");

                // Seed B2 on sheet1 with a number BEFORE grouping (and while ungrouped) -- committing
                // a cell edit WHILE sheets are grouped mirrors the write onto every other grouped sheet
                // (real Excel grouped-edit semantics), which would defeat the "only sheet1 has a seed"
                // premise this test depends on to prove the fan-out is the Fill Series command itself.
                window.Session.SelectSheet(sheet1.Id);
                window.Session.BeginFormulaEdit(new CellAddress(sheet1.Id, 2, 2));
                window.Session.CommitCellText("1").Success.Should().BeTrue();

                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(sheet1.Id);

                // B2:B4 -- default dialog options (Series in Columns i.e. fill down, Linear, Step 1)
                // reseed at B2 (=1) and fill B3=2, B4=3.
                var range = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 4, 2));
                window.Session.SelectRange(range);

                var task = window.ShowFillSeriesDialogForTestAsync();
                await DrainInputAsync();

                var dialog = window.OwnedWindows.OfType<Window>().Single(
                    w => AutomationProperties.GetAutomationId(w) == "FillSeriesDialog");
                var okButton = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => AutomationProperties.GetAutomationId(candidate) == "FillSeriesOkButton");
                okButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await task;

                window.OwnedWindows.Should().BeEmpty("the dialog must close after a successful fill");

                sheet1.GetValue(3, 2).Should().Be(new NumberValue(2), "the active grouped sheet must be filled");
                sheet1.GetValue(4, 2).Should().Be(new NumberValue(3), "the active grouped sheet must be filled");

                // THE DEFECT (pre-fix): only sheet1 (the active sheet) was filled; sheet2 was left
                // completely untouched despite being part of the same grouped-sheet edit.
                sheet2.GetValue(3, 2).Should().Be(
                    new NumberValue(2),
                    "the non-active grouped sheet must ALSO be filled, matching the WPF host's " +
                    "CurrentGroupedEditSheetIds() fan-out for FillSeriesMenuItem_Click");
                sheet2.GetValue(4, 2).Should().Be(new NumberValue(3), "the non-active grouped sheet must ALSO be filled");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: the ordinary UNGROUPED case must be entirely unaffected -- Fill Series
    // still fills only the active sheet, and a second, unrelated sheet is left completely untouched.
    [Fact]
    public async Task FillSeries_Ungrouped_StillFillsOnlyTheActiveSheet()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet1 = window.Session.Workbook.Sheets[0];
                var sheet2 = window.Session.Workbook.AddSheet("FillSeriesUngroupedFixture");

                window.Session.SelectSheet(sheet1.Id);
                window.Session.IsWorkbookGrouped.Should().BeFalse();
                window.Session.BeginFormulaEdit(new CellAddress(sheet1.Id, 2, 2));
                window.Session.CommitCellText("1").Success.Should().BeTrue();

                var range = new GridRange(new CellAddress(sheet1.Id, 2, 2), new CellAddress(sheet1.Id, 4, 2));
                window.Session.SelectRange(range);

                var task = window.ShowFillSeriesDialogForTestAsync();
                await DrainInputAsync();

                var dialog = window.OwnedWindows.OfType<Window>().Single(
                    w => AutomationProperties.GetAutomationId(w) == "FillSeriesDialog");
                var okButton = dialog.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => AutomationProperties.GetAutomationId(candidate) == "FillSeriesOkButton");
                okButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await DrainInputAsync();
                await task;

                window.OwnedWindows.Should().BeEmpty("the dialog must close after a successful fill");

                sheet1.GetValue(3, 2).Should().Be(new NumberValue(2));
                sheet1.GetValue(4, 2).Should().Be(new NumberValue(3));

                sheet2.GetValue(3, 2).Should().Be(BlankValue.Instance, "an ungrouped, unrelated sheet must not be touched by Fill Series");
                sheet2.GetValue(4, 2).Should().Be(BlankValue.Instance, "an ungrouped, unrelated sheet must not be touched by Fill Series");
            }
            finally
            {
                CloseWindowAndOwnedDialogs(window);
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void CloseWindowAndOwnedDialogs(Window window)
    {
        foreach (var owned in window.OwnedWindows.OfType<Window>().ToArray())
        {
            try { owned.Close(); } catch { /* best effort: never mask the real test failure */ }
        }

        try
        {
            (window as MainWindow)?.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        catch { /* best effort */ }
    }
}
