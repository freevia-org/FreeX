using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round-14 fix bucket T10 — R14-status-bar-stats-1: the WPF status bar must aggregate over every
/// area of a multi-area (Ctrl+click) selection, not just the last-clicked active rectangle. Before the
/// fix, RefreshStatusBar (MainWindow.GridStatus.cs) only consulted SheetGrid.SelectedRange, so with
/// A1=10, B1=20, C1=30 and a Ctrl+click-built [A1, B1, C1] selection (active range = C1) the status bar
/// showed Sum:30/Count:1/Average:30 instead of Excel's Sum:60/Count:3/Average:20/Min:10/Max:30.
/// </summary>
public sealed class FreeXR14T10Tests
{
    [Fact]
    public void RefreshStatusBar_MultiAreaCtrlClickSelection_AggregatesAcrossAllSelectedAreas()
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
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b1 = new CellAddress(sheet.Id, 1, 2);
                var c1 = new CellAddress(sheet.Id, 1, 3);
                sheet.SetCell(a1, Cell.FromValue(new NumberValue(10)));
                sheet.SetCell(b1, Cell.FromValue(new NumberValue(20)));
                sheet.SetCell(c1, Cell.FromValue(new NumberValue(30)));

                // Mirrors what AddOrMoveAdditionalSelection (MainWindow.Selection.cs) does when the user
                // clicks A1, then Ctrl+clicks B1, then Ctrl+clicks C1: SelectedRanges accumulates every
                // area while SelectedRange tracks only the last (active) one.
                GridRange[] selectedRanges =
                [
                    new GridRange(a1, a1),
                    new GridRange(b1, b1),
                    new GridRange(c1, c1),
                ];
                window.SheetGrid.SelectedRanges = selectedRanges;
                window.SheetGrid.SelectedRange = new GridRange(c1, c1);
                PumpDispatcher();

                InvokeInstanceMethod(window, "RefreshStatusBar");
                PumpDispatcher();

                var cache = GetStatusBarStatsCache(window);
                var revision = GetNavigationCacheRevision(window);
                var stats = cache.GetOrCalculate(sheet, selectedRanges, revision);

                stats.Sum.Should().Be(60,
                    "Excel sums every selected area (10+20+30), not just the last-clicked cell");
                stats.Count.Should().Be(3);
                stats.NumericalCount.Should().Be(3);
                stats.Average.Should().Be(20);
                stats.Min.Should().Be(10);
                stats.Max.Should().Be(30);
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
        StatusBarShowAverage = true,
        StatusBarShowCount = true,
        StatusBarShowMinimum = true,
        StatusBarShowMaximum = true,
        StatusBarShowSum = true
    };

    private static void InvokeInstanceMethod(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, []);
        method.Should().NotBeNull($"{methodName} should exist as a private instance method on MainWindow");
        method!.Invoke(window, []);
    }

    private static StatusBarStatsCache GetStatusBarStatsCache(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_statusBarStatsCache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("MainWindow should keep a status-bar stats cache for selected ranges");
        return (StatusBarStatsCache)field!.GetValue(window)!;
    }

    private static ulong GetNavigationCacheRevision(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_navigationCacheRevision", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("MainWindow should key status-bar stats by navigation revision");
        return (ulong)field!.GetValue(window)!;
    }
}
