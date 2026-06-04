using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowAdaptiveRibbonTests
{
    private static Window ShowStandaloneRibbonButton(Button button, double width, double height)
    {
        button.Width = width;
        button.Height = height;
        var host = new Grid
        {
            Width = width,
            Height = height,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Children = { button }
        };
        var window = new Window
        {
            Width = width + 64,
            Height = height + 64,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Content = host
        };
        window.Show();
        window.UpdateLayout();
        PumpDispatcher();
        return window;
    }

    private static Rect GetRibbonDropdownZoneBounds(ButtonBase button)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryGetRibbonDropdownZoneBounds",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var args = new object[] { button, Rect.Empty };
        ((bool)method!.Invoke(null, args)!).Should().BeTrue();
        return (Rect)args[1];
    }

    private static IEnumerable<DependencyObject> EnumerateSelfAndVisualDescendants(DependencyObject root)
    {
        yield return root;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in EnumerateSelfAndVisualDescendants(child))
                yield return descendant;
        }
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private static MainWindow? SharedWindow;

        private readonly MainWindow _window;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _normalizeRibbonSurface;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _updateRibbonCompactMode = typeof(MainWindow)
                .GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _normalizeRibbonSurface = typeof(MainWindow)
                .GetMethod("NormalizeRibbonSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "NormalizeRibbonSurface");
        }

        public IReadOnlyList<string> CollapsedRibbonGroupNames =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupNames =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> ActiveRibbonGroupNames =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<DependencyObject>()
                .Where(RibbonMetadata.IsRibbonGroup)
                .Select(group => RibbonMetadata.TryGetGroupName(group, out var name) ? name : "")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

        public IReadOnlyList<string> ActiveRibbonPresentationGroupNames =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<FrameworkElement>()
                .Where(IsEffectivelyVisible)
                .Select(GetRibbonPresentationGroupName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupVisibleLabels =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => EnumerateSelfAndVisualDescendants(button)
                    .Concat(EnumerateLogicalDescendants(button))
                    .OfType<TextBlock>()
                    .Any(textBlock =>
                        RibbonMetadata.IsCommandLabel(textBlock) &&
                        IsEffectivelyVisible(textBlock) &&
                        string.Equals(textBlock.Text, RibbonTooltip.GetTitle(button), StringComparison.Ordinal)))
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupWrappedVisibleLabels =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .SelectMany(button => EnumerateSelfAndVisualDescendants(button)
                    .Concat(EnumerateLogicalDescendants(button))
                    .OfType<TextBlock>()
                    .Where(RibbonMetadata.IsCommandLabel)
                    .Where(IsEffectivelyVisible))
                .Where(textBlock => textBlock.TextWrapping != TextWrapping.NoWrap ||
                                    textBlock.TextTrimming != TextTrimming.CharacterEllipsis)
                .Select(textBlock => textBlock.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

        public IReadOnlyList<CollapsedGroupKeyTip> CollapsedActiveRibbonGroupKeyTips =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Select(button => new CollapsedGroupKeyTip(RibbonTooltip.GetTitle(button) ?? "", RibbonTooltip.GetKeyTip(button) ?? ""))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.GroupName) && !string.IsNullOrWhiteSpace(pair.KeyTip))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupsWithoutKeyTips =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(button)))
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveRibbonGroupsWithoutDropdownGlyph =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => System.Windows.Documents.AdornerLayer.GetAdornerLayer(button)
                    ?.GetAdorners(button)
                    ?.Any(adorner => adorner.GetType().Name == "RibbonCollapsedGroupChevronAdorner") != true)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<string> HiddenCollapsedRibbonGroupsWithVisibleDropdownGlyph =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(button => RibbonMetadata.IsCollapsedGroupButton(button) &&
                                 button.Visibility != Visibility.Visible)
                .Where(HasVisibleCollapsedGroupDropdownGlyph)
                .Select(button => RibbonTooltip.GetTitle(button) ?? "")
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToList();

        public IReadOnlyList<ContextMenu> CollapsedRibbonGroupMenus =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Select(button => button.ContextMenu)
                .Where(menu => menu is not null)
                .Cast<ContextMenu>()
                .ToList();

        public IReadOnlyList<string> CollapsedMenuHeaders(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => OpenCollapsedMenu(button.ContextMenu)?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public IReadOnlyList<string> CollapsedActiveMenuHeaders(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => OpenCollapsedMenu(button.ContextMenu)?.Items.OfType<MenuItem>() ?? [])
                .Select(item => item.Header?.ToString() ?? "")
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .ToList();

        public MenuItem? CollapsedActiveMenuItem(string groupName, string header) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .SelectMany(button => OpenCollapsedMenu(button.ContextMenu)?.Items.OfType<MenuItem>() ?? [])
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        public ContextMenu? CollapsedMenu(string groupName) =>
            HomeRibbonChildren
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .Select(button => button.ContextMenu)
                .FirstOrDefault(menu => menu is not null);

        public ContextMenu? CollapsedActiveMenu(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Button>()
                .Where(IsVisibleCollapsedGroupButton)
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), groupName, StringComparison.Ordinal))
                .Select(button => button.ContextMenu)
                .FirstOrDefault(menu => menu is not null);

        public MenuItem? CollapsedMenuItem(string groupName, string header) =>
            OpenCollapsedMenu(CollapsedMenu(groupName))?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));

        private static ContextMenu? OpenCollapsedMenu(ContextMenu? menu)
        {
            menu?.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            return menu;
        }

        public Button? VisibleOrCollapsedRibbonButton(string title) =>
            HomeRibbonChildren
                .OfType<DependencyObject>()
                .SelectMany(EnumerateSelfAndVisualDescendants)
                .Concat(HomeRibbonChildren.OfType<DependencyObject>().SelectMany(EnumerateLogicalDescendants))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

        public bool VisibleRibbonButtonHasDropdownChevron(string title) =>
            VisibleOrCollapsedRibbonButton(title) is { } button &&
            DropdownChevronCount(button) > 0;

        public int VisibleRibbonButtonDropdownChevronCount(string title) =>
            VisibleOrCollapsedRibbonButton(title) is { } button
                ? DropdownChevronCount(button)
                : 0;

        public bool VisibleRibbonButtonHasDropdownZoneHandler(string title) =>
            VisibleOrCollapsedRibbonButton(title) is { } button &&
            RibbonMetadata.GetDropdownZoneHandlerAttached(button);

        public bool VisibleRibbonButtonHasDropdownZoneHighlight(string title) =>
            VisibleOrCollapsedRibbonButton(title) is { } button &&
            HasDropdownZoneHighlight(button);

        public RibbonCommandContentLayout? NamedRibbonButtonContentLayout(string name)
        {
            if (_window.FindName(name) is not ButtonBase button ||
                button.Content is not FrameworkElement content ||
                !RibbonMetadata.TryGetCommandContentLayout(content, out var layout))
            {
                return null;
            }

            return layout;
        }

        public bool NamedRibbonButtonHasIconSlot(string name) =>
            _window.FindName(name) is ButtonBase button &&
            TryGetCommandIconSlot(button, out _);

        public IReadOnlyList<string> ActiveRibbonMenuButtonsWithoutSplitTreatment =>
            ActiveRibbonMenuButtons
                .Where(button => DropdownChevronCount(button) != 1 ||
                                 !RibbonMetadata.GetDropdownZoneHandlerAttached(button) ||
                                 !HasDropdownZoneHighlight(button))
                .Select(button =>
                    $"{GetButtonDebugName(button)}: chevrons={DropdownChevronCount(button)}, " +
                    $"handler={RibbonMetadata.GetDropdownZoneHandlerAttached(button)}, " +
                    $"highlight={HasDropdownZoneHighlight(button)}")
                .ToList();

        private IReadOnlyList<Button> ActiveRibbonMenuButtons =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                .OfType<Button>()
                .Distinct()
                .Where(IsEffectivelyVisible)
                .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                .Where(button => button.ContextMenu is not null || RibbonMetadata.IsDropdownMenuButton(button))
                .ToList();

        public bool HorizontalDropdownZoneClearsCommandLabel(string title)
        {
            var button = ActiveRibbonButton(title);
            button.Should().NotBeNull(DebugActiveRibbonChildren);

            var method = typeof(MainWindow).GetMethod(
                "TryGetRibbonDropdownZoneBounds",
                BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            var args = new object[] { button!, Rect.Empty };
            ((bool)method!.Invoke(null, args)!).Should().BeTrue();
            var dropdownBounds = (Rect)args[1];
            dropdownBounds.Y.Should().BeGreaterThan(0, "this check is only for horizontal tall-button split zones");

            var labelBottom = EnumerateSelfAndVisualDescendants(button!)
                .Concat(EnumerateLogicalDescendants(button!))
                .OfType<TextBlock>()
                .Distinct()
                .Where(RibbonMetadata.IsCommandLabel)
                .Where(IsEffectivelyVisible)
                .Select(label => label.TransformToAncestor(button!)
                    .TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight))
                    .Bottom)
                .DefaultIfEmpty(0)
                .Max();

            return dropdownBounds.Y >= labelBottom - 0.5;
        }

        private Button? ActiveRibbonButton(string title) =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

        private static int DropdownChevronCount(ButtonBase button) =>
            EnumerateSelfAndVisualDescendants(button)
                .Concat(EnumerateLogicalDescendants(button))
                .Distinct()
                .Count(RibbonMetadata.IsDropdownChevron);

        private static bool HasDropdownZoneHighlight(ButtonBase button) =>
            RibbonMetadata.GetDropdownZoneHighlightAttached(button) &&
            System.Windows.Documents.AdornerLayer.GetAdornerLayer(button)
                ?.GetAdorners(button)
                ?.Any(adorner => adorner.GetType().Name == "RibbonDropdownZoneAdorner") == true;

        private static string GetButtonDebugName(Button button)
        {
            var title = RibbonTooltip.GetTitle(button);
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            var label = GetButtonLabel(button);
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            if (!string.IsNullOrWhiteSpace(button.Name))
                return button.Name;

            return button.Content?.ToString() ?? button.GetType().Name;
        }

        private IEnumerable<UIElement> HomeRibbonChildren =>
            (_window.FindName("HomeRibbonPanel") as StackPanel)?.Children.Cast<UIElement>() ?? [];

        public string DebugRibbonChildren =>
            string.Join(", ", HomeRibbonChildren.Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}"
                    : child.GetType().Name));

        public string DebugActiveRibbonChildren =>
            $"RibbonTabs={(_window.FindName("RibbonTabs") as TabControl)?.ActualWidth:0.0}, " +
            $"ActivePanelDesired={ActiveRibbonPanel?.DesiredSize.Width:0.0}, " +
            string.Join(", ", ActiveRibbonPanel?.Children.Cast<UIElement>().Select(child =>
                child is FrameworkElement fe
                    ? $"{child.GetType().Name}:{fe.Tag}:{fe.Visibility}:{RibbonTooltip.GetTitle(fe) ?? fe.Name}:{fe.DesiredSize.Width:0.0}/{fe.ActualWidth:0.0}"
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

        public IReadOnlyList<string> TallLargeRibbonCommandLabels =>
            (SelectedRibbonTab is null
                ? []
                : EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                    .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                    .OfType<Button>()
                    .Distinct()
                    .Where(IsEffectivelyVisible)
                    .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                    .Where(IsTallLargeRibbonCommand)
                    .Select(GetButtonLabel)
                    .Where(label => !string.IsNullOrWhiteSpace(label)))
            .ToList();

        public IReadOnlyList<int> VisibleRibbonButtonContentIdentityHashCodes =>
            (SelectedRibbonTab is null
                ? []
                : EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                    .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                    .OfType<Button>()
                    .Distinct()
                    .Where(IsEffectivelyVisible)
                    .Select(button => button.Content)
                    .Where(content => content is not null)
                    .Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode))
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

        public double ActiveRibbonGroupCommandOverflow(string groupName)
        {
            if (FindActiveRibbonGroup(groupName) is not { } group)
                return 0;

            var labelTop = group.Children
                .OfType<Border>()
                .Where(border => Grid.GetRow(border) == 1)
                .Select(border => border.TransformToAncestor(group).Transform(new Point(0, 0)).Y)
                .DefaultIfEmpty(group.ActualHeight)
                .Min();

            var maxCommandBottom = EnumerateSelfAndVisualDescendants(group)
                .OfType<Button>()
                .Where(IsEffectivelyVisible)
                .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                .Select(button =>
                {
                    var top = button.TransformToAncestor(group).Transform(new Point(0, 0)).Y;
                    return top + button.ActualHeight;
                })
                .DefaultIfEmpty(0)
                .Max();

            return maxCommandBottom - labelTop;
        }

        public IReadOnlyList<int> ActiveRibbonGroupDenseCommandRows(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .OfType<UniformGrid>()
                    .Where(grid => grid.Children.OfType<Button>().Count() > 3)
                    .Select(grid => grid.Rows)
                    .ToList()
                : [];

        public IReadOnlyList<DenseCommandPlacement> ActiveRibbonGroupDenseCommandPlacements(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .OfType<UniformGrid>()
                    .Where(grid => grid.Rows > 0 && grid.Children.OfType<Button>().Count() > 3)
                    .SelectMany(GetDenseCommandPlacements)
                    .ToList()
                : [];

        public IReadOnlyList<string> ActiveRibbonGroupClippedCommandLabels(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .Concat(EnumerateLogicalDescendants(group))
                    .OfType<TextBlock>()
                    .Distinct()
                    .Where(RibbonMetadata.IsCommandLabel)
                    .Where(IsEffectivelyVisible)
                    .Where(IsTextVisuallyClipped)
                    .Select(FormatClippedTextBlock)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList()
                : [];

        public IReadOnlyList<string> ActiveRibbonGroupCommandLabelsWithoutIconSlots(string groupName) =>
            FindActiveRibbonGroup(groupName) is { } group
                ? EnumerateSelfAndVisualDescendants(group)
                    .OfType<Button>()
                    .Where(IsEffectivelyVisible)
                    .Where(button => !RibbonMetadata.IsCollapsedGroupButton(button))
                    .Select(button => new { Label = GetButtonLabel(button), HasIconSlot = TryGetCommandIconSlot(button, out _) })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Label) && !item.HasIconSlot)
                    .Select(item => item.Label)
                    .ToList()
                : [];

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

        public IReadOnlyList<RibbonIconStackOffsets> StackedRibbonRowColumnIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<StackPanel>()
                .Where(panel => panel.Orientation == Orientation.Vertical)
                .SelectMany(GetStackedRowColumnIconOffsets)
                .ToList();

        public IReadOnlyList<RibbonIconStackOffsets> GridRibbonColumnIconOffsets =>
            EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .OfType<Grid>()
                .Where(grid => !RibbonMetadata.IsRibbonGroup(grid) &&
                               !RibbonMetadata.TryGetCommandContentLayout(grid, out _))
                .SelectMany(GetGridColumnIconOffsets)
                .ToList();

        public IReadOnlyList<CheckBoxLabelOffset> ViewShowCheckBoxLabelOffsets =>
            ViewShowCheckBoxes
                .Select(checkBox => new CheckBoxLabelOffset(
                    checkBox.Name,
                    GetCheckBoxLabelOffset(checkBox)))
                .ToList();

        public IReadOnlyList<System.Windows.HorizontalAlignment> ViewShowCheckBoxContentAlignments =>
            ViewShowCheckBoxes
                .Select(checkBox => checkBox.HorizontalContentAlignment)
                .ToList();

        public IReadOnlyList<string> VisibleViewShowCheckBoxLabels =>
            ViewShowCheckBoxes
                .Select(checkBox => checkBox.Content?.ToString() ?? "")
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToList();

        public bool? ViewRulerCheckBoxIsEnabled =>
            (_window.FindName("ViewRulerChk") as CheckBox)?.IsEnabled;

        public IReadOnlyList<ScrollBarVisibility> RibbonHorizontalScrollBarModes =>
            _window.FindName("RibbonTabs") is TabControl tabs
                ? EnumerateSelfAndVisualDescendants(tabs)
                    .OfType<ScrollViewer>()
                    .Where(IsEffectivelyVisible)
                    .Select(scrollViewer => scrollViewer.HorizontalScrollBarVisibility)
                    .ToList()
                : [];

        public ScrollBarVisibility? ActiveRibbonHorizontalScrollBarMode =>
            ActiveRibbonScrollViewer?.HorizontalScrollBarVisibility;

        public IReadOnlyList<string> ActiveRibbonVisibleHorizontalScrollBars =>
            ActiveRibbonScrollViewer is { } scrollViewer
                ? EnumerateSelfAndVisualDescendants(scrollViewer)
                    .OfType<ScrollBar>()
                    .Where(scrollBar => scrollBar.Orientation == Orientation.Horizontal)
                    .Where(IsEffectivelyVisible)
                    .Where(scrollBar => scrollBar.ActualWidth > 0 && scrollBar.ActualHeight > 0)
                    .Select(scrollBar => $"{scrollBar.Name}:{scrollBar.Visibility}:{scrollBar.ActualWidth:0.#}x{scrollBar.ActualHeight:0.#}")
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

        public double ActiveRibbonPanelUnusedWidth
        {
            get
            {
                if (ActiveRibbonPanel is not { } panel)
                    return 0;

                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var viewport = FindVisualAncestor<ScrollViewer>(panel)?.ActualWidth;
                if (viewport is null or <= 0)
                    viewport = (_window.FindName("RibbonTabs") as TabControl)?.ActualWidth;

                return Math.Max(0, (viewport ?? 0) - 4 - panel.DesiredSize.Width);
            }
        }

        private TabItem? SelectedRibbonTab =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem as TabItem;

        private DependencyObject SelectedRibbonContentRoot =>
            SelectedRibbonTab?.Content as DependencyObject ??
            (DependencyObject?)SelectedRibbonTab ??
            _window;

        private IReadOnlyList<CheckBox> ViewShowCheckBoxes =>
            new[] { "ViewGridlinesChk", "ViewHeadersChk", "ViewRulerChk", "ViewFormulaBarChk" }
                .Select(name => _window.FindName(name))
                .OfType<CheckBox>()
                .Where(IsEffectivelyVisible)
                .ToList();

        private StackPanel? ActiveRibbonPanel =>
            SelectedRibbonTab is { } tabItem
                ? EnumerateSelfAndVisualDescendants(tabItem.Content as DependencyObject ?? tabItem)
                    .Concat(EnumerateLogicalDescendants(tabItem.Content as DependencyObject ?? tabItem))
                    .OfType<StackPanel>()
                    .Distinct()
                    .Where(panel => FindVisualAncestor<Button>(panel) is not { } button ||
                                    !RibbonMetadata.IsCollapsedGroupButton(button))
                    .OrderByDescending(panel => panel.Children.OfType<DependencyObject>().Count(RibbonMetadata.IsRibbonGroup))
                    .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                             panel.Children.OfType<DependencyObject>().Any(RibbonMetadata.IsRibbonGroup))
                : null;

        private ScrollViewer? ActiveRibbonScrollViewer =>
            ActiveRibbonPanel is { } panel
                ? FindVisualAncestor<ScrollViewer>(panel)
                : null;

        private Grid? FindActiveRibbonGroup(string groupName) =>
            (ActiveRibbonPanel?.Children.Cast<UIElement>() ?? [])
                .OfType<Grid>()
                .FirstOrDefault(grid => RibbonMetadata.TryGetGroupName(grid, out var candidate) &&
                                        string.Equals(candidate, groupName, StringComparison.Ordinal));

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

        public bool CanUseRequestedRibbonWidth(double width) =>
            _window.ActualWidth >= width - 1;

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
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void NormalizeRibbonSurface()
        {
            _normalizeRibbonSurface.Invoke(_window, [true]);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ShowPivotContextualTabs()
        {
            if (_window.FindName("PivotTableAnalyzeTab") is TabItem analyzeTab)
                analyzeTab.Visibility = Visibility.Visible;
            if (_window.FindName("PivotTableDesignTab") is TabItem designTab)
                designTab.Visibility = Visibility.Visible;

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ShowTableDesignContextualTab()
        {
            if (_window.FindName("TableDesignTab") is TabItem tableTab)
                tableTab.Visibility = Visibility.Visible;

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void ClickActiveRibbonButton(string title)
        {
            var button = EnumerateSelfAndVisualDescendants(SelectedRibbonContentRoot)
                .Concat(EnumerateLogicalDescendants(SelectedRibbonContentRoot))
                .OfType<Button>()
                .Distinct()
                .FirstOrDefault(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal));

            button.Should().NotBeNull(DebugActiveRibbonChildren);
            button!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var window = SharedWindow ??= CreateSharedWindow();
            if (!window.IsVisible)
                window.Show();

            window.WindowState = WindowState.Normal;
            window.Width = 1280;
            window.Height = 720;
            if (window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            window.UpdateLayout();
            PumpDispatcher();
            var harness = new MainWindowHarness(window);
            harness.ResetUiState();
            return harness;
        }

        private static MainWindow CreateSharedWindow()
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
                workbook,
                NullUserMessageService.Instance);

            window.Width = 1280;
            window.Height = 720;
            window.Show();
            PumpDispatcher();
            return window;
        }

        public void Dispose()
        {
            ResetUiState();
        }

        private void ResetUiState()
        {
            foreach (var menu in CollapsedRibbonGroupMenus)
                menu.IsOpen = false;
            if (VisibleOrCollapsedRibbonButton("Find & Select") is { } findSelect)
                findSelect.IsEnabled = true;
            if (_window.FindName("TableDesignTab") is TabItem tableDesignTab)
                tableDesignTab.Visibility = Visibility.Collapsed;
            if (_window.FindName("PivotTableAnalyzeTab") is TabItem pivotAnalyzeTab)
                pivotAnalyzeTab.Visibility = Visibility.Collapsed;
            if (_window.FindName("PivotTableDesignTab") is TabItem pivotDesignTab)
                pivotDesignTab.Visibility = Visibility.Collapsed;
            if (_window.FindName("RibbonTabs") is TabControl tabs)
                tabs.SelectedIndex = 1;
            _window.UpdateLayout();
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

        private static string GetButtonLabel(ButtonBase button)
        {
            if (button.Content is string text)
                return text;

            return EnumerateSelfAndVisualDescendants(button)
                .Concat(EnumerateLogicalDescendants(button))
                .OfType<TextBlock>()
                .FirstOrDefault(RibbonMetadata.IsCommandLabel)
                ?.Text ?? "";
        }

        private static string GetRibbonPresentationGroupName(FrameworkElement element)
        {
            if (RibbonMetadata.IsRibbonGroup(element) &&
                RibbonMetadata.TryGetGroupName(element, out var groupName))
            {
                return groupName;
            }

            if (element is Button button &&
                RibbonMetadata.IsCollapsedGroupButton(button))
            {
                return RibbonTooltip.GetTitle(button) ?? "";
            }

            return "";
        }

        private static double GetIconSlotOffset(Visual ancestor, ButtonBase button)
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
                .OfType<ButtonBase>()
                .Where(IsEffectivelyVisible)
                .Where(button => TryGetCommandIconSlot(button, out _))
                .ToArray();

            if (buttons.Length < 2)
                yield break;

            yield return new RibbonIconStackOffsets(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetDirectIconSlotCenterOffset(panel, button)).ToArray());
        }

        private static IEnumerable<RibbonIconStackOffsets> GetStackedRowColumnIconOffsets(StackPanel panel)
        {
            var rows = panel.Children
                .OfType<StackPanel>()
                .Where(row => row.Orientation == Orientation.Horizontal)
                .Select(row => row.Children
                    .OfType<ButtonBase>()
                    .Where(IsSmallLabeledCommandButton)
                    .ToArray())
                .Where(buttons => buttons.Length >= 2)
                .ToArray();
            if (rows.Length < 2)
                yield break;

            var columnCount = rows.Max(row => row.Length);
            for (var column = 0; column < columnCount; column++)
            {
                var buttons = rows
                    .Where(row => column < row.Length)
                    .Select(row => row[column])
                    .ToArray();
                if (buttons.Length < 2)
                    continue;

                yield return new RibbonIconStackOffsets(
                    buttons.Select(GetButtonLabel).ToArray(),
                    buttons.Select(button => GetDirectIconSlotCenterOffset(panel, button)).ToArray());
            }
        }

        private static IEnumerable<RibbonIconStackOffsets> GetGridColumnIconOffsets(Grid grid)
        {
            var columns = grid.Children
                .OfType<ButtonBase>()
                .Where(IsSmallLabeledCommandButton)
                .GroupBy(Grid.GetColumn)
                .Where(group => group.Count() >= 2);

            foreach (var column in columns)
            {
                var buttons = column.ToArray();
                yield return new RibbonIconStackOffsets(
                    buttons.Select(GetButtonLabel).ToArray(),
                    buttons.Select(button => GetDirectIconSlotCenterOffset(grid, button)).ToArray());
            }
        }

        private static IEnumerable<ButtonBase> GetSmallCommandButtons(Panel panel) =>
            panel.Children.OfType<ButtonBase>()
                .Where(IsEffectivelyVisible)
                .Where(button => button.Content is FrameworkElement content &&
                                 RibbonMetadata.TryGetCommandContentLayout(content, out var layout) &&
                                 layout == RibbonCommandContentLayout.Small &&
                                 TryGetCommandIconSlot(button, out _));

        private static bool IsSmallLabeledCommandButton(ButtonBase button) =>
            IsEffectivelyVisible(button) &&
            button.Content is FrameworkElement content &&
            RibbonMetadata.TryGetCommandContentLayout(content, out var layout) &&
            layout == RibbonCommandContentLayout.Small &&
            TryGetCommandIconSlot(button, out _) &&
            EnumerateSelfAndVisualDescendants(content)
                .OfType<TextBlock>()
                .Any(textBlock => RibbonMetadata.IsCommandLabel(textBlock) &&
                                  IsEffectivelyVisible(textBlock));

        private static bool IsTallLargeRibbonCommand(Button button) =>
            button.Content is StackPanel { Orientation: Orientation.Vertical } content &&
            RibbonMetadata.TryGetCommandContentLayout(content, out var layout) &&
            layout == RibbonCommandContentLayout.Large &&
            (button.ActualHeight >= 64 || button.Height >= 64) &&
            EnumerateSelfAndVisualDescendants(content)
                .OfType<TextBlock>()
                .Any(textBlock => RibbonMetadata.IsCommandLabel(textBlock) &&
                                  IsEffectivelyVisible(textBlock));

        private static IEnumerable<DenseCommandPlacement> GetDenseCommandPlacements(UniformGrid grid)
        {
            var buttons = grid.Children.OfType<Button>().Where(IsEffectivelyVisible).ToArray();
            var columns = grid.Columns > 0
                ? grid.Columns
                : (int)Math.Ceiling(buttons.Length / (double)Math.Max(1, grid.Rows));
            for (var index = 0; index < buttons.Length; index++)
            {
                var label = GetButtonLabel(buttons[index]);
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                yield return new DenseCommandPlacement(label, index / columns, index % columns);
            }
        }

        private static RibbonIconStackOffsets CreateIconStackOffsets(Visual ancestor, IReadOnlyList<ButtonBase> buttons) =>
            new(
                buttons.Select(GetButtonLabel).ToArray(),
                buttons.Select(button => GetIconSlotOffset(ancestor, button)).ToArray());

        private static double GetDirectIconSlotCenterOffset(Visual ancestor, ButtonBase button)
        {
            if (!TryGetCommandIconSlot(button, out var iconSlot))
            {
                return double.NaN;
            }

            var point = iconSlot.TransformToAncestor(ancestor).Transform(new Point(0, 0));
            return point.X + iconSlot.ActualWidth / 2;
        }

        private static bool TryGetCommandIconSlot(ButtonBase button, out FrameworkElement iconSlot)
        {
            iconSlot = null!;
            var contentRoot = button.Content as DependencyObject ?? button;
            iconSlot = EnumerateSelfAndVisualDescendants(contentRoot)
                .OfType<FrameworkElement>()
                .FirstOrDefault(element => RibbonMetadata.IsCommandIcon(element) &&
                                           !RibbonMetadata.IsCollapsedChevron(element))!;
            return iconSlot is not null;
        }

        private static bool IsTextVisuallyClipped(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Width > textBlock.ActualWidth + 0.5;
        }

        private static string FormatClippedTextBlock(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return $"{textBlock.Text} ({textBlock.ActualWidth:0.#}/{textBlock.DesiredSize.Width:0.#})";
        }

        private static bool IsVisibleCollapsedGroupButton(Button button) =>
            RibbonMetadata.IsCollapsedGroupButton(button) &&
            button.Visibility == Visibility.Visible;

        private static bool HasVisibleCollapsedGroupDropdownGlyph(Button button) =>
            System.Windows.Documents.AdornerLayer.GetAdornerLayer(button)
                ?.GetAdorners(button)
                ?.SelectMany(EnumerateSelfAndVisualDescendants)
                .OfType<TextBlock>()
                .Any(textBlock => RibbonMetadata.IsCollapsedChevron(textBlock) &&
                                  textBlock.Visibility == Visibility.Visible &&
                                  textBlock.IsVisible) == true;

        private static double GetCheckBoxLabelOffset(CheckBox checkBox)
        {
            var presenter = EnumerateSelfAndVisualDescendants(checkBox)
                .OfType<ContentPresenter>()
                .FirstOrDefault(contentPresenter => Equals(contentPresenter.Content, checkBox.Content));
            presenter.Should().NotBeNull($"the {checkBox.Name} checkbox should expose a content presenter for its label");

            var stack = FindVisualAncestor<StackPanel>(checkBox);
            stack.Should().NotBeNull($"the {checkBox.Name} checkbox should be hosted in the View tab Show stack");

            return presenter!.TransformToAncestor(stack!).Transform(new Point(0, 0)).X;
        }

        private static T? FindVisualAncestor<T>(DependencyObject element)
            where T : DependencyObject
        {
            for (var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
                 current is not null;
                 current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            {
                if (current is T match)
                    return match;
            }

            return null;
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

    public sealed record CheckBoxLabelOffset(string Name, double Offset);

    public sealed record CollapsedGroupKeyTip(string GroupName, string KeyTip);

    public sealed record DenseCommandPlacement(string Label, int Row, int Column);

    private sealed record RibbonFallbackExpectation(
        string Tab,
        double Width,
        IReadOnlyList<string> Expanded,
        IReadOnlyList<string> Collapsed);

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
