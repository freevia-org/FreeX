using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for freex-cell-editing-modes-F1 (MainWindow.Editing.cs's
/// <c>ShowInlineEditor</c>/new <c>RefreshInlineEditorPosition</c>, wired into
/// MainWindow.Viewport.cs's <c>UpdateViewport</c>): the WPF in-cell editor's floating Canvas
/// position was computed exactly once, from the viewport metrics captured at edit-start time.
/// Scrolling (mouse wheel / scrollbar drag) rebuilds <c>SheetGrid.Viewport</c> with new row/col
/// metrics but never repositioned the still-open editor, so it stayed glued to its original screen
/// pixel position while the grid content scrolled underneath it.
/// </summary>
public sealed class R170_InlineEditorFollowsScrollTests
{
    [Fact]
    public void ScrollingWhileEditing_RepositionsEditorToStayOverItsCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                // Edit a cell that is comfortably inside the initial (unscrolled) viewport.
                var addr = new CellAddress(sheet.Id, 5, 1);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Visibility.Should().Be(System.Windows.Visibility.Visible);

                inlineEditor.Text = "still editing";

                var topBeforeScroll = Canvas.GetTop(inlineEditor);

                // Scroll the view down (mirrors dragging the vertical scrollbar thumb / turning the
                // mouse wheel): row 5 stays inside even a constrained hosted-runner viewport,
                // but its on-screen pixel row shifts upward as the view's top row advances.
                SetVerticalScrollValue(window, 2);

                GetFormulaEditCell(window).Should().Be(addr,
                    "scrolling must not commit/abandon the in-progress edit while the cell is " +
                    "still on screen");
                inlineEditor.Visibility.Should().Be(System.Windows.Visibility.Visible,
                    "the edited cell is still inside the viewport, so the editor must stay open");
                inlineEditor.Text.Should().Be("still editing",
                    "repositioning must not disturb the text the user was mid-typing");

                var topAfterScroll = Canvas.GetTop(inlineEditor);
                topAfterScroll.Should().NotBe(topBeforeScroll,
                    "the editor's on-screen Top must follow the grid content instead of staying " +
                    "glued to its original screen pixel position after a scroll");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: opening the editor and refreshing the viewport WITHOUT any actual
    // scroll (e.g. any other UpdateViewport-triggering event) must not move or disturb it at all --
    // only a genuine change in the viewport's row/col metrics should reposition the editor.
    [Fact]
    public void RefreshingViewportWithoutScrolling_LeavesEditorPositionUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 5, 1);
                sheet.SetCell(addr, new TextValue("Hello"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                var topBefore = Canvas.GetTop(inlineEditor!);
                var leftBefore = Canvas.GetLeft(inlineEditor!);

                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                Canvas.GetTop(inlineEditor!).Should().Be(topBefore);
                Canvas.GetLeft(inlineEditor!).Should().Be(leftBefore);
                inlineEditor!.Visibility.Should().Be(System.Windows.Visibility.Visible);
                GetFormulaEditCell(window).Should().Be(addr);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // The cell being edited can scroll entirely out of the renderable viewport (a large enough
    // scroll). There is no sane on-screen position left to draw the floating editor at, so it must
    // commit the in-progress text and close -- mirroring RefreshTextBoxInlineEditorPosition's
    // identical hide-on-scrolled-away-anchor behavior for the shape/textbox inline editor.
    [Fact]
    public void ScrollingEditedCellCompletelyOutOfView_CommitsAndHidesEditor()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 2000; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                var addr = new CellAddress(sheet.Id, 5, 1);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Text = "will be committed";

                // Scroll far enough that row 5 is nowhere near the viewport any more.
                SetVerticalScrollValue(window, 1500);

                inlineEditor.Visibility.Should().Be(System.Windows.Visibility.Collapsed,
                    "the edited cell scrolled completely out of the renderable viewport, so the " +
                    "floating editor has no valid anchor left and must close");
                sheet.GetCell(addr)!.Value.Should().Be(new TextValue("will be committed"),
                    "closing the editor this way must commit the in-progress text, not discard it");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// r170 remediation. The branch above is right for a value edit and wrong for a formula: "type
    /// '=', scroll away, click a distant cell" is how a reference to an off-screen cell is entered,
    /// and the scroll is part of the gesture. Committing there ends the formula mid-entry, so the
    /// click that follows overwrites a cell instead of adding its reference. During formula range
    /// entry the edit is suspended -- the formula bar carries it -- and restored on scroll back.
    /// </summary>
    [Fact]
    public void ScrollingOutOfViewWhileEnteringAFormula_SuspendsTheEditInsteadOfCommittingIt()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 2000; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                var addr = new CellAddress(sheet.Id, 5, 2);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Text = "=SUM(";

                SetVerticalScrollValue(window, 1500);

                sheet.GetCell(addr).Should().BeNull(
                    "scrolling away while pointing must not commit the half-written formula");
                GetFormulaEditCell(window).Should().Be(addr,
                    "the edit is suspended, not ended -- the next click must still append a reference");
                window.FormulaBar.Text.Should().Be("=SUM(",
                    "the formula bar carries the edit while the anchor is off-screen, which is also " +
                    "what keeps point mode active");

                // Scrolling back must bring the in-cell editor with its text back.
                SetVerticalScrollValue(window, 0);
                inlineEditor.Visibility.Should().Be(System.Windows.Visibility.Visible);
                inlineEditor.Text.Should().Be("=SUM(");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetVerticalScrollValue(MainWindow window, double value)
    {
        window.VerticalScroll.Maximum = Math.Max(window.VerticalScroll.Maximum, value);
        window.VerticalScroll.Value = value;
        R49MainWindowTestHarness.PumpDispatcher();
    }

    private static TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (TextBox?)field.GetValue(window);
    }

    private static CellAddress? GetFormulaEditCell(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_formulaEditCell", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_formulaEditCell");
        return (CellAddress?)field.GetValue(window);
    }
}
