using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R76-render-freeze-scroll-4-1 (Avalonia twin, MainWindow.RibbonMenuWires.cs's
/// InsertSheetRows/InsertSheetColumns/DeleteSheetRows/DeleteSheetColumns): inserting/deleting rows or
/// columns AT OR ABOVE the current scrolled view did not adjust the scroll origin
/// (ActiveSheet.ViewTopRow/ViewLeftCol), so the viewport kept showing the SAME worksheet row/col
/// numbers even though their CONTENT had just shifted -- the view visibly jumped to different data
/// even though nothing scrolled. Excel instead keeps the same content on screen by shifting the
/// anchor by the inserted/deleted count. An edit strictly below/right of the view must not move it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R76_InsertDeleteScrollAnchorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task InsertSheetRows_AboveView_ShiftsViewTopRowByInsertedCount()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                // Scrolled so the view's first (top) row is 50.
                sheet.ViewTopRow = 50;

                var row1 = new CellAddress(sheet.Id, 1, 1);
                window.Session.SelectRange(new GridRange(row1, row1));

                InvokeParameterless(window, "InsertSheetRows");

                sheet.ViewTopRow.Should().Be(51u,
                    "inserting 1 row above the view must shift the scroll anchor by +1 " +
                    "so the same content stays visible (was row 50, now row 51)");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteSheetRows_AboveView_ShiftsViewTopRowByDeletedCount()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                sheet.ViewTopRow = 50;

                var row1 = new CellAddress(sheet.Id, 1, 1);
                window.Session.SelectRange(new GridRange(row1, row1));

                InvokeParameterless(window, "DeleteSheetRows");

                sheet.ViewTopRow.Should().Be(49u,
                    "deleting 1 row above the view must shift the scroll anchor by -1 " +
                    "so the same content stays visible (was row 50, now row 49)");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InsertSheetRows_BelowView_DoesNotMoveScrollOrigin()
    {
        // Sibling no-regression: an edit strictly below the view must leave the scroll anchor
        // untouched -- only an edit at/above the view relocates it.
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                for (var row = 1; row <= 200; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, (uint)row, 1), new NumberValue(row));

                sheet.ViewTopRow = 50;

                var row100 = new CellAddress(sheet.Id, 100, 1);
                window.Session.SelectRange(new GridRange(row100, row100));

                InvokeParameterless(window, "InsertSheetRows");

                sheet.ViewTopRow.Should().Be(50u,
                    "inserting a row BELOW the current view must not move the scroll anchor");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InsertSheetColumns_LeftOfView_ShiftsViewLeftColByInsertedCount()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                for (var col = 1; col <= 60; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)col), new NumberValue(col));

                sheet.ViewLeftCol = 10;

                var col1 = new CellAddress(sheet.Id, 1, 1);
                window.Session.SelectRange(new GridRange(col1, col1));

                InvokeParameterless(window, "InsertSheetColumns");

                sheet.ViewLeftCol.Should().Be(11u,
                    "inserting 1 column left of the view must shift the scroll anchor by +1");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteSheetColumns_LeftOfView_ShiftsViewLeftColByDeletedCount()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow(out var sheet);
            try
            {
                for (var col = 1; col <= 60; col++)
                    sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)col), new NumberValue(col));

                sheet.ViewLeftCol = 10;

                var col1 = new CellAddress(sheet.Id, 1, 1);
                window.Session.SelectRange(new GridRange(col1, col1));

                InvokeParameterless(window, "DeleteSheetColumns");

                sheet.ViewLeftCol.Should().Be(9u,
                    "deleting 1 column left of the view must shift the scroll anchor by -1");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow(out Sheet sheet)
    {
        var window = new MainWindow([]);
        sheet = window.Session.Workbook.AddSheet("R76ScrollAnchorFixture");
        window.Session.SelectSheet(sheet.Id);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        Refresh(window);
        return window;
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static void InvokeParameterless(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, []);
    }
}
