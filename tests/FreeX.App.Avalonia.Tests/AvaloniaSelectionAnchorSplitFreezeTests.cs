using System;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// split-creation-selection-anchor parity: the WPF host keeps a persistent <c>_selectionAnchor</c>
/// (the cell the user first clicked before dragging / Shift-extending), which View &gt; Split and
/// Freeze Panes resolve against instead of the selection's normalized top-left. The Avalonia shell
/// used to store that anchor only in gesture-scoped fields that were cleared the moment the drag /
/// extend ended, so by the time a ribbon command ran only the collapsed top-left <c>ActiveCell</c>
/// remained. These tests drive a real bottom-right → top-left drag (and the equivalent keyboard
/// Shift-extend) and assert Split / Freeze land on the drag's start cell, not the top-left corner.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaSelectionAnchorSplitFreezeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PointerDragBottomRightToTopLeft_ThenSplit_SplitsAtDragStartCell()
    {
        await Run((window, sheet) =>
        {
            var anchor = new CellAddress(sheet.Id, 5, 3);   // C5 (first click)
            var cursor = new CellAddress(sheet.Id, 1, 1);   // A1 (drag end)

            window.RaiseCellSelectionDragForTest(anchor, cursor);

            window.Session.SelectedRange.Should().Be(new GridRange(anchor, cursor));   // A1:C5
            window.Session.ActiveCell.Should().Be(anchor);                             // C5, not A1

            window.InvokeSplitPanesAtActiveCellForTest();

            sheet.SplitRow.Should().Be(5u);
            sheet.SplitColumn.Should().Be(3u);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PointerDragBottomRightToTopLeft_ThenFreeze_FreezesAtDragStartCell()
    {
        await Run((window, sheet) =>
        {
            window.RaiseCellSelectionDragForTest(
                new CellAddress(sheet.Id, 5, 3),    // C5
                new CellAddress(sheet.Id, 1, 1));   // A1

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 5, 3));
            window.Session.FreezePanesAtActiveCell().Success.Should().BeTrue();

            sheet.FrozenRows.Should().Be(4u);
            sheet.FrozenCols.Should().Be(2u);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task KeyboardShiftExtendUpAndLeft_ThenSplit_SplitsAtExtendStartCell()
    {
        await Run(async (window, sheet) =>
        {
            var start = new CellAddress(sheet.Id, 5, 3);   // C5
            window.Session.SelectCell(start);

            await Press(window, Key.Up, KeyModifiers.Shift);
            await Press(window, Key.Up, KeyModifiers.Shift);
            await Press(window, Key.Up, KeyModifiers.Shift);
            await Press(window, Key.Up, KeyModifiers.Shift);
            await Press(window, Key.Left, KeyModifiers.Shift);
            await Press(window, Key.Left, KeyModifiers.Shift);

            window.Session.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                start));                                    // A1:C5
            window.Session.ActiveCell.Should().Be(start);   // still C5

            window.InvokeSplitPanesAtActiveCellForTest();
            sheet.SplitRow.Should().Be(5u);
            sheet.SplitColumn.Should().Be(3u);
        });
    }

    // No-regression: a plain programmatic SelectRange still normalizes and pins the active cell to
    // the range's top-left Start regardless of the corner order it is handed -- unchanged by this
    // fix, which only alters the dedicated anchored-selection path.
    [Fact]
    public async Task PlainSelectRange_WithReversedCorners_StillCollapsesActiveCellToTopLeft()
    {
        await Run((window, sheet) =>
        {
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 5, 3),    // C5 passed first...
                new CellAddress(sheet.Id, 1, 1)));  // ...A1 second
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 1));   // still A1
            return Task.CompletedTask;
        });
    }

    private static async Task Run(Func<MainWindow, Sheet, Task> test)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("AnchorSplitFreeze");
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
