using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for F21 (MED): the Avalonia sheet-tab context-menu command path used to always
/// collapse an active multi-sheet GROUP selection down to just the right-clicked tab before running
/// any context-menu command (Rename, Tab Color, Ungroup Sheets, etc.), even when that tab was already
/// part of the active group. The WPF host preserves the group in that case
/// (<c>SheetTab_MouseRightButtonDown</c>) and only collapses when the clicked tab is outside the
/// current selection.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class SheetTabContextCommandGroupPreservationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SelectSheetForContextCommand_PreservesGroup_WhenClickedTabIsAlreadyInSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var sheet1 = workbook.Sheets[0];
                var details = workbook.AddSheet("Details");
                workbook.AddSheet("Charts");

                window.Session.SelectSheet(details.Id);
                window.Session.SelectAllVisibleSheets();
                window.Session.IsWorkbookGrouped.Should().BeTrue();

                // Act: simulate right-clicking the "Details" tab, which is already part of the
                // active grouped selection - the group must be preserved. The command itself
                // still reports success (returns true) even though the active sheet doesn't
                // change, matching TryCommitPendingFormulaEdit()'s "proceed" contract.
                var proceeded = window.SelectSheetForContextCommand(details.Id);

                proceeded.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(details.Id);
                window.Session.IsWorkbookGrouped.Should().BeTrue(
                    "right-clicking a tab already in the group must not collapse the group");
                window.Session.SheetTabs.Should().Contain(t => t.Id == sheet1.Id && t.IsGrouped);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectSheetForContextCommand_CollapsesGroup_WhenClickedTabIsOutsideSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var details = workbook.AddSheet("Details");
                var charts = workbook.AddSheet("Charts");

                // Group only {Sheet1, Details} via a Ctrl-click toggle so "Charts" stays OUTSIDE
                // the group (SelectAllVisibleSheets would have grouped Charts too).
                window.Session.SelectSheetFromTab(details.Id, selectRange: false, toggle: true);
                window.Session.IsWorkbookGrouped.Should().BeTrue();

                // Act: simulate right-clicking "Charts", which is NOT part of the active grouped
                // selection - this must collapse the group to just "Charts" (normal behavior).
                var changed = window.SelectSheetForContextCommand(charts.Id);

                changed.Should().BeTrue();
                window.Session.ActiveSheet.Id.Should().Be(charts.Id);
                window.Session.IsWorkbookGrouped.Should().BeFalse();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Shift)]
    public async Task ModifierPointerClick_PreservesTheGroupThroughTheButtonClick(KeyModifiers modifier)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var first = workbook.Sheets[0];
                var details = workbook.AddSheet("Details");
                window.Session.SelectSheet(first.Id);

                window.RaiseSheetTabModifierClickForTest(details.Id, modifier);

                window.Session.ActiveSheet.Id.Should().Be(details.Id);
                window.Session.IsWorkbookGrouped.Should().BeTrue(
                    "the modifier press must suppress the tab's ordinary Click selection");
                window.Session.IsSheetInActiveGroupSelection(first.Id).Should().BeTrue();
                window.Session.IsSheetInActiveGroupSelection(details.Id).Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Shift)]
    public async Task ModifierPointerRelease_ClearsSuppressionBeforeLaterKeyboardActivation(KeyModifiers modifier)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                var first = workbook.Sheets[0];
                var details = workbook.AddSheet("Details");
                window.Session.SelectSheet(first.Id);

                window.RaiseSheetTabModifierReleaseThenKeyboardClickForTest(details.Id, modifier);

                window.Session.ActiveSheet.Id.Should().Be(details.Id);
                window.Session.IsWorkbookGrouped.Should().BeFalse(
                    "pointer release must not leave suppression that swallows a later keyboard Click");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
