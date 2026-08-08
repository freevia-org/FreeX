using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R80: clearing a drawing selection is also a cell-context transition. The active cell does not move
/// when commands such as View, Page Layout, and object commands clear the drawing selection, so table and
/// pivot contextual tabs must be recomputed from that unchanged active cell, matching WPF.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R80_DrawingClearContextualSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly MethodInfo ClearSelectedDrawingObject =
        typeof(MainWindow).GetMethod("ClearSelectedDrawingObject", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(MainWindow).FullName, "ClearSelectedDrawingObject");

    [Fact]
    public async Task ClearDrawingSelection_ReevaluatesActiveTableContext()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var table = new StructuredTableModel
                {
                    Id = 1,
                    Name = "ContextTable",
                    Range = Range(sheet.Id, 1, 1, 4, 3),
                };
                sheet.StructuredTables.Add(table);
                window.Session.SelectCell(table.Range.Start);

                window.RibbonContextStateForTest.IsActive("table.active").Should().BeFalse(
                    "the active-cell context is intentionally stale before the clear transition");

                ClearSelectedDrawingObject.Invoke(window, null);

                window.RibbonContextStateForTest.IsActive("table.active").Should().BeTrue(
                    "clearing a drawing selection must restore Table Design for the unchanged active cell");
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
    public async Task ClearDrawingSelection_ReevaluatesActivePivotContext()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.ActiveSheet;
                var pivot = new PivotTableModel
                {
                    Name = "ContextPivot",
                    SourceRange = Range(sheet.Id, 1, 1, 4, 3),
                    TargetRange = Range(sheet.Id, 1, 5, 4, 7),
                };
                sheet.PivotTables.Add(pivot);
                window.Session.SelectCell(pivot.TargetRange.Start);

                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeFalse(
                    "the active-cell context is intentionally stale before the clear transition");

                ClearSelectedDrawingObject.Invoke(window, null);

                window.RibbonContextStateForTest.IsActive("pivot.active").Should().BeTrue(
                    "clearing a drawing selection must restore PivotTable tabs for the unchanged active cell");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
