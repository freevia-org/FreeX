using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-171 fix wave for the round-170 SUSPEND state (db2002e417 / MainWindow.Editing.cs's
/// <c>_inlineEditorAnchorOffscreen</c>): scrolling the anchor cell of an in-progress formula
/// off-screen suspends the in-cell editor instead of force-committing it, so "type '=', scroll
/// away, click a distant cell" still works. Two reviewers independently found the suspended state
/// had no lifecycle:
///
/// meta F1: <c>SuspendInlineEditorForOffscreenAnchor</c> collapses <c>_inlineEditor</c> while it
/// holds keyboard focus and never reclaims focus anywhere. WPF cannot keep focus on a Collapsed
/// element, so the very next keystroke (Escape/Enter/F9) lands on <c>MainWindow_KeyDown</c>'s
/// generic fallback instead of the formula-aware <c>InlineEditor_KeyDown</c>/<c>FormulaBar_KeyDown</c>,
/// leaving the suspended edit dangling.
///
/// sweep109 F1: nothing on any edit-ABANDONING path (Escape included) ever clears
/// <c>_inlineEditorAnchorOffscreen</c> back to false, so the flag survives into a later, unrelated
/// edit. The next <c>RefreshInlineEditorPosition</c> pass on that later edit sees the stale flag and
/// forces the in-cell editor open over a cell the user never asked to edit in-cell.
///
/// Both are one root cause: the suspended state needs a defined beginning (hand focus to the
/// formula bar, which is already the edit's surface of record while suspended) and a defined end
/// (every path that ends an edit -- centrally, <c>ClearFormulaRangeEntryState</c> -- must clear the
/// flag). These tests exercise the actual keyboard routing (a real Escape delivered to whatever
/// element currently holds <see cref="Keyboard.FocusedElement"/>, exactly like WPF's own input
/// pipeline) rather than reflecting into the key handler directly, so a focus regression cannot
/// hide behind a call made straight to the "right" handler.
/// </summary>
public sealed class R171_InlineEditorOffscreenSuspendLifecycleTests
{
    [Fact]
    public void SuspendedFormulaEdit_EscapeRoutesToFormulaBarAndFullyEndsTheEdit()
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

                // Scroll the anchor cell entirely out of view while a formula is being entered --
                // suspends the in-cell editor instead of committing (r170).
                SetVerticalScrollValue(window, 1500);

                GetFormulaEditCell(window).Should().Be(addr,
                    "the edit must still be suspended, not ended, right after the scroll");

                // meta F1: the collapsed in-cell editor cannot keep keyboard focus, so something
                // must have explicitly reclaimed it onto the formula bar (the documented surface of
                // record for a suspended edit) rather than leaving it to land wherever WPF defaults.
                Keyboard.FocusedElement.Should().BeSameAs(window.FormulaBar,
                    "suspending the in-cell editor must hand keyboard focus to the formula bar so " +
                    "the very next keystroke still reaches formula-aware key handling instead of " +
                    "MainWindow_KeyDown's generic fallback");

                // Press Escape by delivering it to whichever element actually holds focus right
                // now -- the same tunnel-then-bubble routing a real keypress goes through -- instead
                // of invoking a specific handler by name.
                SimulateKeyPress(window, Key.Escape);

                GetFormulaEditCell(window).Should().BeNull(
                    "Escape delivered through real keyboard routing must reach formula-aware " +
                    "handling and fully end the suspended edit, not fall through to the generic " +
                    "window handler that leaves the formula edit dangling");
                GetInlineEditorAnchorOffscreen(window).Should().BeFalse(
                    "ending the suspended edit (by any route) must clear the suspend flag so it " +
                    "cannot resurrect the in-cell editor over a later, unrelated edit");
                inlineEditor.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void SuspendedFormulaEdit_EnterRoutesToFormulaBarAndCommitsTheEdit()
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
                inlineEditor!.Text = "=1+1";

                SetVerticalScrollValue(window, 1500);
                GetFormulaEditCell(window).Should().Be(addr);
                Keyboard.FocusedElement.Should().BeSameAs(window.FormulaBar);

                SimulateKeyPress(window, Key.Enter);

                sheet.GetCell(addr)?.Value.Should().Be(new NumberValue(2),
                    "Enter delivered through real keyboard routing while suspended must reach " +
                    "formula-aware handling and commit the formula, not silently miss it");
                GetFormulaEditCell(window).Should().BeNull();
                GetInlineEditorAnchorOffscreen(window).Should().BeFalse();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    /// <summary>
    /// Sibling/no-regression case, reproducing sweep109 F1's exact gesture: a suspended formula
    /// edit is abandoned (Escape), then a wholly unrelated, ordinary edit is started directly in
    /// the formula bar (the mode that deliberately keeps the in-cell editor hidden) on a different,
    /// on-screen cell. A scroll pass must not resurrect the in-cell editor over that later edit.
    /// </summary>
    [Fact]
    public void AbandonedSuspendedEdit_DoesNotResurfaceInlineEditorOverALaterUnrelatedEdit()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 2000; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                var firstAddr = new CellAddress(sheet.Id, 5, 2);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", firstAddr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", firstAddr, (double?)null);
                var inlineEditor = GetInlineEditor(window);
                inlineEditor!.Text = "=SUM(";

                SetVerticalScrollValue(window, 1500);
                GetInlineEditorAnchorOffscreen(window).Should().BeTrue(
                    "the first edit's anchor scrolling off-screen mid-formula must suspend it");

                SimulateKeyPress(window, Key.Escape);
                GetFormulaEditCell(window).Should().BeNull("the first edit was abandoned");

                // Scroll back to a normal baseline before starting the second, unrelated edit.
                SetVerticalScrollValue(window, 0);

                // Start a completely different, ordinary edit on another on-screen cell directly in
                // the formula bar -- the mode that deliberately leaves the in-cell editor hidden
                // (mirrors clicking straight into the formula bar / pressing F2 with no click).
                var secondAddr = new CellAddress(sheet.Id, 2, 3);
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", secondAddr);
                R49MainWindowTestHarness.Invoke(window, "EditActiveCellInFormulaBar");
                window.FormulaBar.Text = "unrelated text";

                GetFormulaEditCell(window).Should().Be(secondAddr);
                (GetInlineEditor(window)?.IsVisible == true).Should().BeFalse(
                    "editing directly in the formula bar must not open the in-cell editor");

                // A scroll pass (any UpdateViewport) while the second edit is live and its cell is
                // still on screen.
                SetVerticalScrollValue(window, 1);

                (GetInlineEditor(window)?.IsVisible == true).Should().BeFalse(
                    "a stale suspend flag left over from the abandoned first edit must not force " +
                    "the in-cell editor open over this unrelated second edit");
                window.SheetGrid.EditingCell.Should().NotBe(secondAddr,
                    "the second edit never asked for the in-cell editor, so SheetGrid.EditingCell " +
                    "must not be forced onto it by leftover suspend-state plumbing");
                window.FormulaBar.Text.Should().Be("unrelated text",
                    "the second edit's own text must be undisturbed");
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

    /// <summary>
    /// Delivers a key press exactly the way WPF's real input pipeline does: tunnel
    /// (PreviewKeyDown) from the currently focused element, then -- only if nothing handled it --
    /// bubble (KeyDown) from that same element. Using whatever element actually holds
    /// <see cref="Keyboard.FocusedElement"/>, instead of invoking a named handler directly, is what
    /// makes this sensitive to a focus regression: if suspending left focus somewhere that isn't
    /// formula-aware, this reaches the same generic fallback a real user's keypress would.
    /// </summary>
    private static void SimulateKeyPress(MainWindow window, Key key)
    {
        var focused = Keyboard.FocusedElement as UIElement;
        focused.Should().NotBeNull("a key press needs some element to hold keyboard focus");

        var presentationSource = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("Window has no PresentationSource.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, Environment.TickCount, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent
        };
        focused!.RaiseEvent(args);
        if (!args.Handled)
        {
            args.RoutedEvent = UIElement.KeyDownEvent;
            focused.RaiseEvent(args);
        }

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

    private static bool GetInlineEditorAnchorOffscreen(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditorAnchorOffscreen", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditorAnchorOffscreen");
        return (bool)field.GetValue(window)!;
    }
}
