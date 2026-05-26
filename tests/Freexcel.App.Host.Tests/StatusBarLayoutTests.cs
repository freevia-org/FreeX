using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Reflection;
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
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void F6StatusBarFocus_StartsAtZoomOutAndTabStaysInZoomControls()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CycleShellFocus(reverse: true);

            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.HandleFocusedStatusBarTab().Should().BeTrue();

            harness.FocusedElementName.Should().Be("ZoomSlider");
        });
    }

    [Fact]
    public void F6ShellFocusCycle_TraversesVisibleExcelRegionsInHost()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Worksheet);

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Ribbon);

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.FormulaBar);
            harness.FocusedElementName.Should().Be("FormulaBar");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.SheetTabs);
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.StatusBar);
            harness.FocusedElementName.Should().Be("StatusZoomOutButton");

            harness.CycleShellFocus(reverse: false);
            harness.CurrentShellFocusTarget.Should().Be(ShellFocusTarget.Worksheet);
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

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _cycleShellFocus;
        private readonly MethodInfo _getCurrentShellFocusTarget;
        private readonly MethodInfo _tryHandleFocusedStatusBarKeyboardNavigation;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _cycleShellFocus = typeof(MainWindow)
                .GetMethod("CycleShellFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CycleShellFocus");
            _getCurrentShellFocusTarget = typeof(MainWindow)
                .GetMethod("GetCurrentShellFocusTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetCurrentShellFocusTarget");
            _tryHandleFocusedStatusBarKeyboardNavigation = typeof(MainWindow)
                .GetMethod("TryHandleFocusedStatusBarKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedStatusBarKeyboardNavigation");
        }

        public ShellFocusTarget CurrentShellFocusTarget =>
            (ShellFocusTarget)_getCurrentShellFocusTarget.Invoke(_window, [])!;

        public string? FocusedElementName =>
            Keyboard.FocusedElement is FrameworkElement element ? element.Name : null;

        public string? FocusedSheetTabName =>
            Keyboard.FocusedElement is FrameworkElement element &&
            element.DataContext is SheetTabViewModel sheetTab
                ? sheetTab.Name
                : null;

        public void CycleShellFocus(bool reverse)
        {
            _cycleShellFocus.Invoke(_window, [reverse]);
            PumpDispatcher();
        }

        public bool HandleFocusedStatusBarTab()
        {
            var source = PresentationSource.FromVisual(_window);
            source.Should().NotBeNull("the test window must be visible before routing keyboard input");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, Key.Tab)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };

            var handled = (bool)_tryHandleFocusedStatusBarKeyboardNavigation.Invoke(_window, [args])!;
            PumpDispatcher();
            return handled;
        }

        public static MainWindowHarness Create()
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
                workbook)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            if (window.FindName("FormulaBarBorder") is FrameworkElement formulaBarBorder)
                formulaBarBorder.Visibility = Visibility.Visible;
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }
}
