using System.Reflection;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless proof of the macOS chart element selection surface against the WPF grid behaviour:
/// outline instead of whole-chart handles, arrow-key navigation, per-chart geometry, stale-element
/// fallback and Delete leaving the whole chart selected.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaChartElementSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static ChartModel NewChart(MainWindow window, Action<ChartModel>? configure = null)
    {
        var sheet = window.Session.ActiveSheet;
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            ShowLegend = true,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            Left = 20,
            Top = 30,
            Width = 400,
            Height = 300,
        };
        configure?.Invoke(chart);
        sheet.Charts.Add(chart);
        return chart;
    }

    private static Control BuildContainer(MainWindow window, ChartModel chart)
    {
        const double width = 400;
        const double height = 300;
        var plot = new PlotRect(28, 28, width - 56, height - 56);
        bool Accessor(uint row, uint col, out double value, out string text)
        {
            value = row * 10 + col;
            text = row == 1 || col == 1 ? $"L{row}{col}" : value.ToString("0");
            return true;
        }

        var measurer = typeof(MainWindow)
            .GetField("ChartTextMeasurer", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        var request = ChartLayoutRequestBuilder.TryBuild(
            chart, plot, new ChartLayoutRequestBuilder.ChartCellAccessor(Accessor),
            (FreeX.App.Presentation.Text.ITextMeasurer)measurer);
        request.Should().NotBeNull();
        var layout = ChartLayoutEngine.Layout(request!);
        return (Control)typeof(MainWindow)
            .GetMethod("CreateSelectableChartContainer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [chart, layout, new Canvas(), width, height])!;
    }

    private static ChartElementMap LastMap(MainWindow window, ChartModel chart)
    {
        var dict = (System.Collections.IDictionary)typeof(MainWindow)
            .GetField("_lastChartElementMaps", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        return (ChartElementMap)dict[chart.Id]!;
    }

    private static void Select(MainWindow window, ChartModel chart, ChartElementRef element)
    {
        var map = LastMap(window, chart);
        typeof(MainWindow)
            .GetMethod("SelectChartElement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [chart, element, map]);
    }

    private static ChartElementRef? Current(MainWindow window) =>
        (ChartElementRef?)typeof(MainWindow)
            .GetField("_selectedChartElement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window);

    private static bool Navigate(MainWindow window, Key key) =>
        (bool)typeof(MainWindow)
            .GetMethod("TryNavigateSelectedChartElement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [key])!;

    private static async Task RunAsync(Action<MainWindow> body)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                body(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectedInnerElement_ReplacesWholeChartHandlesWithElementOutline()
    {
        await RunAsync(window =>
        {
            var chart = NewChart(window);
            BuildContainer(window, chart);
            var title = new ChartElementRef(ChartElementKind.ChartTitle);
            LastMap(window, chart).Contains(title).Should().BeTrue();

            Select(window, chart, title);
            var container = (Panel)BuildContainer(window, chart);

            var layer = container.Children.OfType<Canvas>().Last();
            // Outline + four corner grips for a single-rectangle element; the 8-handle adorner has 9.
            layer.Children.Should().HaveCount(5);
            Current(window).Should().Be(title);
        });
    }

    [Fact]
    public async Task StaleSelectedElement_FallsBackToWholeChartHandles()
    {
        await RunAsync(window =>
        {
            var chart = NewChart(window, c => c.ShowLegend = true);
            BuildContainer(window, chart);
            Select(window, chart, new ChartElementRef(ChartElementKind.Legend));
            Current(window).Should().NotBeNull();

            chart.ShowLegend = false;
            var container = (Panel)BuildContainer(window, chart);

            Current(window).Should().BeNull("an element that no longer exists must not stay selected");
            container.Children.OfType<Canvas>().Last().Children.Should().HaveCount(9);
        });
    }

    [Fact]
    public async Task ArrowNavigation_UsesTheSelectedChartsOwnGeometryEvenWithSeveralCharts()
    {
        await RunAsync(window =>
        {
            var first = NewChart(window);
            var second = NewChart(window, c => c.Title = "Second");
            BuildContainer(window, first);
            BuildContainer(window, second);

            Select(window, first, new ChartElementRef(ChartElementKind.ChartTitle));
            var before = Current(window);

            Navigate(window, Key.Down).Should().BeTrue(
                "an inner element is selected so the arrow key must navigate instead of nudging the chart");
            Current(window).Should().NotBe(before);
        });
    }

    [Fact]
    public async Task ArrowNavigation_WithoutInnerElement_LeavesKeyForChartNudge()
    {
        await RunAsync(window =>
        {
            var chart = NewChart(window);
            BuildContainer(window, chart);
            Select(window, chart, ChartElementRef.ChartArea);

            Navigate(window, Key.Down).Should().BeFalse();
        });
    }

    [Fact]
    public async Task DeleteOnSelectedTitle_RemovesTitleAndLeavesChartSelected_UndoRestores()
    {
        await RunAsync(window =>
        {
            var chart = NewChart(window);
            BuildContainer(window, chart);
            Select(window, chart, new ChartElementRef(ChartElementKind.ChartTitle));

            typeof(MainWindow)
                .GetMethod("TryDeleteSelectedDrawingObject", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, []).Should().Be(true);

            window.Session.ActiveSheet.Charts.Should().Contain(chart, "Delete on an element must not delete the chart");
            chart.Title.Should().BeNullOrEmpty();
            Current(window).Should().BeNull();

            window.Session.UndoLastEdit().Success.Should().BeTrue();
            chart.Title.Should().Be("Sales");
        });
    }
}
