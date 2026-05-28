using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowSheetTabKeyboardTests
{
    [Fact]
    public void MenuKeyOnFocusedSheetTab_OpensSheetTabContextMenuWithFocusAndAccessKeys()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.OpenFocusedSheetTabContextMenu();

            harness.SheetTabContextMenuIsOpen.Should().BeTrue(harness.DebugSheetTabs);
            harness.SheetTabContextMenuPlacementTargetIsFocusedTab.Should().BeTrue();
            harness.SheetTabMenuItemGestureText("Rename\u2026").Should().Be("R");
            harness.SheetTabMenuItemGestureText("Insert Sheet").Should().Be("I");
            harness.SheetTabMenuItemGestureText("Duplicate").Should().Be("D");
            harness.SheetTabMenuItemGestureText("Tab Color\u2026").Should().Be("T");
        });
    }

    [Fact]
    public void AddSheetGhostTab_SitsBetweenVisibleTabsAndRightNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var window = harness.Window;

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");
            var rightNav = (FrameworkElement)window.FindName("SheetNavRightBtn");

            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var addBounds = BoundsRelativeToWindow(addSheet, window);
            var rightNavBounds = BoundsRelativeToWindow(rightNav, window);

            addBounds.Left.Should().BeLessThan(scrollerBounds.Right, "the ghost tab intentionally overlaps the clipped tab viewport so it reads as the next tab");
            addBounds.Right.Should().BeGreaterThan(scrollerBounds.Right);
            rightNavBounds.Left.Should().BeGreaterThan(addBounds.Right);
            rightNavBounds.Left.Should().BeGreaterThan(addBounds.Right + 20);
            addSheet.ActualWidth.Should().BeGreaterThan(34);
            addSheet.ActualHeight.Should().BeGreaterThan(20);
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _tryFocusCurrentSheetTab;
        private readonly MethodInfo _tryOpenFocusedSheetTabContextMenu;
        private readonly MethodInfo _sheetTabContextMenuOpened;
        private FrameworkElement? _routedSheetTabTarget;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _tryFocusCurrentSheetTab = typeof(MainWindow)
                .GetMethod("TryFocusCurrentSheetTab", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryFocusCurrentSheetTab");
            _tryOpenFocusedSheetTabContextMenu = typeof(MainWindow)
                .GetMethod("TryOpenFocusedSheetTabContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryOpenFocusedSheetTabContextMenu");
            _sheetTabContextMenuOpened = typeof(MainWindow)
                .GetMethod("SheetTabContextMenu_Opened", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SheetTabContextMenu_Opened");
        }

        public string? FocusedSheetTabName =>
            FocusedSheetTabTarget?.DataContext?.GetType().GetProperty("Name")?.GetValue(FocusedSheetTabTarget.DataContext)?.ToString();

        public MainWindow Window => _window;

        public bool SheetTabContextMenuIsOpen => RoutedOrActiveSheetTabTarget?.ContextMenu?.IsOpen == true;

        public bool SheetTabContextMenuPlacementTargetIsFocusedTab =>
            RoutedOrActiveSheetTabTarget is { ContextMenu.PlacementTarget: { } target } &&
            ReferenceEquals(target, RoutedOrActiveSheetTabTarget);

        public string? SheetTabMenuItemGestureText(string header) =>
            RoutedOrActiveSheetTabTarget?.ContextMenu?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                ?.InputGestureText;

        public string DebugSheetTabs =>
            string.Join("; ", SheetTabTargets.Select(element =>
            {
                var dataContext = element.DataContext;
                var name = dataContext?.GetType().GetProperty("Name")?.GetValue(dataContext)?.ToString();
                var active = dataContext?.GetType().GetProperty("IsActive")?.GetValue(dataContext);
                return $"{name}:active={active}:focus={element.IsKeyboardFocusWithin}:menu={element.ContextMenu?.IsOpen}:placement={ReferenceEquals(element.ContextMenu?.PlacementTarget, element)}";
            })) + $" routed={_routedSheetTabTarget?.ContextMenu?.IsOpen}:{ReferenceEquals(_routedSheetTabTarget?.ContextMenu?.PlacementTarget, _routedSheetTabTarget)} focused={Keyboard.FocusedElement?.GetType().Name}";

        public bool FocusCurrentSheetTab()
        {
            var focused = (bool)_tryFocusCurrentSheetTab.Invoke(_window, [])!;
            PumpDispatcher();
            return focused;
        }

        public void OpenFocusedSheetTabContextMenu()
        {
            _routedSheetTabTarget = FocusedSheetTabTarget;
            _routedSheetTabTarget.Should().NotBeNull("the active sheet tab should have keyboard focus before the Menu key is routed");
            _routedSheetTabTarget!.Focus();
            Keyboard.Focus(_routedSheetTabTarget);

            var opened = (bool)_tryOpenFocusedSheetTabContextMenu.Invoke(_window, [])!;
            opened.Should().BeTrue("the focused sheet tab route should open the sheet-tab context menu before worksheet fallback");
            if (_routedSheetTabTarget.ContextMenu is { } menu)
                _sheetTabContextMenuOpened.Invoke(null, [menu, new RoutedEventArgs(ContextMenu.OpenedEvent, menu)]);
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            workbook.AddSheet("Sheet2");
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
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }

        private FrameworkElement? FocusedSheetTabTarget
        {
            get
            {
                return SheetTabTargets.FirstOrDefault(element => element.IsKeyboardFocusWithin);
            }
        }

        private FrameworkElement? ActiveSheetTabTarget =>
            SheetTabTargets.FirstOrDefault(element =>
                element.DataContext?.GetType().GetProperty("IsActive")?.GetValue(element.DataContext) is true);

        private FrameworkElement? RoutedOrActiveSheetTabTarget => _routedSheetTabTarget ?? ActiveSheetTabTarget;

        private IReadOnlyList<FrameworkElement> SheetTabTargets
        {
            get
            {
                if (_window.FindName("SheetTabsControl") is not ItemsControl tabs)
                    return [];

                return EnumerateVisualDescendants(tabs)
                    .Concat(EnumerateLogicalDescendants(tabs))
                    .OfType<FrameworkElement>()
                    .Distinct()
                    .Where(element =>
                        element.ContextMenu is not null &&
                        element.DataContext?.GetType().Name == "SheetTabViewModel")
                    .ToList();
            }
        }

        private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;

                foreach (var descendant in EnumerateVisualDescendants(child))
                    yield return descendant;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject dependencyObject)
                    continue;

                yield return dependencyObject;

                foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                    yield return descendant;
            }
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static Rect BoundsRelativeToWindow(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
