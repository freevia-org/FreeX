using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class StatusBarLayoutTests
{
    [Fact]
    public void AggregateViewport_StaysClearOfZoomControlsAtCompactWidth()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook);

            try
            {
                window.Width = 860;
                window.Height = 500;
                window.Show();

                ((TextBlock)window.FindName("StatusReadyText")).Visibility = Visibility.Collapsed;
                var statsPanel = (StackPanel)window.FindName("StatusStatsPanel");
                statsPanel.Visibility = Visibility.Visible;
                ((TextBlock)window.FindName("StatusAvgText")).Text = "Average: 2";
                ((TextBlock)window.FindName("StatusCountText")).Text = "Count: 1";
                ((TextBlock)window.FindName("StatusSumText")).Text = "Sum: 2";
                ((TextBlock)window.FindName("StatusMinText")).Text = "Min: 2";
                ((TextBlock)window.FindName("StatusMaxText")).Text = "Max: 2";

                window.UpdateLayout();
                PumpDispatcher();
                window.UpdateLayout();

                var statsViewport = (FrameworkElement)window.FindName("StatusStatsViewport");
                var zoomControls = (FrameworkElement)window.FindName("StatusZoomControls");
                var maxText = (FrameworkElement)window.FindName("StatusMaxText");

                var viewportBounds = BoundsRelativeToWindow(statsViewport, window);
                var zoomBounds = BoundsRelativeToWindow(zoomControls, window);
                var maxTextBounds = BoundsRelativeToWindow(maxText, window);

                viewportBounds.Right.Should().BeLessThanOrEqualTo(zoomBounds.Left + 0.5);
                maxTextBounds.Right.Should().BeLessThanOrEqualTo(viewportBounds.Right + 0.5);
            }
            finally
            {
                window.Close();
                PumpDispatcher();
            }
        });
    }

    private static Rect BoundsRelativeToWindow(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
