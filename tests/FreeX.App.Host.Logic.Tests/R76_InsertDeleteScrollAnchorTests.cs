using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R76-render-freeze-scroll-4-1 (MainWindow.CellsCommands.cs's
/// InsertRows/InsertColumns/DeleteSelectedRows/DeleteSelectedColumns, plus MainWindow.Viewport.cs's
/// new ShiftScrollOriginForRowEdit/ShiftScrollOriginForColEdit): inserting/deleting rows or columns
/// AT OR ABOVE the current scrolled view did not adjust the scroll origin, so the viewport kept
/// showing the SAME worksheet row/col numbers even though their CONTENT had just shifted -- the
/// view visibly jumped to different data even though nothing scrolled. Excel instead keeps the
/// same content on screen by shifting the scrollbar anchor by the inserted/deleted count. An edit
/// strictly below/right of the view must not move it.
/// </summary>
public sealed class R76_InsertDeleteScrollAnchorTests
{
    [Fact]
    public void InsertRowAboveView_ShiftsViewTopRowByInsertedCount_KeepsSameContentVisible()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                // Scrolled so the view's first (top) row is 50 (e.g. rows 50-80 visible).
                window.VerticalScroll.Value = 50;

                // Insert 1 row above row 1 (above the view).
                var row1 = new CellAddress(sheet.Id, 1, 1);
                SetSelectedRange(window, new GridRange(row1, row1));
                InvokeClickHandler(window, "InsertRowBtn_Click");

                window.VerticalScroll.Value.Should().Be(50 + 1,
                    "inserting 1 row above the view must shift the scroll anchor by +1 " +
                    "so the same content stays visible (was row 50, now row 51)");
                sheet.ViewTopRow.Should().Be(51u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void DeleteRowAboveView_ShiftsViewTopRowByDeletedCount()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                window.VerticalScroll.Value = 50;

                var row1 = new CellAddress(sheet.Id, 1, 1);
                SetSelectedRange(window, new GridRange(row1, row1));
                InvokeClickHandler(window, "DeleteRowBtn_Click");

                window.VerticalScroll.Value.Should().Be(50 - 1,
                    "deleting 1 row above the view must shift the scroll anchor by -1 " +
                    "so the same content stays visible (was row 50, now row 49)");
                sheet.ViewTopRow.Should().Be(49u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void InsertRowBelowView_DoesNotMoveScrollOrigin()
    {
        // Sibling no-regression: an edit strictly below the view must leave the scroll anchor
        // untouched -- only an edit at/above the view relocates it.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                window.VerticalScroll.Value = 50;

                var row100 = new CellAddress(sheet.Id, 100, 1);
                SetSelectedRange(window, new GridRange(row100, row100));
                InvokeClickHandler(window, "InsertRowBtn_Click");

                window.VerticalScroll.Value.Should().Be(50,
                    "inserting a row BELOW the current view must not move the scroll anchor");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void InsertColumnLeftOfView_ShiftsViewLeftColByInsertedCount()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var col = 1; col <= 60; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)col), new NumberValue(col));

                window.HorizontalScroll.Value = 10;

                var col1 = new CellAddress(sheet.Id, 1, 1);
                SetSelectedRange(window, new GridRange(col1, col1));
                InvokeClickHandler(window, "InsertColBtn_Click");

                window.HorizontalScroll.Value.Should().Be(10 + 1,
                    "inserting 1 column left of the view must shift the scroll anchor by +1");
                sheet.ViewLeftCol.Should().Be(11u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void DeleteColumnLeftOfView_ShiftsViewLeftColByDeletedCount()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                for (var col = 1; col <= 60; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)col), new NumberValue(col));

                window.HorizontalScroll.Value = 10;

                var col1 = new CellAddress(sheet.Id, 1, 1);
                SetSelectedRange(window, new GridRange(col1, col1));
                InvokeClickHandler(window, "DeleteColBtn_Click");

                window.HorizontalScroll.Value.Should().Be(10 - 1,
                    "deleting 1 column left of the view must shift the scroll anchor by -1");
                sheet.ViewLeftCol.Should().Be(9u);
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
}
