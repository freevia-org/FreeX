using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-22 regression guards for the "avalonia-editkeys" bucket:
///
///   R22-avalonia-parity-deep-1 - the Avalonia Formula Bar's Enter-commit called
///     ExcelEditKeyPlanner.GetIntent without moveSelectionAfterEnter/enterDirection, so it always
///     moved the active cell Down regardless of the persisted AppOptions.MoveSelectionAfterEnter /
///     AfterEnterDirection options, unlike the WPF host (MainWindow.Editing.cs:417-418) which
///     forwards both options through.
///
///   R22-avalonia-parity-deep-3 - GetIntent already computes ExcelEditKeyAction.CommitSelection for
///     Ctrl+Enter, but neither Avalonia key handler had a branch for it, so the keystroke was
///     silently swallowed: the edit was never committed, not even to the active cell.
///
/// Both drive the real FormulaBox_KeyDown handler via the RaiseFormulaBoxKeyDownForTest seam
/// (MainWindow.cs), so these exercise the actual production code path rather than a source-string
/// proxy.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaEditKeysParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── R22-avalonia-parity-deep-1: Enter must honor MoveSelectionAfterEnter / AfterEnterDirection ──

    [Fact]
    public async Task Enter_WithMoveSelectionAfterEnterDisabled_DoesNotMoveActiveCell()
    {
        await WithOptions(new AppOptions { MoveSelectionAfterEnter = false }, async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                // The default new-window workbook is the seeded port-preview demo (has content like
                // "Windows" at B1) — run this on a fresh, guaranteed-empty sheet instead.
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);
                var start = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(start);
                window.Session.BeginFormulaEdit(start);
                window.FormulaBoxTextForTest = "42";

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

                sheet.GetValue(start).Should().Be(new NumberValue(42));
                window.Session.ActiveCell.Should().Be(start,
                    "MoveSelectionAfterEnter=false must keep the active cell in place after Enter, matching the persisted option");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Enter_WithAfterEnterDirectionRight_MovesRightInsteadOfDown()
    {
        await WithOptions(
            new AppOptions { MoveSelectionAfterEnter = true, AfterEnterDirection = AppOptionsEnterDirection.Right },
            async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);
                var start = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(start);
                window.Session.BeginFormulaEdit(start);
                window.FormulaBoxTextForTest = "7";

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

                sheet.GetValue(start).Should().Be(new NumberValue(7));
                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 4),
                    "AfterEnterDirection=Right must move the active cell right after Enter, not down (the hardcoded default)");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Enter_WithDefaultOptions_StillMovesDown()
    {
        // Guards against a regression in the opposite direction: the default persisted options
        // (MoveSelectionAfterEnter=true, AfterEnterDirection=Down) must still behave exactly like
        // Excel's out-of-the-box Enter key.
        await WithOptions(new AppOptions(), async () =>
        {
            await Session.Dispatch(() =>
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);
                var start = new CellAddress(sheet.Id, 3, 3);
                window.Session.SelectCell(start);
                window.Session.BeginFormulaEdit(start);
                window.FormulaBoxTextForTest = "1";

                window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 3),
                    "default options must still move the active cell Down after Enter");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }, CancellationToken.None);
        });
    }

    // ── R22-avalonia-parity-deep-3: Ctrl+Enter must commit, not be swallowed ────────────────────────

    [Fact]
    public async Task CtrlEnter_CommitsEnteredValue_UnlikePlainEnterItDoesNotSwallowTheEdit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var start = new CellAddress(sheet.Id, 2, 2);
            window.Session.SelectCell(start);
            window.Session.BeginFormulaEdit(start);
            window.FormulaBoxTextForTest = "123";

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

            sheet.GetValue(start).Should().Be(new NumberValue(123),
                "Ctrl+Enter must commit the typed value; before the fix the keystroke was silently swallowed and nothing committed");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlEnter_FillsSameValueAcrossWholeSelection_AndKeepsSelectionIntact()
    {
        // Excel's Ctrl+Enter ("fill selection with same value") writes the exact same entered text
        // into every cell of the current selection, as one undoable action, and leaves the whole
        // range selected afterwards (unlike a plain Enter, which collapses to a single cell and
        // moves on).
        //
        // Note: this deliberately drives FormulaBox_KeyDown WITHOUT going through
        // WorkbookSession.BeginFormulaEdit first, so it isolates the CommitEditAcrossSelection fix:
        // given whatever range is selected at the moment Ctrl+Enter fires, it must fill every cell
        // in it and restore the selection afterward. The companion test below
        // (CtrlEnter_AfterBeginFormulaEdit_FillsWholeSelection) covers the realistic end-to-end
        // path where BeginFormulaEdit runs first — that used to collapse the selection to the edited
        // cell, but now preserves it when the edited cell is inside the selection (WorkbookSession.cs).
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2));
            window.Session.SelectRange(range);
            window.FormulaBoxTextForTest = "99";

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

            foreach (var address in range.AllCells())
            {
                sheet.GetValue(address).Should().Be(new NumberValue(99),
                    $"Ctrl+Enter must fill the same value into every selected cell, including {address}");
            }

            window.Session.SelectedRange.Should().Be(range,
                "Ctrl+Enter must leave the whole original selection intact, unlike a plain Enter commit which collapses to one cell");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlEnter_AfterBeginFormulaEdit_FillsWholeSelection()
    {
        // End-to-end guard for the live app: selecting a multi-cell range and then STARTING an edit
        // (BeginFormulaEdit, as every real edit-entry point does) must leave the range intact so the
        // subsequent Ctrl+Enter fills all of it. Before the WorkbookSession fix, BeginFormulaEdit
        // collapsed SelectedRange to the single edited cell, so live Ctrl+Enter only ever filled the
        // active cell — never the originally-selected range.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2));
            window.Session.SelectRange(range);
            window.Session.BeginFormulaEdit(range.Start);
            window.FormulaBoxTextForTest = "99";

            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

            foreach (var address in range.AllCells())
            {
                sheet.GetValue(address).Should().Be(new NumberValue(99),
                    $"Ctrl+Enter after starting the edit must still fill every selected cell, including {address}");
            }

            window.Session.SelectedRange.Should().Be(range,
                "the multi-cell selection must survive BeginFormulaEdit so Ctrl+Enter fills the whole range");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static async Task WithOptions(AppOptions options, Func<Task> action)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"freex-avalonia-editkeys-options-{Guid.NewGuid():N}.json");
        var previousEnv = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        try
        {
            AppOptionsStore.SaveToPath(options, tempPath);
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, tempPath);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousEnv);
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
