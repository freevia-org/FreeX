using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void MenuKeyOnInactiveFocusedSheetTab_SelectsTabBeforeWorksheetFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet2");

            harness.FocusSheetTab("Sheet1").Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.RouteFocusedSheetTabContextMenu().Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.GroupedSheetTabNames.Should().Equal("Sheet1");
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
    public void SheetTabViewport_LeavesTopInsetForChromeStroke()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 6);
            var window = harness.Window;

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var row = (FrameworkElement)window.FindName("SheetTabsRowGrid");
            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var chromeLayer = (FrameworkElement)window.FindName("SheetTabsChromeLayer");
            var overlayLayer = (FrameworkElement)window.FindName("SheetTabsOverlayLayer");

            harness.FocusCurrentSheetTab().Should().BeTrue();
            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var focusedTab = Keyboard.FocusedElement.Should().BeAssignableTo<FrameworkElement>().Subject;
            var rowBounds = BoundsRelativeToWindow(row, window);
            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var chromeBounds = BoundsRelativeToWindow(chromeLayer, window);
            var overlayBounds = BoundsRelativeToWindow(overlayLayer, window);
            var focusedTabBounds = BoundsRelativeToWindow(focusedTab, window);

            scrollerBounds.Top.Should().BeGreaterThan(rowBounds.Top + 3.5, "the clipped viewport needs enough breathing room above tab text and focus visuals for the rounded stroke to render");
            chromeBounds.Top.Should().BeGreaterThan(rowBounds.Top + 3.5, "the drawn tab chrome stroke should not start on the row's clipping edge");
            overlayBounds.Top.Should().BeApproximately(chromeBounds.Top, 0.25);
            scroller.ActualHeight.Should().BeGreaterThanOrEqualTo(32);
            rowBounds.Height.Should().BeGreaterThan(scroller.ActualHeight);
            focusedTabBounds.Top.Should().BeGreaterThanOrEqualTo(scrollerBounds.Top - 0.5);
            focusedTabBounds.Bottom.Should().BeLessThanOrEqualTo(scrollerBounds.Bottom + 0.5, "the focused sheet tab chrome must fit inside the clipped viewport");
            chromeBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5);
            overlayBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5);
        });
    }

    [Fact]
    public void SheetTabNavigationButtons_StayBelowGridRule()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 140);
            var window = harness.Window;

            harness.UpdateSheetTabNavigation();
            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var row = (FrameworkElement)window.FindName("SheetTabsRowGrid");
            var overlayLayer = (FrameworkElement)window.FindName("SheetTabsOverlayLayer");
            var visibleNavButtons = new[]
                {
                    (FrameworkElement)window.FindName("SheetNavLeftBtn"),
                    (FrameworkElement)window.FindName("SheetNavRightBtn")
                }
                .Where(button => button.Visibility == Visibility.Visible)
                .ToList();

            visibleNavButtons.Should().NotBeEmpty("the many-sheet viewport should expose at least one sheet-tab navigation button");

            var rowBounds = BoundsRelativeToWindow(row, window);
            var overlayBounds = BoundsRelativeToWindow(overlayLayer, window);
            overlayBounds.Top.Should().BeGreaterThan(rowBounds.Top + 3.5, "the blue sheet-tab rule should not sit on the row's clipping edge");

            foreach (var button in visibleNavButtons)
            {
                var buttonBounds = BoundsRelativeToWindow(button, window);
                buttonBounds.Top.Should().BeGreaterThan(overlayBounds.Top + 1.0, "sheet-tab nav buttons should not cover or overlap the blue rule");
                buttonBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5, "sheet-tab nav buttons should stay inside the sheet-tab row");
            }

            CaptureSheetTabLowerBandIfRequested(window, row);
        });
    }

    [Fact]
    public void ArrowKeyOnAddSheetButton_DoesNotRouteAsFocusedSheetTabNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusAddSheetButton().Should().BeTrue();
            harness.ActiveSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Right).Should().BeFalse();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.AddSheetButtonHasKeyboardFocus.Should().BeTrue();
        });
    }

    [Fact]
    public void ArrowKeyOnFocusedSheetTab_RoutesAsSheetTabNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Left).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");
        });
    }

    [Fact]
    public void HomeEndKeysOnFocusedSheetTab_RouteToEdgeSheetTabs()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet3");

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet3");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Home).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.End).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet3");
            harness.FocusedSheetTabName.Should().Be("Sheet3");
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    [InlineData(Key.Tab)]
    public void NonNavigationKeyOnFocusedSheetTab_DoesNotRouteAsSheetTabNavigation(Key key)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(key).Should().BeFalse();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");
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

    [Fact]
    public void SheetTabMouseMove_CancelsStaleDragWhenLeftButtonIsReleased()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.SheetTabs.cs"));
        var mouseMove = source[
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (_dragSheetTabId is not { } draggedId)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("_dragSheetTabId = null;");
        mouseMove.IndexOf("_dragSheetTabId = null;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var current = e.GetPosition(SheetTabsControl);", StringComparison.Ordinal));
    }

    [Fact]
    public void SheetTabDrag_CapturesMouseAndClearsStateOnReleaseOrLostCapture()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.SheetTabs.cs"));
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));

        var mouseDown = source[
            source.IndexOf("private void SheetTab_MouseLeftButtonDown", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)];
        var mouseMove = source[
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)];
        var mouseUpAndLostCapture = source[
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseRightButtonDown", StringComparison.Ordinal)];

        xaml.Should().Contain("LostMouseCapture=\"SheetTab_LostMouseCapture\"");
        mouseDown.Should().Contain("element.CaptureMouse();");
        mouseDown.IndexOf("element.CaptureMouse();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(mouseDown.IndexOf("_dragSheetTabStart = e.GetPosition(SheetTabsControl);", StringComparison.Ordinal));
        mouseMove.Should().Contain("element.ReleaseMouseCapture();");
        mouseMove.IndexOf("element.ReleaseMouseCapture();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var current = e.GetPosition(SheetTabsControl);", StringComparison.Ordinal));
        mouseUpAndLostCapture.Should().Contain("element.ReleaseMouseCapture();");
        mouseUpAndLostCapture.Should().Contain("private void SheetTab_LostMouseCapture");
        mouseUpAndLostCapture.Should().Contain("_dragSheetTabId = null;");
    }

    [Fact]
    public void SheetTabLabelDoubleClick_RenamesOnlyForLeftButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.SheetTabs.cs"));
        var labelMouseDown = source[
            source.IndexOf("private void SheetTab_LabelMouseDown", StringComparison.Ordinal)..
            source.IndexOf("private void RenameSheetFromTab", StringComparison.Ordinal)];

        labelMouseDown.Should().Contain("e.ChangedButton != MouseButton.Left");
        labelMouseDown.Should().Contain("e.ClickCount != 2");
        labelMouseDown.Should().Contain("RenameSheetFromTab(tab);");
        labelMouseDown.Should().Contain("e.Handled = true;");
        labelMouseDown.IndexOf("e.ChangedButton != MouseButton.Left", StringComparison.Ordinal)
            .Should()
            .BeLessThan(labelMouseDown.IndexOf("RenameSheetFromTab(tab);", StringComparison.Ordinal));
        labelMouseDown.IndexOf("RenameSheetFromTab(tab);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(labelMouseDown.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void SheetTabChrome_ReusesRenderedPathsAcrossRepeatedManyTabNavigationUpdates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 140);

            harness.UpdateSheetTabNavigation();
            var chromeChildren = harness.SheetTabChromeChildren;
            var overlayChildren = harness.SheetTabOverlayChildren;

            chromeChildren.Should().NotBeEmpty("many visible tabs should render chrome paths before repeated scroll or resize callbacks");
            overlayChildren.Should().NotBeEmpty("the sheet-tab grid rule should be rendered before repeated callbacks");

            harness.UpdateSheetTabNavigation(iterations: 60);

            harness.SheetTabChromeChildren.Should().Equal(chromeChildren, "unchanged many-tab navigation callbacks should skip chrome clear/rebuild churn");
            harness.SheetTabOverlayChildren.Should().Equal(overlayChildren, "unchanged many-tab navigation callbacks should keep the overlay path intact");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _sheetTabMouseRightButtonDown;
        private readonly MethodInfo _updateSheetTabNavigation;
        private readonly MethodInfo _tryFocusCurrentSheetTab;
        private readonly MethodInfo _tryOpenFocusedSheetTabContextMenu;
        private readonly MethodInfo _tryHandleFocusedSheetTabKeyboardNavigation;
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
            _updateSheetTabNavigation = typeof(MainWindow)
                .GetMethod("UpdateSheetTabNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateSheetTabNavigation");
            _tryFocusCurrentSheetTab = typeof(MainWindow)
                .GetMethod("TryFocusCurrentSheetTab", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryFocusCurrentSheetTab");
            _tryOpenFocusedSheetTabContextMenu = typeof(MainWindow)
                .GetMethod("TryOpenFocusedSheetTabContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryOpenFocusedSheetTabContextMenu");
            _tryHandleFocusedSheetTabKeyboardNavigation = typeof(MainWindow)
                .GetMethod("TryHandleFocusedSheetTabKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedSheetTabKeyboardNavigation");
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

        public bool AddSheetButtonHasKeyboardFocus =>
            _window.FindName("AddSheetButton") is FrameworkElement addSheet &&
            addSheet.IsKeyboardFocusWithin;

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

        public IReadOnlyList<UIElement> SheetTabChromeChildren =>
            ((Panel)_window.FindName("SheetTabsChromeLayer")).Children.Cast<UIElement>().ToList();

        public IReadOnlyList<UIElement> SheetTabOverlayChildren =>
            ((Panel)_window.FindName("SheetTabsOverlayLayer")).Children.Cast<UIElement>().ToList();

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

        public bool FocusSheetTab(string name)
        {
            var target = SheetTabTarget(name);
            var focused = target.Focus();
            Keyboard.Focus(target);
            PumpDispatcher();
            return focused;
        }

        public bool FocusAddSheetButton()
        {
            if (_window.FindName("AddSheetButton") is not FrameworkElement addSheet)
                return false;

            var focused = addSheet.Focus();
            Keyboard.Focus(addSheet);
            PumpDispatcher();
            return focused;
        }

        public bool HandleFocusedSheetTabKeyboardNavigation(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };

            var handled = (bool)_tryHandleFocusedSheetTabKeyboardNavigation.Invoke(_window, [args])!;
            _window.UpdateLayout();
            PumpDispatcher();
            _window.UpdateLayout();
            return handled;
        }

        public void InsertNewSheet()
        {
            _insertNewSheet.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void UpdateSheetTabNavigation(int iterations = 1)
        {
            for (var i = 0; i < iterations; i++)
                _updateSheetTabNavigation.Invoke(_window, null);

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void RightClickSheetTab(string name)
        {
            var target = SheetTabTarget(name);
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

        public bool RouteFocusedSheetTabContextMenu()
        {
            var routed = (bool)_tryOpenFocusedSheetTabContextMenu.Invoke(_window, [])!;
            _window.UpdateLayout();
            PumpDispatcher();
            _window.UpdateLayout();
            return routed;
        }

        public static MainWindowHarness Create(int sheetCount = 2)
        {
            var workbook = new Workbook("Book1");
            for (var i = 1; i <= sheetCount; i++)
                workbook.AddSheet($"Sheet{i}");
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

        private FrameworkElement SheetTabTarget(string name) =>
            SheetTabTargets.Single(element =>
                element.DataContext is { } viewModel &&
                string.Equals(GetStringProperty(viewModel, "Name"), name, StringComparison.Ordinal));

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

    private static void CaptureSheetTabLowerBandIfRequested(Window window, FrameworkElement row)
    {
        var outputPath = Environment.GetEnvironmentVariable("FREEX_SHEET_TAB_LOWER_BAND_CAPTURE");
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        if (window.FindName("RootGrid") is not FrameworkElement root)
            return;

        root.UpdateLayout();
        var source = PresentationSource.FromVisual(root);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpiX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpiY));

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var rowTop = row.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
        var cropTop = Math.Max(0, (int)Math.Floor((rowTop - 28) * dpiY));
        var cropHeight = Math.Min(pixelHeight - cropTop, Math.Max(1, (int)Math.Ceiling(120 * dpiY)));
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(0, cropTop, pixelWidth, cropHeight));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
