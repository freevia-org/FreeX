using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

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
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_Rename")).Should().Be("R");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_InsertSheet")).Should().Be("I");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_Duplicate")).Should().Be("D");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_TabColor_EDBDA613")).Should().Be("T");
        });
    }

    [Fact]
    public void AddSheetGhostTab_LivesInScrollableTabStripAndExposesKeyboardAutomation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var window = harness.Window;

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var scrollableContent = (FrameworkElement)window.FindName("SheetTabsScrollableContent");
            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");

            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var addBounds = BoundsRelativeToWindow(addSheet, window);

            addSheet.Parent.Should().BeSameAs(scrollableContent, "the ghost tab should scroll and clip with the sheet tabs instead of reserving fixed space");
            addSheet.Focusable.Should().BeTrue();
            AutomationProperties.GetName(addSheet).Should().Be(UiText.Get("MainWindow_AutomationName_InsertSheet"));
            AutomationProperties.GetHelpText(addSheet).Should().Be(UiText.Get("MainWindow_AutomationHelpText_AddANewSheetToTheWorkbook"));
            addBounds.Left.Should().BeGreaterThanOrEqualTo(scrollerBounds.Left);
            addBounds.Left.Should().BeLessThan(scrollerBounds.Right, "the ghost tab should be inside the clipped tab viewport so it can slide under the right arrow");
            addSheet.ActualWidth.Should().BeGreaterThan(34);
            addSheet.ActualHeight.Should().BeGreaterThan(20);
        });
    }

    [Fact]
    public void RightClickSheetTab_ClearsPreviousGroupedHighlight()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet2");
            harness.GroupedSheetTabNames.Should().Equal("Sheet2");

            harness.RightClickSheetTab("Sheet1");

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.GroupedSheetTabNames.Should().Equal("Sheet1");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _sheetTabMouseRightButtonDown;
        private readonly MethodInfo _tryFocusCurrentSheetTab;
        private readonly MethodInfo _tryOpenFocusedSheetTabContextMenu;
        private readonly MethodInfo _sheetTabContextMenuOpened;
        private FrameworkElement? _routedSheetTabTarget;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _insertNewSheet = typeof(MainWindow)
                .GetMethod("InsertNewSheet", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertNewSheet");
            _sheetTabMouseRightButtonDown = typeof(MainWindow)
                .GetMethod("SheetTab_MouseRightButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SheetTab_MouseRightButtonDown");
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

        public string? ActiveSheetTabName =>
            SheetTabViewModels
                .FirstOrDefault(viewModel => GetBoolProperty(viewModel, "IsActive"))
                is { } active
                    ? GetStringProperty(active, "Name")
                    : null;

        public IReadOnlyList<string> GroupedSheetTabNames =>
            SheetTabViewModels
                .Where(viewModel => GetBoolProperty(viewModel, "IsGrouped"))
                .Select(viewModel => GetStringProperty(viewModel, "Name"))
                .Where(name => name is not null)
                .Cast<string>()
                .ToList();

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

        public void InsertNewSheet()
        {
            _insertNewSheet.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void RightClickSheetTab(string name)
        {
            var target = SheetTabTargets.Single(element =>
                element.DataContext is { } viewModel &&
                string.Equals(GetStringProperty(viewModel, "Name"), name, StringComparison.Ordinal));
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            {
                RoutedEvent = UIElement.MouseRightButtonDownEvent,
                Source = target
            };

            _sheetTabMouseRightButtonDown.Invoke(_window, [target, args]);
            _window.UpdateLayout();
            PumpDispatcher();
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
                workbook,
                NullUserMessageService.Instance)
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

        private IReadOnlyList<object> SheetTabViewModels =>
            SheetTabTargets
                .Select(element => element.DataContext)
                .Where(dataContext => dataContext is not null)
                .Cast<object>()
                .Distinct()
                .ToList();

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

        private static bool GetBoolProperty(object source, string propertyName) =>
            source.GetType().GetProperty(propertyName)?.GetValue(source) is true;

        private static string? GetStringProperty(object source, string propertyName) =>
            source.GetType().GetProperty(propertyName)?.GetValue(source)?.ToString();

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
