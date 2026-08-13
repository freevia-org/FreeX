using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R72-services-clipboard-interop-4-1
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecuteCopy/BuildFullRangeViewportForClipboard).
///
/// Before the fix: copying an entire column/row (Ctrl+C on a column/row header -- a GridRange
/// spanning the FULL 1..MaxRow=1,048,576 or 1..MaxCol=16,384 extent) was never clamped to the
/// sheet's actual used range. BuildFullRangeViewportForClipboard sized its viewport request to the
/// RAW selection, so ExecuteCopy materialized up to 1,048,576 DisplayCells and serialized a
/// TSV/CSV/HTML payload with a million rows -- and the internal clipboard's own captured cell list
/// (clip.Cells, used for in-app paste) was equally unbounded.
///
/// After the fix, ExecuteCopy clamps a whole-column/row selection to the sheet's used-range extent
/// (mirroring WorksheetPrintRenderPlanner.ResolveUsedRange) before any of that materializes, so a
/// single-column copy captures only the actual data rows.
/// </summary>
public sealed class R72_ClipboardWholeColumnUsedRangeClampTests
{
    [Fact]
    public void ExecuteCopy_WholeColumn_ClampsInternalClipboardToUsedRangeExtent()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Data occupies A1:C200 -- the sheet's used range.
                for (uint row = 1; row <= 200; row++)
                {
                    sheet.SetCell(new CellAddress(sheetId, row, 1), new NumberValue(row));
                    sheet.SetCell(new CellAddress(sheetId, row, 3), new NumberValue(row * 2));
                }

                // Select the ENTIRE column A, as a Ctrl+C on the column header would.
                var wholeColumnA = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, CellAddress.MaxRow, 1));
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = wholeColumnA;

                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                var sourceRange = GetInternalClipboardSourceRange(window);
                sourceRange.RowCount.Should().Be(
                    200,
                    "the whole-column copy must be clamped to the sheet's used-range row extent (1..200), not the full 1,048,576-row column span");
                sourceRange.ColCount.Should().Be(1, "clamping must only affect the row axis for a single-column copy");
                GetInternalClipboardCellsCount(window).Should().Be(
                    200,
                    "the internal clipboard's captured cell list must also be bounded by the clamp, not one entry per row of the raw 1,048,576-row selection");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an ordinary small explicit selection (the overwhelmingly common case)
    // must be copied completely unchanged -- no clamping applied.
    [Fact]
    public void ExecuteCopy_SmallExplicitSelection_IsNotClamped()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                for (uint row = 1; row <= 3; row++)
                    for (uint col = 1; col <= 3; col++)
                        sheet.SetCell(new CellAddress(sheetId, row, col), new NumberValue(row * 10 + col));

                var a1c3 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = a1c3;

                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                GetInternalClipboardSourceRange(window).Should().Be(
                    a1c3, "a normal bounded A1:C3 copy must be completely unaffected by the used-range clamp");
                GetInternalClipboardCellsCount(window).Should().Be(9);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a whole-column copy on a completely empty sheet has no used range at
    // all, so it must degenerate to a trivial (single-cell) payload rather than the full column.
    [Fact]
    public void ExecuteCopy_WholeColumn_EmptySheet_YieldsTrivialPayload()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var wholeColumnA = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, CellAddress.MaxRow, 1));
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = wholeColumnA;

                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                GetInternalClipboardSourceRange(window).RowCount.Should().Be(
                    1, "an empty sheet has no used range, so the whole-column copy must clamp to a single trivial cell");
                GetInternalClipboardCellsCount(window).Should().Be(1);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static WorkbookClipboardSnapshot GetInternalClipboard(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
                "_workbookClipboardSession",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_workbookClipboardSession");
        var session = (WorkbookClipboardSession)field.GetValue(window)!;
        return session.Content
            ?? throw new InvalidOperationException("The shared clipboard session was empty after ExecuteCopy.");
    }

    private static GridRange GetInternalClipboardSourceRange(MainWindow window)
    {
        return GetInternalClipboard(window).SourceRange;
    }

    private static int GetInternalClipboardCellsCount(MainWindow window)
    {
        return GetInternalClipboard(window).Cells.Count;
    }
}
