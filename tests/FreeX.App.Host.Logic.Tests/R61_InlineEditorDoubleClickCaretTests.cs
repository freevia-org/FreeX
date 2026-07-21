using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R61-render-formula-bar-6-2: the WPF host always placed the caret at
/// the END of the text on double-click edit entry, ignoring the clicked pixel position. Real
/// Excel (and FreeX's own Avalonia shell, via CalculateInlineCellCaretIndex) place the caret at
/// the hit-tested position instead.
///
/// src/FreeX.App.Host/MainWindow.Selection.cs's ClickCount==2 branch now threads the double-click
/// pointer X (in SheetGrid coordinate space) through EnterEditMode -> ShowInlineEditor, which
/// hit-tests it against the live inline TextBox via WPF's GetCharacterIndexFromPoint
/// (MainWindow.Editing.cs's new ResolveInlineEditorCaretIndex helper) instead of unconditionally
/// setting CaretIndex = Text.Length.
/// </summary>
public sealed class R61_InlineEditorDoubleClickCaretTests
{
    [Fact]
    public void ShowInlineEditor_WithClickNearCellStart_PlacesCaretNearStart_NotAtEnd()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1); // A1
                var text = "Quarterly Revenue Summary";
                sheet.SetCell(addr, new TextValue(text));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);

                // A click just inside the left edge of the cell's text -- i.e. near the very
                // start of "Quarterly Revenue Summary", not the end.
                double clickX = window.SheetGrid.ActualRowHeaderWidth + 6;

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)clickX);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Text.Should().Be(text);
                inlineEditor.CaretIndex.Should().BeLessThan(
                    text.Length,
                    "a double-click near the start of the text must hit-test the caret near the click " +
                    "position, not hard-code it to the end of the text (R61-render-formula-bar-6-2)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: keyboard-driven edit entry (F2, typing, etc.) supplies no click
    // coordinate and must keep the pre-existing "caret at end" behavior.
    [Fact]
    public void ShowInlineEditor_WithNoClickCoordinate_StillPlacesCaretAtEnd()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1); // A1
                var text = "Quarterly Revenue Summary";
                sheet.SetCell(addr, new TextValue(text));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Text.Should().Be(text);
                inlineEditor.CaretIndex.Should().Be(
                    text.Length,
                    "F2/typed entry (no click coordinate) must keep placing the caret at the end, " +
                    "matching Excel's own keyboard-entry behavior");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static System.Windows.Controls.TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (System.Windows.Controls.TextBox?)field.GetValue(window);
    }
}
