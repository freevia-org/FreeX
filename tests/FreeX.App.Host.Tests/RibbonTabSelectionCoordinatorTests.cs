using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonTabSelectionCoordinatorTests
{
    [Fact]
    public void MouseTabSelection_NormalizesImmediatelyAndQueuesSingleFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectRibbonTabByMouse("Data", 900);

            var queued = harness.FallbackDiagnostics;
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.ExecutedCount.Should().Be(0);
            queued.LastMergedWork.Should().Be("NormalizeSurface");
            queued.IsPending.Should().BeTrue();
            harness.ActiveRibbonPanelOverflow.Should().BeLessThanOrEqualTo(1);

            harness.PumpDispatcher();
            harness.FallbackDiagnostics.ExecutedCount.Should().Be(1);
        });
    }

    [Fact]
    public void KeyTipTabSelection_SuppressesSelectionEventAndSkipsAlreadyActiveTab()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectRibbonTabByHeader("Insert", 900);

            var changed = harness.FallbackDiagnostics;
            changed.RequestCount.Should().Be(1);
            changed.PostedCount.Should().Be(1);
            changed.LastMergedWork.Should().Be("NormalizeSurface");

            harness.PumpDispatcher();
            harness.ResetFallbackDiagnostics();
            var contentBefore = harness.VisibleRibbonButtonContentIdentityHashCodes;

            harness.SelectRibbonTabByHeader("Insert", 900);

            harness.FallbackDiagnostics.RequestCount.Should().Be(0);
            harness.VisibleRibbonButtonContentIdentityHashCodes.Should().Equal(contentBefore);
        });
    }

    [Fact]
    public void FileTabBounce_ReturnsHomeAndQueuesSingleFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = RibbonTabSelectionHarness.Create();

            harness.SelectFileTab();

            var queued = harness.FallbackDiagnostics;
            harness.SelectedTabHeader.Should().Be("Home");
            queued.RequestCount.Should().Be(1);
            queued.PostedCount.Should().Be(1);
            queued.LastMergedWork.Should().Be("NormalizeSurface");
        });
    }

    private sealed class RibbonTabSelectionHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _selectRibbonTabByHeader;

        private RibbonTabSelectionHarness(MainWindow window)
        {
            _window = window;
            _selectRibbonTabByHeader = typeof(MainWindow)
                .GetMethod("SelectRibbonTabByHeader", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SelectRibbonTabByHeader");
        }

        public RibbonFallbackDiagnosticsSnapshot FallbackDiagnostics => _window.GetRibbonFallbackDiagnosticsForTests();

        public string? SelectedTabHeader =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem item
                ? item.Header?.ToString()
                : null;

        public IReadOnlyList<int> VisibleRibbonButtonContentIdentityHashCodes =>
            SelectedRibbonContentRoot is { } root
                ? EnumerateVisualDescendants(root)
                    .Concat(EnumerateLogicalDescendants(root))
                    .OfType<Button>()
                    .Where(button => button.IsVisible && button.Content is not null)
                    .Select(button => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(button.Content))
                    .ToList()
                : [];

        public double ActiveRibbonPanelOverflow
        {
            get
            {
                if (ActiveRibbonPanel is not { } panel)
                    return 0;

                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var viewport = FindVisualAncestor<ScrollViewer>(panel)?.ActualWidth;
                if (viewport is null or <= 0)
                    viewport = (_window.FindName("RibbonTabs") as TabControl)?.ActualWidth;

                return panel.DesiredSize.Width - Math.Max(0, (viewport ?? 0) - 4);
            }
        }

        public void SelectRibbonTabByMouse(string header, double width)
        {
            SetWidth(width);
            _window.ResetRibbonFallbackDiagnosticsForTests();
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.UpdateLayout();
        }

        public void SelectRibbonTabByHeader(string header, double width)
        {
            SetWidth(width);
            _window.ResetRibbonFallbackDiagnosticsForTests();
            _selectRibbonTabByHeader.Invoke(_window, [header]);
            _window.UpdateLayout();
        }

        public void SelectFileTab()
        {
            _window.ResetRibbonFallbackDiagnosticsForTests();
            if (_window.FindName("RibbonTabs") is TabControl tabs &&
                _window.FindName("FileTab") is TabItem fileTab)
            {
                tabs.SelectedItem = fileTab;
            }
        }

        public void ResetFallbackDiagnostics() =>
            _window.ResetRibbonFallbackDiagnosticsForTests();

        public void PumpDispatcher()
        {
            _window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        public static RibbonTabSelectionHarness Create()
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
                Array.Empty<IFileAdapter>(),
                workbookRef,
                workbook,
                NullUserMessageService.Instance);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            var harness = new RibbonTabSelectionHarness(window);
            harness.PumpDispatcher();
            harness.SelectRibbonTabByMouse("Home", 1280);
            harness.PumpDispatcher();
            window.ResetRibbonFallbackDiagnosticsForTests();
            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        }

        private void SetWidth(double width)
        {
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        private DependencyObject? SelectedRibbonContentRoot =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem selectedTab
                ? selectedTab.Content as DependencyObject ?? selectedTab
                : null;

        private StackPanel? ActiveRibbonPanel =>
            SelectedRibbonContentRoot is { } root
                ? EnumerateVisualDescendants(root)
                    .Concat(EnumerateLogicalDescendants(root))
                    .OfType<StackPanel>()
                    .Distinct()
                    .Where(panel => panel.Orientation == Orientation.Horizontal &&
                                    panel.Children.OfType<DependencyObject>().Any(RibbonMetadata.IsRibbonGroup))
                    .OrderByDescending(panel => panel.Children.OfType<DependencyObject>().Count(RibbonMetadata.IsRibbonGroup))
                    .FirstOrDefault()
                : null;

        private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;

                foreach (var descendant in EnumerateVisualDescendants(child))
                    yield return descendant;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;

                foreach (var descendant in EnumerateLogicalDescendants(child))
                    yield return descendant;
            }
        }

        private static T? FindVisualAncestor<T>(DependencyObject? element)
            where T : DependencyObject
        {
            while (element is not null)
            {
                if (element is T match)
                    return match;

                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }

            return null;
        }
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
