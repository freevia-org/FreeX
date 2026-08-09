using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R79-commands-insert-delete-shift-5-1 (src/FreeX.App.Avalonia/
/// MainWindow.KeyboardParity.cs:357-361). Ctrl+'+'/Ctrl+Shift+'+' (Insert Cells) and Ctrl+'-'
/// (Delete Cells) dispatch straight to ShowInsertCellsDialogAsync/ShowDeleteCellsDialogAsync. Those
/// methods (fixed for the sibling finding R79-commands-insert-delete-shift-5-2, in
/// MainWindow.InsertDeleteCells.cs) now check SelectionRangeService.IsWholeRowSelection/
/// IsWholeColumnSelection *before* ever building the shift-direction dialog, and route a whole-row/
/// whole-column selection straight to InsertRowsCommand/InsertColumnsCommand (and their Delete-
/// counterparts) with no prompt -- exactly matching Excel and the WPF host's
/// KeyboardInsertDeletePlanner.
///
/// The existing R79_InsertDeleteCellsWholeRowColumnRoutingTests only reflect into
/// ShowInsertCellsDialogAsync/ShowDeleteCellsDialogAsync directly; they do not prove the *keyboard*
/// entry point (MainWindow_KeyDownAsync, reached via the AvaloniaHostShortcut.InsertCells/DeleteCells
/// routes at MainWindow.KeyboardParity.cs:357-361) benefits from the fix. These tests drive the real
/// key-down handler via RaiseKeyDownForTest so a whole-row/whole-column selection is proven to insert/
/// delete directly rather than await an unanswered modal shift-direction dialog (which a partial
/// selection would still show, and which would deadlock this synchronous await if ever reached here).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R79_KeyboardInsertDeleteWholeRowColumnAutoDetectTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Insert Cells: whole-row selection via the real Ctrl+Shift+'+' keyboard shortcut ──────────

    [Fact]
    public async Task CtrlShiftPlus_WholeRowSelected_InsertsRowDirectly_NoDialogPrompt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardInsertWholeRow");
            window.Session.SelectSheet(sheet.Id);

            var wholeRow3 = new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 3, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow3);

            var args = new KeyEventArgs { Key = Key.OemPlus, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue("Ctrl+Shift+'+' must be consumed by the Insert Cells shortcut");
            window.StatusTextForTest.Text.Should().Be("Inserted rows",
                "a whole-row selection reached via the real Ctrl+Shift+'+' keyboard shortcut must " +
                "auto-detect and insert an entire row directly -- if it instead awaited the " +
                "shift-direction dialog (as it did before the fix), this call would deadlock and " +
                "never reach this assertion");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Delete Cells: whole-column selection via the real Ctrl+'-' keyboard shortcut ─────────────
    // (no-regression sibling covering the other command/axis combination)

    [Fact]
    public async Task CtrlMinus_WholeColumnSelected_DeletesColumnDirectly_NoDialogPrompt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardDeleteWholeColumn");
            window.Session.SelectSheet(sheet.Id);

            var wholeColumnC = new GridRange(
                new CellAddress(sheet.Id, 1, 3),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
            window.Session.SelectRange(wholeColumnC);

            var args = new KeyEventArgs { Key = Key.OemMinus, KeyModifiers = KeyModifiers.Control };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue("Ctrl+'-' must be consumed by the Delete Cells shortcut");
            window.StatusTextForTest.Text.Should().Be("Deleted columns",
                "a whole-column selection reached via the real Ctrl+'-' keyboard shortcut must " +
                "auto-detect and delete an entire column directly, with no shift-direction dialog");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
