using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R75-render-selection-marquee-4-1
/// (<c>MainWindow.CellsCommands.cs</c>): unlike an ordinary cell edit or Delete/Clear Contents
/// (both fixed in R54, see <see cref="R54_ClipboardMarqueeAndCutMoveTests"/>), Insert/Delete
/// Rows/Columns/Cells never cancelled an active Copy/Cut marching-ants marquee -- leaving a stale
/// marquee AND, worse, letting a subsequent Cut+Paste use the STALE pre-shift
/// <c>clip.SourceRange</c>, moving the wrong cells. Every structural Insert/Delete path must now
/// clear the marquee and null the internal clipboard, mirroring the R54 fix.
/// </summary>
public sealed class R75_InsertDeleteClipboardMarqueeTests
{
    [Fact]
    public void CopyThenInsertRow_ClearsClipboardMarqueeAndInternalClipboard()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b2 = new CellAddress(sheet.Id, 2, 2);
                sheet.SetCell(a1, new NumberValue(1));
                sheet.SetCell(b2, new NumberValue(4));

                SetSelectedRange(window, new GridRange(a1, b2));
                InvokeClickHandler(window, "CopyBtn_Click");

                window.SheetGrid.ClipboardRange.Should().NotBeNull("Copy must start an active marching-ants marquee");

                // Insert a row somewhere on the sheet -- an unrelated structural edit.
                var row5 = new CellAddress(sheet.Id, 5, 1);
                SetSelectedRange(window, new GridRange(row5, row5));
                InvokeClickHandler(window, "InsertRowBtn_Click");

                window.SheetGrid.ClipboardRange.Should().BeNull(
                    "Insert Row must cancel an active Copy/Cut marquee just like an ordinary cell edit " +
                    "or Delete/Clear Contents (R75-render-selection-marquee-4-1)");
                GetInternalClipboard(window).Should().BeNull(
                    "the stale internal clipboard payload must also be dropped so a later Paste cannot " +
                    "silently use the pre-shift source range");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CutThenInsertRow_ThenDeleteColumn_ClearsClipboardMarqueeOnEachStructuralEdit()
    {
        // Covers both Insert and Delete structural paths, and a Cut (not just Copy) marquee.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(a1, new NumberValue(42));

                SetSelectedRange(window, new GridRange(a1, a1));
                InvokeClickHandler(window, "CutBtn_Click");
                window.SheetGrid.ClipboardRange.Should().NotBeNull("Cut must start an active marching-ants marquee");
                window.SheetGrid.ClipboardIsCut.Should().BeTrue();

                var row3 = new CellAddress(sheet.Id, 3, 1);
                SetSelectedRange(window, new GridRange(row3, row3));
                InvokeClickHandler(window, "InsertRowBtn_Click");

                window.SheetGrid.ClipboardRange.Should().BeNull(
                    "Insert Row must cancel an active Cut marquee (R75-render-selection-marquee-4-1)");
                GetInternalClipboard(window).Should().BeNull();

                // Re-arm a Cut marquee and confirm Delete Column also clears it.
                SetSelectedRange(window, new GridRange(a1, a1));
                InvokeClickHandler(window, "CutBtn_Click");
                window.SheetGrid.ClipboardRange.Should().NotBeNull();

                var col3 = new CellAddress(sheet.Id, 1, 3);
                SetSelectedRange(window, new GridRange(col3, col3));
                InvokeClickHandler(window, "DeleteColBtn_Click");

                window.SheetGrid.ClipboardRange.Should().BeNull(
                    "Delete Column must cancel an active Cut marquee (R75-render-selection-marquee-4-1)");
                GetInternalClipboard(window).Should().BeNull();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CopyThenPaste_WithNoStructuralEditInBetween_StillPastesNormally()
    {
        // Sibling no-regression: a plain copy-paste with no intervening structural edit must be
        // completely unaffected by the new clipboard-marquee cancellation.
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var c3 = new CellAddress(sheet.Id, 3, 3);
                sheet.SetCell(a1, new NumberValue(7));

                SetSelectedRange(window, new GridRange(a1, a1));
                InvokeClickHandler(window, "CopyBtn_Click");
                window.SheetGrid.ClipboardRange.Should().NotBeNull();

                SetSelectedRange(window, new GridRange(c3, c3));
                InvokeClickHandler(window, "PasteBtn_Click");

                sheet.GetCell(c3)!.Value.Should().Be(
                    new NumberValue(7),
                    "an ordinary copy-paste with no structural edit in between must still work normally");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetSelectedRange(MainWindow window, GridRange range)
    {
        window.SheetGrid.SelectedRanges = null;
        window.SheetGrid.SelectedRange = range;
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(object), typeof(System.Windows.RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new System.Windows.RoutedEventArgs()]);
        R49MainWindowTestHarness.PumpDispatcher();
    }

    private static object? GetInternalClipboard(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
                "_workbookClipboardSession",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
        return ((WorkbookClipboardSession)field.GetValue(window)!).Content;
    }
}
