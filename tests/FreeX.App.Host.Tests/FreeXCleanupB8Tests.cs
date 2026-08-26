using System.Reflection;
using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// P41: Copy must serialize the FULL selected range to the OS clipboard (plain text + CF_HTML),
// not just the on-screen viewport. SheetGrid.Viewport only materializes rows/columns that fit the
// current scroll position (ViewportService.Metrics.BuildFrozenAwareRowMetrics stops once it has
// covered the visible height), so copying a selection taller than the visible window used to place
// blank fields on the OS clipboard for every off-screen row.
public sealed class FreeXCleanupB8Tests
{
    [WindowsClipboardFact]
    public void Copy_SelectionTallerThanViewport_ClipboardTextIncludesOffScreenRows()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);

                // Populate far more rows than a 720px-tall test window can show at once (~30-40
                // default-height rows), so the on-screen viewport is guaranteed to truncate before
                // reaching the bottom of the selection.
                const int rowCount = 300;
                for (uint row = 1; row <= rowCount; row++)
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

                var grid = (GridView)window.FindName("SheetGrid");
                var fullRange = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, rowCount, 1));
                grid.SelectedRange = fullRange;
                PumpDispatcher();

                // Sanity: confirm the on-screen viewport itself does NOT cover the whole selection
                // (otherwise this test would not be exercising the truncation bug at all).
                var viewport = grid.Viewport;
                viewport.Should().NotBeNull();
                var visibleRows = viewport!.Cells.Select(c => c.Row).DefaultIfEmpty(0u).Max();
                visibleRows.Should().BeLessThan((uint)rowCount,
                    "the on-screen viewport must not already materialize every row, or this test would not cover the P41 truncation bug");

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                var clipboardText = System.Windows.Clipboard.GetText();
                var lines = clipboardText.Replace("\r\n", "\n").Split('\n');

                lines.Length.Should().Be(rowCount);
                // The last row is off-screen relative to the test window's viewport and must still
                // be present with its real value, not blank.
                lines[rowCount - 1].Should().Be(rowCount.ToString());
                // Spot-check a middle off-screen row too.
                lines[rowCount / 2].Should().Be((rowCount / 2 + 1).ToString());
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
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
