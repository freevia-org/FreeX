using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Avalonia-shell regression coverage for
/// R139-freex-cell-editing-edit-commit-collapses-range-selection (the WPF twin is
/// R139_EditCommitPreservesRangeSelectionTests in FreeX.App.Host.Logic.Tests).
///
/// Before this fix, committing an in-cell edit with Enter/Tab always routed through the
/// delta-based <c>_session.MoveActiveCell(rowDelta, colDelta)</c> (which calls
/// <c>SelectCell</c>, unconditionally collapsing any pre-existing multi-cell selection down to
/// a single cell) instead of cycling the active cell WITHIN the pre-existing selection the way
/// real Excel -- and the app's own ready-mode Enter/Tab handler (NavigateActiveCell) -- do. The
/// deeper root cause lived in the shared <c>WorkbookSession.ApplySuccessfulEditResult</c>
/// (src/FreeX.App.Services/WorkbookSession.cs), which unconditionally called
/// <c>SetSingleSelectedRange</c> on every successful commit regardless of any wider selection;
/// that collapse happens before either shell's own active-cell-advance logic runs, so both
/// shells needed the shared fix plus their own cycling logic.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R139_EditCommitPreservesRangeSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EnterCommit_WithinPreExistingRangeSelection_KeepsSelectionAndCyclesActiveCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var top = new CellAddress(sheet.Id, 2, 2);    // B2
                var bottom = new CellAddress(sheet.Id, 5, 2); // B5
                window.Session.SelectAnchoredRange(top, bottom);
                Refresh(window);
                window.Session.SelectedRange.Should().Be(new GridRange(top, bottom));
                window.Session.ActiveCell.Should().Be(top);

                window.ActiveCellBorderForTest.Should().NotBeNull();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                Press(window, Key.F2, PhysicalKey.F2);
                var editor = FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor");
                RaiseRawTextInput(editor, "10");
                await DrainInputAsync();

                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(top).Should().Be(new NumberValue(10),
                    "the typed value must still commit to the cell that was being edited");
                window.Session.SelectedRange.Should().Be(new GridRange(top, bottom),
                    "Enter committing an edit must NOT collapse a pre-existing multi-cell selection " +
                    "(R139-freex-cell-editing-edit-commit-collapses-range-selection)");
                window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 2), // B3
                    "Enter must advance the active cell to the next cell WITHIN the selection");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    // Wrap-direction coverage: committing at the LAST cell of the range must wrap the active
    // cell back to the FIRST cell, matching Excel, instead of walking off the selection.
    [Fact]
    public async Task EnterCommit_AtEndOfRangeSelection_WrapsActiveCellBackToStart()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var top = new CellAddress(sheet.Id, 2, 2);    // B2
                var bottom = new CellAddress(sheet.Id, 3, 2); // B3
                var range = new GridRange(top, bottom);
                window.Session.SelectAnchoredRange(top, bottom);
                Refresh(window);

                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F2, PhysicalKey.F2);
                RaiseRawTextInput(FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor"), "1");
                await DrainInputAsync();
                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                window.Session.ActiveCell.Should().Be(bottom);
                window.Session.SelectedRange.Should().Be(range);

                Refresh(window);
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F2, PhysicalKey.F2);
                RaiseRawTextInput(FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor"), "2");
                await DrainInputAsync();
                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                window.Session.ActiveCell.Should().Be(top,
                    "Enter from the last cell of the selected range must wrap back to the first cell");
                window.Session.SelectedRange.Should().Be(range,
                    "wrapping must not collapse or otherwise change the selected range");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    // Sibling no-regression: when there is NO multi-cell selection (a single selected cell),
    // Enter committing an edit must keep behaving exactly as before this fix -- the active cell
    // simply advances, collapsing to (and staying on) a single cell.
    [Fact]
    public async Task EnterCommit_WithNoRangeSelection_StillAdvancesActiveCellNormally()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                var addr = window.Session.ActiveCell;
                window.Session.SelectedRange.Should().Be(new GridRange(addr, addr));

                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();
                Press(window, Key.F2, PhysicalKey.F2);
                RaiseRawTextInput(FindByAutomationId<TextBox>(window, "WorksheetInlineCellEditor"), "42");
                await DrainInputAsync();
                Press(window, Key.Enter, PhysicalKey.Enter);
                await DrainInputAsync();

                sheet.GetValue(addr).Should().Be(new NumberValue(42));
                var expectedNext = new CellAddress(addr.Sheet, addr.Row + 1, addr.Col);
                window.Session.ActiveCell.Should().Be(expectedNext,
                    "with no multi-cell selection to cycle within, Enter must still just advance " +
                    "the active cell, unchanged from the pre-fix behavior");
                window.Session.SelectedRange.Should().Be(new GridRange(expectedNext, expectedNext));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("R139EditCommitFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static void Press(
        MainWindow window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
    }

    private static void RaiseRawTextInput(InputElement target, string text) =>
        target.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = target,
            Text = text,
        });

    private static T FindByAutomationId<T>(MainWindow window, string automationId)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
