using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for round-140 finding F1 (lens shared-accessibility-tree):
///
/// Name Box (Go To) navigation moved the active cell correctly (NavigateCellAddressBoxTo ->
/// RefreshShell), but the follow-up call, FocusShellRegion(ShellFocusTarget.Worksheet), landed real
/// keyboard focus on the static "Worksheet"-named <c>_sheetGridHost</c> ContentControl instead of the
/// destination cell's own Border, so a screen reader announced only "Worksheet" and never the
/// destination cell's address/contents. The shell's own <c>MoveFocusToActiveCellBorder</c> already
/// exists to solve exactly this (it is used by arrow-key navigation, gated on the grid already having
/// focus pre-rebuild), but <c>FocusShellRegion</c>'s Worksheet arm never called it. The fix routes
/// every <c>FocusShellRegion(Worksheet)</c> caller -- Name Box navigation, committing an inline edit
/// across the whole selection (Ctrl+Enter), cancelling an inline edit (Escape), and the rest -- through
/// a new <c>FocusActiveCellOrGridHost</c> helper that prefers the active cell's Border and only falls
/// back to <c>_sheetGridHost</c> when no active cell Border exists yet.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R140_NameBoxFocusAccessibilityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Primary: Name Box Enter must move real focus onto the destination cell's Border ──────

    [Fact]
    public async Task NameBoxGoTo_MovesRealKeyboardFocusOntoTheDestinationCellBorder()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            // Simulate the user having typed into the Name Box and pressing Enter -- this is the
            // exact production entry point (CellAddressBox_KeyDown -> NavigateCellAddressBoxTo ->
            // RefreshShell -> FocusShellRegion) named in the finding.
            window.CellAddressBoxTextForTest = "Z100";
            window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 100, 26));

            var focusedAfterNavigation = window.FocusManager!.GetFocusedElement();
            focusedAfterNavigation.Should().NotBeNull(
                "Name Box navigation must move real keyboard focus, not leave it wherever it was " +
                "before Enter was pressed");
            focusedAfterNavigation.Should().NotBe(window.SheetGridHostForTest,
                "focus must move OFF the static \"Worksheet\"-named host onto the destination cell, " +
                "otherwise a screen reader announces only \"Worksheet\" and never Z100");
            focusedAfterNavigation.Should().BeSameAs(window.ActiveCellBorderForTest,
                "focus must land on exactly the destination cell's Border so a screen reader " +
                "announces its accessible name/address");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Named sibling #1: committing an inline edit across the selection (Ctrl+Enter) ────────

    [Fact]
    public async Task CommitInlineEditAcrossSelection_MovesRealKeyboardFocusOntoTheActiveCellBorder()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(address);

            window.BeginInlineCellEditForTest(address, "42", 2);
            window.RaiseInlineCellEditorKeyDownForTest(
                new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.Control });

            sheet.GetValue(address).Should().Be(new NumberValue(42));

            var focusedAfterCommit = window.FocusManager!.GetFocusedElement();
            focusedAfterCommit.Should().NotBeNull();
            focusedAfterCommit.Should().NotBe(window.SheetGridHostForTest,
                "committing across the selection must not leave focus stuck on the static grid host");
            focusedAfterCommit.Should().BeSameAs(window.ActiveCellBorderForTest,
                "focus must land on the active cell's Border after Ctrl+Enter, just like Name Box " +
                "navigation");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Named sibling #2: cancelling an inline edit (Escape) ──────────────────────────────────

    [Fact]
    public async Task CancelInlineEdit_MovesRealKeyboardFocusOntoTheActiveCellBorder()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 4, 2);
            window.Session.SelectCell(address);

            window.BeginInlineCellEditForTest(address, "unsaved text", 0);
            window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape });

            window.Session.ActiveCell.Should().Be(address,
                "Escape cancels the edit in place, it does not move the active cell");
            sheet.GetValue(address).Should().Be(BlankValue.Instance,
                "the cancelled edit must not commit any value");

            var focusedAfterCancel = window.FocusManager!.GetFocusedElement();
            focusedAfterCancel.Should().NotBeNull();
            focusedAfterCancel.Should().NotBe(window.SheetGridHostForTest,
                "cancelling an inline edit must not leave focus stuck on the static grid host");
            focusedAfterCancel.Should().BeSameAs(window.ActiveCellBorderForTest,
                "focus must return to the active cell's own Border after Escape");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Sibling regression guard: the Worksheet focus target always resolves to a real control ──

    [Fact]
    public async Task WorksheetFocusTarget_AlwaysResolvesToARealFocusTarget_WithoutThrowing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();

            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            // Escape from the Name Box is one of the many Worksheet-target FocusShellRegion callers.
            // Whether or not an active cell Border happens to exist yet, resolving the Worksheet
            // focus target must never throw and must never leave focus on nothing -- proving the new
            // FocusActiveCellOrGridHost fallback path (used when there is no active cell Border) did
            // not regress the pre-fix always-succeeds guarantee that FocusControl(_sheetGridHost)
            // alone used to provide.
            var act = () => window.RaiseCellAddressBoxKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
            act.Should().NotThrow();

            var focused = window.FocusManager!.GetFocusedElement();
            focused.Should().NotBeNull(
                "the Worksheet focus target must always land on a real control, never leave focus " +
                "completely unset");
            (ReferenceEquals(focused, window.SheetGridHostForTest) ||
                ReferenceEquals(focused, window.ActiveCellBorderForTest)).Should().BeTrue(
                "the Worksheet focus target must resolve to either the active cell's Border or, " +
                "failing that, the grid host -- never anything else");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
