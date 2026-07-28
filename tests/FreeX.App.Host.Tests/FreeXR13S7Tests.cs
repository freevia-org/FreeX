using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-13 fix bucket S7 — R13-crosscutting-perf-mem-2: Shift+F9 ("Calculate Sheet") must bump the
/// navigation-cache revision the same way the full-workbook "Calculate Now" / F9 path already does
/// (RecalculateWorkbook calls InvalidateNavigationCaches, MainWindow.WorkbookUiState.cs). Before the
/// fix, CalcSheetBtn_Click called RecalcEngine.RecalculateSheetFormulas + UpdateViewport but never
/// InvalidateNavigationCaches, so the status-bar aggregate cache (and the sparkline cache, keyed on
/// the same _navigationCacheRevision) kept serving pre-recalculation values after a sheet-scoped
/// recalculation changed cell values.
/// </summary>
public sealed class FreeXR13S7Tests
{
    [Fact]
    public void CalcSheetBtnClick_AfterCellValueChanges_InvalidatesStatusBarStatsCache()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                options: CreateStatusReadoutOptions());

            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher();

                var sheet = workbook.GetSheetAt(0);
                var cellAddress = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(cellAddress, new NumberValue(10));

                var grid = (SheetGridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(cellAddress, cellAddress);
                PumpDispatcher();

                var cache = GetStatusBarStatsCache(window);
                var revision = GetNavigationCacheRevision(window);
                var range = new GridRange(cellAddress, cellAddress);
                var primedStats = cache.GetOrCalculate(sheet, range, revision);
                primedStats.Sum.Should().Be(10, "priming the status-bar stats cache with the original value");

                // Simulate what a sheet recalculation does to a formula cell's value: the underlying
                // cell value changes WITHOUT going through the normal edit commands that themselves
                // invalidate navigation caches (mirroring RAND()-style volatility recalculated by
                // Shift+F9 / RecalcEngine.RecalculateSheetFormulas).
                sheet.SetCell(cellAddress, new NumberValue(999));
                cache.GetOrCalculate(sheet, range, revision).Sum.Should().Be(10,
                    "without a navigation-cache invalidation the cached status-bar aggregate would stay stale");

                InvokeClickHandler(window, "CalcSheetBtn_Click");
                PumpDispatcher();

                var refreshedRevision = GetNavigationCacheRevision(window);
                refreshedRevision.Should().BeGreaterThan(revision);

                cache.GetOrCalculate(sheet, range, refreshedRevision).Sum.Should().Be(999,
                    "Shift+F9 (Calculate Sheet) must invalidate the navigation caches (like the full " +
                    "Calculate Now / F9 path already does) so the status bar doesn't keep serving the " +
                    "pre-recalculation cached aggregate");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static FreeXOptions CreateStatusReadoutOptions() => new()
    {
        StatusBarShowSum = true
    };

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }

    private static StatusBarStatsCache GetStatusBarStatsCache(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_statusBarStatsCache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("MainWindow should keep a status-bar stats cache keyed by navigation revision");
        return (StatusBarStatsCache)field!.GetValue(window)!;
    }

    private static ulong GetNavigationCacheRevision(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_navigationCacheRevision", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("MainWindow should expose its navigation-cache revision to cache-dependent tests");
        return (ulong)field!.GetValue(window)!;
    }
}
