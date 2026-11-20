using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Behaviour parity with the Windows WPF changes merged since 2026-11-12 that the Avalonia shell
/// (macOS/Linux) had not mirrored: inline sheet-tab rename (f7abb1094f), right-click sheet menu
/// (7614981719), Ctrl+F1 ribbon visibility (e505a5e15b, fdaf36e852), Ctrl+Space / Shift+Space
/// keeping the active cell (4b00dec437) and the Name Box after a drag (56ac52f5c3).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class MacOsDriftBehaviourParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task DoubleClickSheetTab_EditsNameInlineAndEnterCommitsRename() =>
        Run(window =>
        {
            var workbook = window.Session.Workbook;
            var second = workbook.AddSheet("Second");
            window.Show();

            window.RaiseSheetTabDoubleClickForTest(second.Id);

            window.IsInlineSheetTabRenameActiveFor(second.Id).Should().BeTrue();
            window.OwnedWindows.Should().BeEmpty("the rename is in place, not a dialog");
            var editor = window.SheetTabRenameEditorForTest;
            editor.Should().NotBeNull();
            editor!.Text.Should().Be("Second");
            editor.Name.Should().Be("SheetTabRenameEditor");
            global::Avalonia.Automation.AutomationProperties.GetName(editor).Should().Be("Rename sheet tab");

            editor.Text = "Renamed";
            var enter = new KeyEventArgs { Key = Key.Enter };
            window.HandleInlineSheetTabRenameKeyDown(enter);

            enter.Handled.Should().BeTrue();
            window.IsInlineSheetTabRenameActive.Should().BeFalse();
            window.Session.Workbook.GetSheet(second.Id)!.Name.Should().Be("Renamed");
            window.SheetTabRenameEditorForTest.Should().BeNull();
        });

    [Fact]
    public Task EscapeCancelsInlineSheetTabRenameWithoutChangingTheName() =>
        Run(window =>
        {
            var second = window.Session.Workbook.AddSheet("Second");
            window.Show();
            window.RaiseSheetTabDoubleClickForTest(second.Id);
            window.SheetTabRenameEditorForTest!.Text = "Discarded";

            var escape = new KeyEventArgs { Key = Key.Escape };
            window.HandleInlineSheetTabRenameKeyDown(escape);

            escape.Handled.Should().BeTrue();
            window.IsInlineSheetTabRenameActive.Should().BeFalse();
            window.Session.Workbook.GetSheet(second.Id)!.Name.Should().Be("Second");
            window.SheetTabButtonForTest(second.Id)!
                .GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
        });

    [Fact]
    public Task RejectedInlineRenameKeepsTheEditorOpen() =>
        Run(window =>
        {
            var first = window.Session.Workbook.Sheets[0];
            var second = window.Session.Workbook.AddSheet("Second");
            window.Show();
            window.RaiseSheetTabDoubleClickForTest(second.Id);
            window.SheetTabRenameEditorForTest!.Text = first.Name;

            window.HandleInlineSheetTabRenameKeyDown(new KeyEventArgs { Key = Key.Enter });

            window.IsInlineSheetTabRenameActiveFor(second.Id).Should().BeTrue();
            window.Session.Workbook.GetSheet(second.Id)!.Name.Should().Be("Second");
        });

    [Fact]
    public Task RightPressOnInactiveSheetTabSelectsItAndOpensItsMenuOnTheRefreshedTab() =>
        Run(window =>
        {
            var first = window.Session.Workbook.Sheets[0];
            var second = window.Session.Workbook.AddSheet("Second");
            window.Session.SelectSheet(first.Id);
            window.Show();

            window.HandleSheetTabRightPress(second.Id).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            window.Session.ActiveSheet.Id.Should().Be(second.Id);
            window.IsSheetTabContextMenuOpenForTest(second.Id).Should().BeTrue();
        });

    [Theory]
    [InlineData(Key.F1, KeyModifiers.Control, true)]
    [InlineData(Key.F1, KeyModifiers.Meta, true)]
    [InlineData(Key.R, KeyModifiers.Meta | KeyModifiers.Alt, true)]
    [InlineData(Key.F1, KeyModifiers.None, false)]
    [InlineData(Key.F1, KeyModifiers.Control | KeyModifiers.Shift, false)]
    [InlineData(Key.R, KeyModifiers.Meta, false)]
    public void RibbonVisibilityShortcut_MatchesCtrlAndMacCommandChords(Key key, KeyModifiers modifiers, bool expected) =>
        MainWindow.IsToggleRibbonVisibilityShortcut(key, modifiers).Should().Be(expected);

    [Fact]
    public Task CtrlF1AndCmdF1CollapseThenRestoreTheRibbonContent() =>
        Run(async window =>
        {
            window.Show();
            var tabControl = window.GetVisualDescendants().OfType<TabControl>()
                .First(tab => tab.Items.Count > 3);
            var expandedMinHeight = tabControl.MinHeight;

            var collapse = new KeyEventArgs { Key = Key.F1, KeyModifiers = KeyModifiers.Control };
            await window.RaiseKeyDownForTest(collapse);

            collapse.Handled.Should().BeTrue();
            window.IsRibbonContentCollapsed.Should().BeTrue();
            tabControl.MinHeight.Should().Be(0);

            var restore = new KeyEventArgs { Key = Key.F1, KeyModifiers = KeyModifiers.Meta };
            await window.RaiseKeyDownForTest(restore);

            restore.Handled.Should().BeTrue();
            window.IsRibbonContentCollapsed.Should().BeFalse();
            tabControl.MinHeight.Should().Be(expandedMinHeight);
        });

    [Fact]
    public Task ShiftSpaceKeepsTheActiveCellInsideTheExpandedSelection() =>
        Run(window =>
        {
            var sheet = window.Session.ActiveSheet;
            var range = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 3));
            window.Session.SelectAnchoredRange(new CellAddress(sheet.Id, 4, 3), new CellAddress(sheet.Id, 2, 2));
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 3));

            window.SelectWholeRowsFromSelectionForTest();

            window.Session.SelectedRange.Start.Col.Should().Be(1u);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 3));
            range.Contains(window.Session.ActiveCell).Should().BeTrue();
        });

    private static Task Run(Func<MainWindow, Task> test) =>
        Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                await test(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);

    private static Task Run(Action<MainWindow> test) =>
        Run(window =>
        {
            test(window);
            return Task.CompletedTask;
        });
}
