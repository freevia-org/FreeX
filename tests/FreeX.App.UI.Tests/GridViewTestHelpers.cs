using System.Reflection;
using System.Threading;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

internal static class WpfTestThread
{
    public static void Run(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null)
            throw exception;
    }
}

internal static class GridViewTestHelpers
{
    public static ViewportModel CreateTwoByTwoViewport(
        uint startRow = 1,
        uint startColumn = 1,
        double rowHeight = 24,
        double columnWidth = 80) =>
        new(
            [],
            [
                new RowMetric(startRow, rowHeight, 0),
                new RowMetric(startRow + 1, rowHeight, rowHeight)
            ],
            [
                new ColMetric(startColumn, columnWidth, 0),
                new ColMetric(startColumn + 1, columnWidth, columnWidth)
            ]);

    public static (Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor) HitTestDrawingObject(
        GridView grid,
        Point point) =>
        ((Guid Id, ObjectKind Kind, Rect Rect, CellAddress Anchor))InvokePrivate(grid, "HitTestDrawingObject", point);

    public static object HitTestObjectHandle(GridView grid, Point point, Rect rect) =>
        InvokePrivate(grid, "HitTestObjectHandle", point, rect);

    public static Rect GetSelectedObjectRect(GridView grid) =>
        InvokePrivate(grid, "GetSelectedObjectRect").Should().BeOfType<Rect>().Subject;

    public static CellAddress? GetSelectedObjectAnchor(GridView grid) =>
        InvokePrivate(grid, "GetSelectedObjectAnchor") is CellAddress anchor ? anchor : null;

    private static object InvokePrivate(GridView grid, string methodName, params object[] arguments)
    {
        var method = typeof(GridView).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(grid, arguments)!;
    }
}
