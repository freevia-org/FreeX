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
                SetVerticalScrollValue(window, 50);

                InvokeScrollShift(window, "ShiftScrollOriginForRowEdit", editIndex: 1, delta: 1);

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

                SetVerticalScrollValue(window, 50);

                InvokeScrollShift(window, "ShiftScrollOriginForRowEdit", editIndex: 1, delta: -1);

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

                SetVerticalScrollValue(window, 50);

                InvokeScrollShift(window, "ShiftScrollOriginForRowEdit", editIndex: 100, delta: 1);

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

                SetHorizontalScrollValue(window, 10);

                InvokeScrollShift(window, "ShiftScrollOriginForColEdit", editIndex: 1, delta: 1);

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

                SetHorizontalScrollValue(window, 10);

                InvokeScrollShift(window, "ShiftScrollOriginForColEdit", editIndex: 1, delta: -1);

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

    private static void SetVerticalScrollValue(MainWindow window, double value)
    {
        window.VerticalScroll.Maximum = Math.Max(window.VerticalScroll.Maximum, value);
        window.VerticalScroll.Value = value;
    }

    private static void SetHorizontalScrollValue(MainWindow window, double value)
    {
        window.HorizontalScroll.Maximum = Math.Max(window.HorizontalScroll.Maximum, value);
        window.HorizontalScroll.Value = value;
    }

    private static void InvokeScrollShift(MainWindow window, string methodName, uint editIndex, int delta)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(uint), typeof(int)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [editIndex, delta]);
        R49MainWindowTestHarness.PumpDispatcher();
    }
}
