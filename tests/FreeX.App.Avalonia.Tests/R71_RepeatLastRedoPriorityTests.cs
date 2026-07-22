using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R71-services-undo-redo-4-1: Excel treats F4/Repeat-Last as REDO
/// whenever a redo is pending (redo takes priority over repeat). Without the CanRedo gate in
/// <c>MainWindow.ExecuteRepeatLastAction</c> (<c>src/FreeX.App.Avalonia/MainWindow.cs</c>), F4
/// after an Undo would re-invoke the stale repeatable factory against whatever is now selected AND
/// destroy the pending redo entry (a plain command execute clears the redo stack), permanently
/// losing the undone change.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R71_RepeatLastRedoPriorityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task F4_AfterUndo_PerformsRedoInsteadAndLeavesNewSelectionUntouched()
    {
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b5 = new CellAddress(sheet.Id, 5, 2);

            window.Session.SelectCell(a1);
            window.Session.SetSelectedRangeBold(true).Success.Should().BeTrue();
            IsBold(window, sheet, 1, 1).Should().BeTrue();

            window.Session.UndoLastEdit().Success.Should().BeTrue();
            IsBold(window, sheet, 1, 1).Should().BeFalse("undo must revert the bold applied to A1");
            window.Session.CanRedo.Should().BeTrue();

            // Select a different cell before pressing F4 -- this is the scenario that exposed the
            // bug: the stale repeatable factory closes over "the current selection" at replay time.
            window.Session.SelectCell(b5);
            await Press(window, Key.F4, KeyModifiers.None);

            // Redo took priority: A1's bold comes back...
            IsBold(window, sheet, 1, 1).Should().BeTrue();

            // ...and B5 (the now-current-before-F4 selection) was never touched by a stale repeat.
            // (Note: Excel/WorkbookSession re-selects the redone range after Redo, so the *active*
            // selection moves back to A1 -- checked directly against the sheet's style rather than
            // via IsSelectedRangeStartBold to avoid coupling this assertion to that selection move.)
            IsBold(window, sheet, 5, 2).Should().BeFalse();

            // The redo entry was consumed (not destroyed): nothing left to redo, but the undo that
            // would revert the just-replayed bold is available again.
            window.Session.CanRedo.Should().BeFalse();
            window.Session.CanUndo.Should().BeTrue();
        });
    }

    [Fact]
    public async Task F4_WithNoPendingRedo_StillRepeatsAgainstCurrentSelection()
    {
        await Run(async (window, sheet) =>
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var b5 = new CellAddress(sheet.Id, 5, 2);

            window.Session.SelectCell(a1);
            window.Session.SetSelectedRangeBold(true).Success.Should().BeTrue();
            window.Session.CanRedo.Should().BeFalse();

            // Normal Repeat behavior is unchanged when there is no pending redo: B5 gets bolded too.
            window.Session.SelectCell(b5);
            await Press(window, Key.F4, KeyModifiers.None);

            IsBold(window, sheet, 1, 1).Should().BeTrue();
            IsBold(window, sheet, 5, 2).Should().BeTrue();
        });
    }

    private static bool IsBold(MainWindow window, Sheet sheet, uint row, uint col)
    {
        var styleId = sheet.GetStyleOnly(row, col) ?? sheet.GetCell(new CellAddress(sheet.Id, row, col))?.StyleId;
        return styleId is { } id && window.Session.Workbook.GetStyle(id).Bold;
    }

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("RepeatRedoPriority");
            window.Session.SelectSheet(sheet.Id);
            try
            {
                await test(window, sheet);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task Press(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue($"{modifiers}+{key} should be consumed by MainWindow");
    }
}
