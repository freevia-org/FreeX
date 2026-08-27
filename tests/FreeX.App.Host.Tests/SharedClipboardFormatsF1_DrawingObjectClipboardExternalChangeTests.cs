using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Free.Shared.AppServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// shared-clipboard-formats F1: before this fix, ExecutePaste checked
/// <c>_drawingObjectClipboard.Content</c> FIRST and pasted it unconditionally whenever present --
/// unlike the cell-range clipboard path (WorkbookClipboardSession.ResolvePaste), which always
/// re-reads the OS clipboard and compares a marker before trusting its own snapshot. So Ctrl+C on
/// a chart/shape/picture/text box, followed by some OTHER application (or even a different,
/// non-FreeX-routed write) replacing the OS clipboard, followed by Ctrl+V back in FreeX, silently
/// repasted the stale drawing object instead of the content that is now actually on the clipboard.
/// These tests drive the real product entry points (CopyBtn_Click/PasteBtn_Click on a live
/// MainWindow) with an InMemoryPlatformClipboard standing in for the OS clipboard, so an "external
/// app" write is modeled by writing to that same shared clipboard instance directly -- exactly what
/// Alt-Tabbing to Notepad and copying text there does to the real OS clipboard.
/// </summary>
public sealed class SharedClipboardFormatsF1_DrawingObjectClipboardExternalChangeTests
{
    [Fact]
    public void PasteAfterExternalClipboardChange_PastesExternalTextInsteadOfStaleCopiedChart()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var clipboard = new InMemoryPlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: clipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales",
                    Left = 10,
                    Top = 10,
                    Width = 240,
                    Height = 160,
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                // 1) Ctrl+C the chart -- populates both the in-process _drawingObjectClipboard and
                // (via the marker fix) the OS clipboard.
                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                // 2) Simulate Alt-Tabbing to another application and copying plain text there: some
                // OTHER writer replaces the OS clipboard directly, with no marker and no image --
                // FreeX's own process never observes this happening, exactly like a real external
                // app's clipboard write.
                clipboard.WriteAsync(new PlatformClipboardContent(Text: "hello from notepad"))
                    .AsTask().GetAwaiter().GetResult();

                // 3) Alt-Tab back to the SAME FreeX window (no Escape, no new Copy) and paste onto a
                // plain cell.
                var targetCell = new CellAddress(sheet.Id, 3, 3);
                grid.SelectedRange = new GridRange(targetCell, targetCell);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.Charts.Should().HaveCount(
                    1,
                    "the stale copied chart must NOT be repasted once the OS clipboard changed underneath it");
                sheet.GetCell(targetCell)?.Value.Should().Be(
                    new TextValue("hello from notepad"),
                    "Paste must use the clipboard's CURRENT content, not the stale in-process chart clip");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    /// <summary>
    /// Sibling/no-regression proof: with NO external clipboard change in between, Ctrl+C then Ctrl+V
    /// on a chart must still duplicate it exactly as before -- the new marker check must not treat a
    /// same-session, untouched object clip as stale.
    /// </summary>
    [Fact]
    public void PasteWithNoExternalClipboardChange_StillDuplicatesTheCopiedChart()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var clipboard = new InMemoryPlatformClipboard();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: clipboard);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var anchorCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(anchorCell, new NumberValue(99));
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
                    Title = "Sales",
                    Left = 10,
                    Top = 10,
                };
                sheet.Charts.Add(chart);

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(anchorCell, anchorCell);
                grid.SelectedObjectId = chart.Id;
                grid.SelectedObjectKind = ObjectKind.Chart;

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                sheet.Charts.Should().HaveCount(2, "Ctrl+V on a copied chart must still duplicate it when nothing else touched the clipboard");
                sheet.Charts.Should().Contain(c => c.Id == chart.Id);
                sheet.GetCell(anchorCell)!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
