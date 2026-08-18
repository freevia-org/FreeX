using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for r140 finding avalonia-missing-ctrl-numpad-insert-delete-cells (src/
/// FreeX.App.Avalonia/MainWindow.KeyboardParity.cs). The WPF host's KeyboardShortcutMatcher.
/// IsCtrlPlus/IsCtrlMinus (src/FreeX.App.Host/KeyboardShortcutMatcher.cs) treat the numeric-keypad
/// Add/Subtract keys as aliases of the row-key OemPlus/OemMinus for the Insert Cells / Delete Cells
/// shortcuts (Ctrl+NumpadPlus / Ctrl+NumpadMinus), but AvaloniaLocalShortcutRules (MainWindow.
/// KeyboardParity.cs) previously listed only Key.OemPlus/Key.OemMinus, so the keypad chords were
/// silently swallowed on the Avalonia (Linux/macOS) shell -- no dialog, no insert/delete, no error.
///
/// Mirrors the existing R79_KeyboardInsertDeleteWholeRowColumnAutoDetectTests coverage for the
/// row-key chords: these tests drive the real key-down handler via RaiseKeyDownForTest (not the
/// TryResolveAvaloniaLocalShortcut helper directly) so both the whole-row/whole-column auto-detect
/// routing (no dialog) and the plain-cell-range routing (which does show Excel's shift-direction
/// prompt, matching WPF's ExecuteKeyboardInsertCellsWithPrompt/ExecuteKeyboardDeleteCellsWithPrompt)
/// are proven through the production dispatch path a real user reaches.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R140_CtrlNumpadInsertDeleteCellsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Insert Cells: whole-row selection via the real Ctrl+NumpadPlus keyboard shortcut ─────────

    [Fact]
    public async Task CtrlNumpadPlus_WholeRowSelected_InsertsRowDirectly_NoDialogPrompt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardNumpadInsertWholeRow");
            window.Session.SelectSheet(sheet.Id);

            var wholeRow3 = new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 3, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow3);

            var args = new KeyEventArgs { Key = Key.Add, KeyModifiers = KeyModifiers.Control };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue(
                "Ctrl+NumpadPlus must be consumed by the Insert Cells shortcut, exactly as Ctrl+'+' " +
                "already is -- the WPF host's KeyboardShortcutMatcher.IsCtrlPlus treats Key.Add and " +
                "Key.OemPlus as aliases for the same Insert Cells shortcut");
            window.StatusTextForTest.Text.Should().Be("Inserted rows",
                "a whole-row selection reached via the real Ctrl+NumpadPlus keyboard shortcut must " +
                "auto-detect and insert an entire row directly, matching WPF's " +
                "KeyboardInsertDeletePlanner.PlanInsert routing");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── Delete Cells: whole-column selection via the real Ctrl+NumpadMinus keyboard shortcut ─────

    [Fact]
    public async Task CtrlNumpadMinus_WholeColumnSelected_DeletesColumnDirectly_NoDialogPrompt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardNumpadDeleteWholeColumn");
            window.Session.SelectSheet(sheet.Id);

            var wholeColumnC = new GridRange(
                new CellAddress(sheet.Id, 1, 3),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 3));
            window.Session.SelectRange(wholeColumnC);

            var args = new KeyEventArgs { Key = Key.Subtract, KeyModifiers = KeyModifiers.Control };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue(
                "Ctrl+NumpadMinus must be consumed by the Delete Cells shortcut, exactly as Ctrl+'-' " +
                "already is -- the WPF host's KeyboardShortcutMatcher.IsCtrlMinus treats Key.Subtract " +
                "and Key.OemMinus as aliases for the same Delete Cells shortcut");
            window.StatusTextForTest.Text.Should().Be("Deleted columns",
                "a whole-column selection reached via the real Ctrl+NumpadMinus keyboard shortcut must " +
                "auto-detect and delete an entire column directly, matching WPF's " +
                "KeyboardInsertDeletePlanner.PlanDelete routing");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // For the plain-cell-range case (which routes to Excel's shift-direction prompt via
    // ShowInsertCellsDialogAsync/ShowDeleteCellsDialogAsync, exactly like Ctrl+'+'/Ctrl+'-' already
    // do -- both new rules resolve to the same AvaloniaLocalShortcut.InsertCells/DeleteCells enum
    // value and the same dialog handlers, so no new routing logic exists for that case), coverage
    // comes from AvaloniaShortcutInteractionCoverageTests.ProductionShortcutValidationCore_
    // CompletesEntireCatalog, which now exercises "Ctrl+Numpad +"/"Ctrl+Numpad -" (added to the
    // shortcut.editing.insert-delete scenario in InteractiveValidationInventory.cs) through the real
    // production window via ExerciseShortcutInteractionAsync -- the only harness in this codebase
    // that safely settles the resulting modal shift-direction dialog instead of hanging on it.

    // ── No-regression sibling: the pre-existing row-key chords must still resolve exactly as ─────
    // ── before -- adding the two new rules must not shadow or duplicate-match Ctrl+'+'/Ctrl+'-'. ──

    [Fact]
    public async Task CtrlPlusRowKey_StillInsertsRowDirectly_UnaffectedByTheNewNumpadRules()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("KeyboardRowKeyInsertStillWorks");
            window.Session.SelectSheet(sheet.Id);

            var wholeRow2 = new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
            window.Session.SelectRange(wholeRow2);

            var args = new KeyEventArgs { Key = Key.OemPlus, KeyModifiers = KeyModifiers.Control };
            await window.RaiseKeyDownForTest(args);

            args.Handled.Should().BeTrue("Ctrl+'+' must still be consumed by the Insert Cells shortcut");
            window.StatusTextForTest.Text.Should().Be("Inserted rows",
                "the pre-existing OemPlus rule must keep working unchanged after the new Key.Add rule " +
                "is added alongside it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
