using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowAdaptiveRibbonTests
{
    [Fact]
    public void HomeRibbon_CollapsesGroupsIntoGroupButtonsAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);

            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.CollapsedRibbonGroupMenus.Should().Contain(menu => menu.Items.Count > 0);
            harness.CollapsedMenuHeaders("Editing").Should().Contain(["AutoSum", "Fill", "Clear", "Sort & Filter", "Find & Select"]);
        });
    }

    [Fact]
    public void HomeRibbon_CollapsesEditingBeforeLabelsClipAtWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(1465);

            harness.CollapsedRibbonGroupNames.Should().Contain("Editing", harness.DebugRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().NotContain("Find & Select", harness.DebugRibbonChildren);
        });
    }

    [Fact]
    public void FormulasRibbon_CollapsesFunctionLibraryAtShortWideWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Formulas", 1465);

            harness.CollapsedActiveRibbonGroupNames.Should().Contain("Function Library", harness.DebugActiveRibbonChildren);
        });
    }

    [Fact]
    public void IconOnlyRibbonCommandsRemainCenterAligned()
    {
        StaTestRunner.Run(() =>
        {
            var label = new TextBlock { Text = "Paste", Tag = "RibbonLabel" };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Children = { new TextBlock { Text = "\uE16D", Tag = "RibbonIcon" }, label }
            };
            var button = new Button
            {
                Tag = "RibbonCompact:72:32",
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
                Content = content
            };

            var compactLevel = typeof(MainWindow).GetNestedType("RibbonCompactLevel", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonCompactLevel");
            var iconOnly = Enum.Parse(compactLevel, "IconOnly");
            var setCompact = typeof(MainWindow)
                .GetMethod("SetRibbonButtonCompact", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetRibbonButtonCompact");

            setCompact.Invoke(null, [button, iconOnly]);

            button.HorizontalContentAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
            content.HorizontalAlignment.Should().Be(System.Windows.HorizontalAlignment.Center);
        });
    }

    [Fact]
    public void InsertRibbon_HidesChartFormattingCommands()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("Insert", 800);

            harness.VisibleRibbonCommandLabels.Should().NotContain("Label Border", harness.DebugActiveRibbonChildren);
            harness.VisibleRibbonCommandLabels.Should().NotContain("Y Bounds", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveRibbonGroupNames.Should().Contain("Charts", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().Contain("Column Chart", harness.DebugActiveRibbonChildren);
            harness.CollapsedActiveMenuHeaders("Charts").Should().NotContain("Data Label Border", harness.DebugActiveRibbonChildren);
        });
    }

    [Fact]
    public void RibbonTabs_RemainSingleRowAtNarrowWidths()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(640);

            harness.VisibleRibbonTabHeaderRows.Should().HaveCount(1, "Excel keeps the main ribbon tabs on one row while the command groups collapse");
        });
    }

    [Fact]
    public void CollapsedRibbonMenuItems_MirrorSourceMenuStateAndOpenedUpdates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRibbonTab("View", 640);
            var arrangeAll = harness.CollapsedActiveMenuItem("Window", "Arrange All");

            arrangeAll.Should().NotBeNull(harness.DebugActiveRibbonChildren);
            var tiled = arrangeAll!.Items.OfType<MenuItem>()
                .First(item => string.Equals(item.Header?.ToString(), "Tiled", StringComparison.Ordinal));

            tiled.IsCheckable.Should().BeTrue();
            tiled.InputGestureText.Should().Be("T");

            arrangeAll.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, arrangeAll));

            tiled.IsChecked.Should().BeTrue("the clone should run the source menu's Opened state refresh before display");
            arrangeAll.Items.OfType<MenuItem>()
                .Where(item => !ReferenceEquals(item, tiled))
                .Should().OnlyContain(item => item.IsChecked == false);
        });
    }

    [Fact]
    public void CollapsedRibbonMenuItems_RefreshSourceButtonEnabledStateWhenOpened()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetRibbonWidth(220);
            var sourceButton = harness.VisibleOrCollapsedRibbonButton("Find & Select");
            var menu = harness.CollapsedMenu("Editing");
            var item = harness.CollapsedMenuItem("Editing", "Find & Select");

            sourceButton.Should().NotBeNull(harness.DebugRibbonChildren);
            item.Should().NotBeNull(harness.DebugRibbonChildren);

            sourceButton!.IsEnabled = false;
            menu!.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));

            item!.IsEnabled.Should().BeFalse("collapsed overflow commands should use the current enabled state of their source ribbon controls");
        });
    }

    [Fact]
    public void DenseRibbonCommandColumns_UseShortRowButtons()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.DenseColumnButtonHeights.Should().OnlyContain(
                    height => height <= 24,
                    $"{tab} dense ribbon columns should use Excel-like short row commands instead of tall large-button footprints");
            }
        });
    }

    [Fact]
    public void VerticallyStackedRibbonCommands_AlignIconSlots()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 1465);

                harness.VerticallyStackedRibbonIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Select(offset => Math.Round(offset, 1)).Distinct().Count() == 1,
                    $"{tab} vertical command stacks should put small command icons directly above one another");

                harness.DirectVerticalButtonStackIconOffsets.Should().OnlyContain(
                    stack => stack.Offsets.Select(offset => Math.Round(offset, 1)).Distinct().Count() == 1,
                    $"{tab} direct XAML vertical button stacks should align small command icons in a fixed column");
            }
        });
    }

    [Fact]
    public void RibbonScrollViewers_HideHorizontalScrollBarsWithoutDisablingFallbackScroll()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            foreach (var tab in new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View", "Help" })
            {
                harness.SelectRibbonTab(tab, 640);

                harness.RibbonHorizontalScrollBarModes.Should().OnlyContain(
                    mode => mode == ScrollBarVisibility.Hidden,
                    $"{tab} should keep the ribbon face clean while preserving hidden horizontal fallback scrolling");
            }
        });
    }

    [Fact]
    public void RibbonScrollViewers_DefaultToHiddenHorizontalScrollBarsInXaml()
    {
        var xaml = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));

        xaml.Should().NotContain(
            "HorizontalScrollBarVisibility=\"Auto\"",
            "ribbon tabs should not briefly show a horizontal scrollbar before runtime normalization collapses groups");
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _updateRibbonCompactMode;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _updateRibbonCompactMode = typeof(MainWindow)
                .GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
        }

        public IReadOnlyList<string> CollapsedRibbonGroupNames =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupNames =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<ContextMenu> CollapsedRibbonGroupMenus =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Select(button => button.ContextMenu)
                .Where(menu => menu is not null)
                .Cast<ContextMenu>()
                .ToList();

        public IReadOnlyList<string> CollapsedMenuHeaders(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveMenuHeaders(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public MenuItem? CollapsedActiveMenuItem(string groupName, string header) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => button.ContextMenu?.Items.OfType<MenuItem>() ?? [])
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public ContextMenu? CollapsedMenu(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(button => button.Tag is string tag && tag == "RibbonCollapsedGroupButton" && button.Visibility == Visibility.Visible)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .Select(button => button.ContextMenu)
                .FirstOrDefault(menu => menu is not null);

        public MenuItem? CollapsedMenuItem(string groupName, string header) =>
            CollapsedMenu(groupName)?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public Button? VisibleOrCollapsedRibbonButton(string title) =>
            HomeRibbonChildren
                .OfType<DependencyObject>()
                .SelectMany(EnumerateSelfAndVisualDescendants)
                .Concat(HomeRibbonChildren.OfType<DependencyObject>().SelectMany(EnumerateLogicalDescendants))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

        private IEnumerable<UIElement> HomeRibbonChildren =>
            (_window.FindName("HomeRibbonPanel") as StackPanel)?.Children.Cast<UIElement>() ?? [];

        public string DebugRibbonChildren =>
            string.Join(", ", HomeRibbonChildren.Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}"
                    : child.GetType().Name));

        public string DebugActiveRibbonChildren =>
            string.Join(", ", ActiveRibbonPanel?.Children.Cast<UIElement>().Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}"
                    : child.GetType().Name) ?? []);

        public IReadOnlyList<string> VisibleRibbonCommandLabels =>
            (SelectedRibbonTab is null
                ? []
                : EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                    .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                    .OfType<Button>()
                    .Distinct()
                    .Where(IsEffectivelyVisible)
                    .Select(GetButtonLabel)
                    .Where(label => !string.IsNullOrWhiteSpace(label)))
            .ToList();

        public IReadOnlyList<int> VisibleRibbonTabHeaderRows =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? EnumerateSelfAndVisualDescendants(tabs)
                    .OfType<TabItem>()
                    .Where(item => item.Visibility == Visibility.Visible && item.ActualHeight > 0)
                    .Select(item => (int)Math.Round(item.TransformToAncestor(tabs).Transform(new Point(0, 0)).Y))
                    .Distinct()
                    .OrderBy(row => row)
                    .ToList()
                : [];

        public IReadOnlyList<double> DenseColumnButtonHeights =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<UniformGrid>()
                .Where(grid => grid.Rows == 3 && grid.Children.OfType<Button>().Count() > 3)
                .SelectMany(grid => grid.Children.OfType<Button>())
                .Where(IsEffectivelyVisible)
                .Select(button => button.Height)
                .ToList();

        public IReadOnlyList<RibbonIconStackOffsets> VerticallyStackedRibbonIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<Panel>()
                .SelectMany(GetVerticalIconStacks)
                .ToList();

        public IReadOnlyList<RibbonIconStackOffsets> DirectVerticalButtonStackIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<StackPanel>()
                .Where(panel => panel.Orientation == Orientation.Vertical)
                .SelectMany(GetDirectVerticalButtonStacks)
                .ToList();

        public IReadOnlyList<ScrollBarVisibility> RibbonHorizontalScrollBarModes =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? EnumerateSelfAndVisualDescendants(tabs)
                    .OfType<ScrollViewer>()
                    .Where(IsEffectivelyVisible)
                    .Select(scrollViewer => scrollViewer.HorizontalScrollBarVisibility)
                    .ToList()
                : [];

        private TabItem? SelectedRibbonTab =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem as TabItem;

        private DependencyObject SelectedRibbonContentRoot =>
            SelectedRibbonTab?.Content as DependencyObject ??
            (DependencyObject?)SelectedRibbonTab ??
            _window;

        private StackPanel? ActiveRibbonPanel =>
            SelectedRibbonTab is { } tabItem
                ? EnumerateSelfAndVisualDescendants(tabItem.Content as DependencyObject ?? tabItem)
                    .Concat(EnumerateLogicalDescendants(tabItem.Content as DependencyObject ?? tabItem))
                    .OfType<StackPanel>()
                    .Distinct()
                    .OrderByDescending(panel => panel.Children.OfType<Grid>().Count())
                    .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                             panel.Children.OfType<Grid>().Any())
                : null;

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl tabs)
            {
                tabs.SelectedItem = tabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
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
                workbook);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            _window.Close();
            PumpDispatcher();
        }

        private static IEnumerable<DependencyObject> EnumerateSelfAndVisualDescendants(DependencyObject root)
        {
            yield return root;

            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                foreach (var descendant in EnumerateSelfAndVisualDescendants(child))
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

        private static string GetButtonLabel(Button button)
        {
            if (button.Content is string text)
                return text;

            return EnumerateSelfAndVisualDescendants(button)
                .Concat(EnumerateLogicalDescendants(button))
                .OfType<TextBlock>()
                .FirstOrDefault(textBlock => string.Equals(textBlock.Tag?.ToString(), "RibbonLabel", StringComparison.Ordinal))
                ?.Text ?? "";
        }

        private static double GetIconSlotOffset(Visual ancestor, Button button)
        {
            if (!TryGetCommandIconSlot(button, out var iconSlot))
            {
                return double.NaN;
            }

            return iconSlot.TransformToAncestor(ancestor).Transform(new Point(0, 0)).X;
        }

        private static IEnumerable<RibbonIconStackOffsets> GetVerticalIconStacks(Panel panel)
        {
            if (panel is StackPanel { Orientation: Orientation.Vertical })
            {
                var buttons = GetSmallCommandButtons(panel).ToArray();
                if (buttons.Length >= 2)
                    yield return CreateIconStackOffsets(panel, buttons);

                yield break;
            }

            if (panel is UniformGrid { Rows: > 0 } grid)
            {
                var buttons = GetSmallCommandButtons(grid).ToArray();
                if (buttons.Length < 2)
                    yield break;

                var columns = (int)Math.Ceiling(buttons.Length / (double)grid.Rows);
                for (var column = 0; column < columns; column++)
                {
                    var columnButtons = buttons
                        .Skip(column)
                        .Where((_, index) => index % columns == 0)
                        .ToArray();
                    if (columnButtons.Length >= 2)
                        yield return CreateIconStackOffsets(grid, columnButtons);
                }
            }
        }

        private static IEnumerable<RibbonIconStackOffsets> GetDirectVerticalButtonStacks(StackPanel panel)
        {
            var buttons = panel.Children
                .OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => TryGetCommandIconSlot(button, out _))
                .ToArray();

            if (buttons.Length < 2)
                yield break;

            yield return new RibbonIconStackOffsets(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetDirectIconSlotCenterOffset(panel, button)).ToArray());
        }

        private static IEnumerable<Button> GetSmallCommandButtons(Panel panel) =>
            panel.Children.OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => button.Content is FrameworkElement content &&
                                 string.Equals(content.Tag?.ToString(), "RibbonCommandContent:S", StringComparison.Ordinal) &&
                                 TryGetCommandIconSlot(button, out _));

        private static RibbonIconStackOffsets CreateIconStackOffsets(Visual ancestor, IReadOnlyList<Button> buttons) =>
            new(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetIconSlotOffset(ancestor, button)).ToArray());

        private static double GetDirectIconSlotCenterOffset(Visual ancestor, Button button)
        {
            if (!TryGetCommandIconSlot(button, out var iconSlot))
            {
                return double.NaN;
            }

            var point = iconSlot.TransformToAncestor(ancestor).Transform(new Point(0, 0));
            return point.X + iconSlot.ActualWidth / 2;
        }

        private static bool TryGetCommandIconSlot(Button button, out FrameworkElement iconSlot)
        {
            iconSlot = null!;
            if (button.Content is not Panel { Children.Count: > 0 } content ||
                content.Children[0] is not FrameworkElement firstChild)
            {
                return false;
            }

            iconSlot = firstChild;
            return true;
        }

        private static bool IsEffectivelyVisible(DependencyObject element)
        {
            var current = element;
            while (current is not null)
            {
                if (current is UIElement { Visibility: not Visibility.Visible })
                    return false;

                current = System.Windows.Media.VisualTreeHelper.GetParent(current) ??
                          LogicalTreeHelper.GetParent(current);
            }

            return true;
        }
    }

    public sealed record RibbonIconStackOffsets(IReadOnlyList<string> Labels, IReadOnlyList<double> Offsets);

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
