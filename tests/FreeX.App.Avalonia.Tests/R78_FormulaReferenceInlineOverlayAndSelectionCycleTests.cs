using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R78-render-inplace-editor-5-2 (colored formula-reference highlighting must also paint the
/// in-cell editor, not only the formula bar) and R78-render-selection-namebox-5-2 (Enter/Tab must
/// cycle the active cell within an already-selected multi-cell range instead of collapsing the
/// selection) -- both WPF-parity gaps in the Avalonia shell.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R78_FormulaReferenceInlineOverlayAndSelectionCycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── R78-render-inplace-editor-5-2: in-cell reference-highlight overlay ─────────────────────

    [Fact]
    public async Task InlineCellEditor_TypingFormulaWithReferences_ColorsInlineEditorLikeFormulaBar()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(formulaCell);
            window.BeginInlineCellEditForTest(formulaCell, "=A1+B2", 6);

            // Before the fix, only the formula bar's overlay was ever populated -- the in-cell
            // editor's own text stayed plain/uncolored and its Foreground was never suppressed.
            window.InlineCellReferenceOverlayVisibleForTest.Should().BeTrue();
            window.InlineCellReferenceOverlayRunCountForTest.Should().BeGreaterThan(1);
            window.InlineCellEditorForegroundForTest.Should().BeSameAs(Brushes.Transparent);

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Escape));
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineCellEditor_TypingPlainTextWithoutReferences_OverlayStaysHiddenAndForegroundUnchanged()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var formulaCell = new CellAddress(sheet.Id, 5, 5);
            window.Session.SelectCell(formulaCell);
            window.BeginInlineCellEditForTest(formulaCell, "hello", 5);

            window.InlineCellReferenceOverlayVisibleForTest.Should().BeFalse();
            window.InlineCellReferenceOverlayRunCountForTest.Should().Be(0);
            window.InlineCellEditorForegroundForTest.Should().NotBeSameAs(Brushes.Transparent);

            window.RaiseInlineCellEditorKeyDownForTest(Press(Key.Escape));
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }

    // ── Static decision-logic seam (ApplyFormulaReferenceTextOverlayForTest) ───────────────────

    [Fact]
    public void ApplyFormulaReferenceTextOverlay_ColorsEachReferenceRunAndSuppressesEditorForeground()
    {
        var editor = new global::Avalonia.Controls.TextBox { Foreground = Brushes.Black };
        var overlay = new global::Avalonia.Controls.TextBlock();
        var text = "=A1+B2";
        var highlights = new[]
        {
            new FormulaReferenceHighlight(1, 2, 0, "A1", null, null),
            new FormulaReferenceHighlight(4, 2, 1, "B2", null, null),
        };

        MainWindow.ApplyFormulaReferenceTextOverlayForTest(editor, overlay, Brushes.Black, text, highlights);

        overlay.IsVisible.Should().BeTrue();
        overlay.Inlines.Should().NotBeNull();
        // "=" (plain) + "A1" (colored) + "+" (plain) + "B2" (colored) = 4 runs.
        overlay.Inlines!.Count.Should().Be(4);
        editor.Foreground.Should().BeSameAs(Brushes.Transparent);
    }

    [Fact]
    public void ApplyFormulaReferenceTextOverlay_NoHighlights_ClearsOverlayAndRestoresPlainForeground()
    {
        var editor = new global::Avalonia.Controls.TextBox { Foreground = Brushes.Black };
        var overlay = new global::Avalonia.Controls.TextBlock();

        MainWindow.ApplyFormulaReferenceTextOverlayForTest(
            editor, overlay, Brushes.Black, "=SUM(", []); // no references yet

        overlay.IsVisible.Should().BeFalse();
        editor.Foreground.Should().BeSameAs(Brushes.Black);
    }

    // ── R78-render-selection-namebox-5-2: Enter/Tab cycles within a multi-cell selection ───────

    [Fact]
    public async Task MultiCellSelection_Enter_MovesActiveCellWithinRangeAndWrapsAtEdges_KeepingSelectionHighlighted()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 4));
            window.Session.SelectRange(range);
            window.Session.ActiveCell.Should().Be(range.Start);

            var firstEnter = Press(Key.Enter);
            await window.RaiseKeyDownForTest(firstEnter);
            firstEnter.Handled.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 2));
            window.Session.SelectedRange.Should().Be(range);

            var secondEnter = Press(Key.Enter);
            await window.RaiseKeyDownForTest(secondEnter);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 2));
            window.Session.SelectedRange.Should().Be(range);

            // Reached the bottom of the column -- the third Enter wraps back to the range's top
            // row and advances one column, matching Excel, instead of falling off the selection.
            var thirdEnter = Press(Key.Enter);
            await window.RaiseKeyDownForTest(thirdEnter);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 3));
            window.Session.SelectedRange.Should().Be(range);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MultiCellSelection_Tab_MovesActiveCellWithinRangeAndWrapsAtEdges_KeepingSelectionHighlighted()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 4, 4));
            window.Session.SelectRange(range);

            var firstTab = Press(Key.Tab);
            await window.RaiseKeyDownForTest(firstTab);
            firstTab.Handled.Should().BeTrue();
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 3));
            window.Session.SelectedRange.Should().Be(range);

            var secondTab = Press(Key.Tab);
            await window.RaiseKeyDownForTest(secondTab);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 2, 4));
            window.Session.SelectedRange.Should().Be(range);

            // Reached the right edge of the row -- the third Tab wraps back to the range's left
            // column and advances one row, matching Excel.
            var thirdTab = Press(Key.Tab);
            await window.RaiseKeyDownForTest(thirdTab);
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 3, 2));
            window.Session.SelectedRange.Should().Be(range);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MultiAreaSelection_TabAndShiftTab_CycleAcrossAreasWithoutCollapsingSelection()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var first = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2));
            var second = new GridRange(
                new CellAddress(sheet.Id, 1, 4),
                new CellAddress(sheet.Id, 1, 4));
            GridRange[] areas = [first, second];
            window.Session.SelectRanges(second, areas, second.Start);

            await window.RaiseKeyDownForTest(Press(Key.Tab));
            window.Session.ActiveCell.Should().Be(first.Start);
            window.Session.SelectedRange.Should().Be(second);
            window.Session.SelectedRanges.Should().Equal(areas);

            await window.RaiseKeyDownForTest(Press(Key.Tab));
            window.Session.ActiveCell.Should().Be(first.End);
            window.Session.SelectedRanges.Should().Equal(areas);

            await window.RaiseKeyDownForTest(Press(Key.Tab));
            window.Session.ActiveCell.Should().Be(second.Start);
            window.Session.SelectedRanges.Should().Equal(areas);

            window.Session.SelectRanges(first, areas, first.Start);
            await window.RaiseKeyDownForTest(Press(Key.Tab, KeyModifiers.Shift));
            window.Session.ActiveCell.Should().Be(second.End);
            window.Session.SelectedRange.Should().Be(first);
            window.Session.SelectedRanges.Should().Equal(areas);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SingleMergedCellSelection_Enter_StillSkipsPastMergeInsteadOfCyclingWithinIt()
    {
        // No-regression sibling for the new multi-cell-selection guard: a lone selected MERGED
        // cell also satisfies Start != End (it spans multiple rows/cols) but is logically a single
        // cell, not a real multi-cell selection -- Enter must still skip past it (existing
        // AdjustTargetPastMerge behavior) instead of Tab/Enter-cycling through its interior.
        await Session.Dispatch(async () =>
        {
            var window = CreateWindowWithCleanSheet(out var sheet);
            var mergeRange = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 3, 3));
            window.Session.SelectRange(mergeRange);
            window.Session.MergeAndCenterSelectedRange().Success.Should().BeTrue();
            window.Session.SelectedRange.Should().Be(mergeRange);

            var enter = Press(Key.Enter);
            await window.RaiseKeyDownForTest(enter);

            enter.Handled.Should().BeTrue();
            // Skips clean past the merge's far edge (row 3 -> row 4), and collapses the selection
            // down to that single cell -- the ordinary (non-cycling) Enter behavior.
            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 4, 2));
            window.Session.SelectedRange.Should().Be(
                new GridRange(new CellAddress(sheet.Id, 4, 2), new CellAddress(sheet.Id, 4, 2)));

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateWindowWithCleanSheet(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("CleanFixture");
        window.Session.SelectSheet(sheet.Id);
        return window;
    }

    private static KeyEventArgs Press(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        new() { Key = key, KeyModifiers = modifiers };
}
