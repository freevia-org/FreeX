using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression test for FreeX cleanup batch B1 HIGH finding P33 (accessibility-deep):
///
/// Avalonia grid navigation was silent to screen readers because keyboard focus never left
/// _sheetGridHost (a ContentControl with a static "Worksheet" accessible name); the per-cell
/// Borders carried an AutomationId/Name but were never Focusable, so a screen reader had no
/// focus-change signal at all while the user arrowed from cell to cell. The fix makes the active
/// cell's Border real, focusable, and moves actual keyboard focus onto it after each grid rebuild
/// (MainWindow.cs's MoveFocusToActiveCellBorder/IsGridFocused), gated so it only steals focus when
/// the grid itself already had it (captured BEFORE the rebuild, since detaching the old focused
/// Border clears focus outright rather than leaving it on an ancestor).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXCleanupB1Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ArrowKeyNavigation_MovesRealKeyboardFocusOntoNewActiveCellBorder()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            // Simulate the grid already having real keyboard focus (e.g. the user tabbed into it
            // or clicked a cell earlier) — the pre-fix state where focus then never moved again no
            // matter how many times the user pressed an arrow key.
            window.FocusManager!.Focus(window.SheetGridHostForTest);
            window.FocusManager!.GetFocusedElement().Should().Be(window.SheetGridHostForTest,
                "test setup must start with focus on the grid host, mirroring the pre-fix stuck state");

            // Drives the real production KeyDown handler (MainWindow_KeyDownAsync ->
            // NavigateActiveCell -> RefreshShell), which is what rebuilds the grid for the
            // "CleanFixture" sheet and (post-fix) moves focus onto the new active cell.
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Down });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 1));

            var focusedAfterNavigation = window.FocusManager!.GetFocusedElement();
            focusedAfterNavigation.Should().NotBeNull(
                "arrow-key navigation must move real keyboard focus, not leave it stuck on the static grid host");
            focusedAfterNavigation.Should().NotBe(window.SheetGridHostForTest,
                "focus must move OFF the static \"Worksheet\"-named host and onto the new active cell");
            focusedAfterNavigation.Should().BeSameAs(window.ActiveCellBorderForTest,
                "focus must land on exactly the active cell's Border so a screen reader announces its accessible name");

            var focusedElement = (StyledElement)focusedAfterNavigation!;
            AutomationProperties.GetAutomationId(focusedElement).Should().Be("Cell_A2",
                "the focused control's automation id must identify the new active cell");
            AutomationProperties.GetName(focusedElement).Should().Contain("A2",
                "the focused control's accessible name must announce the new active cell's address");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ArrowKeyNavigation_DoesNotStealFocus_WhenGridDidNotAlreadyHaveIt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            // Explicitly clear focus (simulating focus resting on a dialog/menu/formula bar, i.e.
            // NOT on the grid) before the grid rebuild that arrow-key navigation triggers.
            window.FocusManager!.Focus(null);
            window.FocusManager!.GetFocusedElement().Should().BeNull(
                "test setup must start with focus off the grid entirely for this guard to be meaningful");

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Down });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 1),
                "navigation itself must still work even though nothing had focus");
            window.FocusManager!.GetFocusedElement().Should().BeNull(
                "a grid rebuild must not grab focus when the grid did not already have it, " +
                "so it never steals focus from a dialog/menu/formula bar the user is actually using");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
