using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaCatalogContextMenuTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public Task RecentFiles_RenderRecentAndPinnedPlannerVariantsAndDispatch() => RunOnUiThread(() =>
    {
        var actions = new List<BackstageRecentFileMenuAction>();
        var recent = AvaloniaBackstageRecentFileContextMenu.BuildItems(
            false,
            "Budget.xlsx",
            key => key,
            actions.Add).Cast<MenuItem>().ToArray();
        var pinned = AvaloniaBackstageRecentFileContextMenu.BuildItems(
            true,
            "Budget.xlsx",
            key => key,
            actions.Add).Cast<MenuItem>().ToArray();

        recent.Select(item => item.Header).Should().Equal(
            "MainWindow_Header_PinToList",
            "MainWindow_Header_RemoveFromList");
        pinned.Select(item => item.Header).Should().Equal(
            "MainWindow_Header_UnpinFromList",
            "MainWindow_Header_RemoveFromList");
        AutomationProperties.GetAutomationId(recent[0]).Should().Be("BackstageRecentPinMenuItem");

        pinned[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        actions.Should().Equal(BackstageRecentFileMenuAction.Unpin);
    });

    [Fact]
    public Task QatCustomization_RendersDynamicAddRemoveAndLastItemDisablement() => RunOnUiThread(() =>
    {
        var add = AvaloniaQuickAccessToolbarContextMenu.BuildCustomizationItems(
            new QuickAccessToolbarCustomizationMenuState("Redo", ["Save", "Undo"]),
            key => key,
            _ => { }).Cast<MenuItem>().Single();
        var remove = AvaloniaQuickAccessToolbarContextMenu.BuildCustomizationItems(
            new QuickAccessToolbarCustomizationMenuState("Undo", ["Save", "Undo"]),
            key => key,
            _ => { }).Cast<MenuItem>().Single();
        var last = AvaloniaQuickAccessToolbarContextMenu.BuildCustomizationItems(
            new QuickAccessToolbarCustomizationMenuState("Save", ["Save"]),
            key => key,
            _ => { }).Cast<MenuItem>().Single();

        add.Header.Should().Be(QuickAccessToolbarContextMenuPlanner.AddHeaderResourceKey);
        AutomationProperties.GetAutomationId(add).Should().Be(QuickAccessToolbarContextMenuPlanner.AddAutomationId);
        remove.Header.Should().Be(QuickAccessToolbarContextMenuPlanner.RemoveHeaderResourceKey);
        remove.IsEnabled.Should().BeTrue();
        last.IsEnabled.Should().BeFalse();
    });

    [Fact]
    public Task QatHistory_RendersCountsAutomationAndEmptyPlaceholder() => RunOnUiThread(() =>
    {
        var dispatchedCounts = new List<int>();
        var history = AvaloniaQuickAccessToolbarContextMenu.BuildHistoryItems(
            new QuickAccessToolbarHistoryMenuState(false, ["Typing", "Paste"]),
            command => dispatchedCounts.Add(command.ActionCount)).Cast<MenuItem>().ToArray();
        var emptyRedo = AvaloniaQuickAccessToolbarContextMenu.BuildHistoryItems(
            new QuickAccessToolbarHistoryMenuState(true, []),
            _ => { }).Cast<MenuItem>().Single();

        history.Select(item => item.Header).Should().Equal("Typing", "Paste");
        AutomationProperties.GetAutomationId(history[1]).Should().Be("UndoQatHistoryItem2");
        history[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        dispatchedCounts.Should().Equal(2);
        emptyRedo.Header.Should().Be("No actions to redo");
        emptyRedo.IsEnabled.Should().BeFalse();
    });

    [Fact]
    public void ManagedMenu_RecognizesMenuKeyAndShiftF10Only()
    {
        AvaloniaManagedContextMenu.IsKeyboardInvocation(Key.Apps, KeyModifiers.None).Should().BeTrue();
        AvaloniaManagedContextMenu.IsKeyboardInvocation(Key.F10, KeyModifiers.Shift).Should().BeTrue();
        AvaloniaManagedContextMenu.IsKeyboardInvocation(Key.F10, KeyModifiers.None).Should().BeFalse();
    }

    [Fact]
    public Task ManagedMenu_AttachesDynamicItemsToPointerContextSurface() => RunOnUiThread(() =>
    {
        var anchor = new Button();
        var buildCount = 0;
        var menu = AvaloniaManagedContextMenu.Attach(anchor, () =>
        {
            buildCount++;
            return [new MenuItem { Header = $"Item {buildCount}" }];
        });

        anchor.ContextMenu.Should().BeSameAs(menu);
        menu.Items.Cast<MenuItem>().Single().Header.Should().Be("Item 1");
    });

    [Fact]
    public Task PivotField_RendersAvailableAndBucketVariantsAndDispatches() => RunOnUiThread(() =>
    {
        var actions = new List<PivotFieldContextMenuAction>();
        var available = AvaloniaPivotFieldContextMenu.BuildItems(false, key => key, actions.Add);
        var bucket = AvaloniaPivotFieldContextMenu.BuildItems(true, key => key, actions.Add);

        available.OfType<MenuItem>().Should().HaveCount(7);
        bucket.OfType<MenuItem>().Should().HaveCount(8);
        bucket.OfType<MenuItem>().Last().Header.Should().Be("MainWindow_Content_Remove");
        bucket.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        actions.Should().Equal(PivotFieldContextMenuAction.Remove);
    });

    [Fact]
    public Task PivotField_UsesPlannerKeyTipsAsMenuGesturesForAvailableAndBucketItems() => RunOnUiThread(() =>
    {
        var expected = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true)
            .Where(command => !command.IsSeparator)
            .Select(command => KeyGesture.Parse(command.KeyTip).Key)
            .ToArray();

        var available = AvaloniaPivotFieldContextMenu.BuildItems(false, key => key, _ => { })
            .OfType<MenuItem>()
            .Select(item => Assert.IsType<KeyGesture>(item.InputGesture).Key)
            .ToArray();
        var bucket = AvaloniaPivotFieldContextMenu.BuildItems(true, key => key, _ => { })
            .OfType<MenuItem>()
            .Select(item => Assert.IsType<KeyGesture>(item.InputGesture).Key)
            .ToArray();

        available.Should().Equal(expected[..^1]);
        bucket.Should().Equal(expected);
    });

    [Fact]
    public Task PivotField_KeyTipInvokesPlannerActionOnlyInsideOpenContextMenu() => RunOnUiThread(() =>
    {
        var anchor = new Button();
        var window = new Window { Content = anchor };
        var actions = new List<PivotFieldContextMenuAction>();
        var menu = AvaloniaManagedContextMenu.Attach(
            anchor,
            () => AvaloniaPivotFieldContextMenu.BuildItems(false, key => key, actions.Add));

        window.Show();
        menu.Open(anchor);
        menu.IsOpen.Should().BeTrue();
        var menuItems = menu.Items.OfType<MenuItem>().ToArray();
        var sortAscending = menuItems
            .Single(item => Assert.IsType<KeyGesture>(item.InputGesture).Key == Key.S);
        var focusedItem = menuItems.First(item => item != sortAscending && item.IsEnabled);
        focusedItem.Focus().Should().BeTrue();
        focusedItem.IsFocused.Should().BeTrue();

        var keyDown = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.S,
            KeyModifiers = KeyModifiers.None,
            Source = focusedItem,
        };
        focusedItem.RaiseEvent(keyDown);

        keyDown.Handled.Should().BeTrue();
        actions.Should().Equal(PivotFieldContextMenuAction.SortAscending);
        menu.IsOpen.Should().BeFalse();

        menu.Open(anchor);
        menu.IsOpen.Should().BeTrue();
        var escapeItem = menu.Items.OfType<MenuItem>().First(item => item.IsEnabled);
        escapeItem.Focus();
        var escape = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
            KeyModifiers = KeyModifiers.None,
            Source = escapeItem,
        };
        escapeItem.RaiseEvent(escape);

        escape.Handled.Should().BeTrue();
        menu.IsOpen.Should().BeFalse();

        var outsideMenuKeyDown = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.S,
            KeyModifiers = KeyModifiers.None,
            Source = anchor,
        };
        anchor.RaiseEvent(outsideMenuKeyDown);

        actions.Should().Equal(PivotFieldContextMenuAction.SortAscending);
        window.Close();
    });

    [Fact]
    public Task PivotChart_RendersFilterAndNoFilterVariantsWithPlannerEnablement() => RunOnUiThread(() =>
    {
        var filtered = AvaloniaPivotChartFieldContextMenu.BuildItems(
            PivotChartState(hasFilterState: true),
            _ => { }).OfType<MenuItem>().ToArray();
        var noFilter = AvaloniaPivotChartFieldContextMenu.BuildItems(
            PivotChartState(hasFilterState: false),
            _ => { }).OfType<MenuItem>().ToArray();

        filtered.First().Header.Should().Be("Region: Filtered");
        filtered.Single(item => Equals(item.Tag, PivotChartFieldContextMenuAction.ClearFilter))
            .IsEnabled.Should().BeTrue();
        noFilter.Should().NotContain(item => Equals(item.Tag, PivotChartFieldContextMenuAction.Summary));
        noFilter.Single(item => Equals(item.Tag, PivotChartFieldContextMenuAction.SelectItems))
            .IsEnabled.Should().BeFalse();
    });

    [Fact]
    public Task WaterfallPoint_RendersRegularTotalAndInvalidVariantsAndDispatches() => RunOnUiThread(() =>
    {
        var chart = CreateWaterfallChart();
        var dispatchCount = 0;
        var regular = AvaloniaWaterfallPointContextMenu.BuildItems(chart, 0, () => dispatchCount++)
            .Cast<MenuItem>().Single();
        var total = AvaloniaWaterfallPointContextMenu.BuildItems(chart, 3, () => { })
            .Cast<MenuItem>().Single();
        var invalid = AvaloniaWaterfallPointContextMenu.BuildItems(chart, 9, () => { })
            .Cast<MenuItem>().Single();

        regular.IsChecked.Should().BeFalse();
        total.IsChecked.Should().BeTrue();
        invalid.IsEnabled.Should().BeFalse();
        regular.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        dispatchCount.Should().Be(1);
    });

    private static PivotChartFieldContextMenuState PivotChartState(bool hasFilterState) => new(
        HasFilterState: hasFilterState,
        OverallSummary: "Region: Filtered",
        SelectItemsHeader: "Select Items...",
        LabelFilterHeader: "Label Filter...",
        ValueFilterHeader: "Value Filter...",
        ClearFilterHeader: "Clear Filters from Region",
        CanValueFilter: hasFilterState,
        HasAnyFilter: hasFilterState,
        CanValueFieldSettings: true);

    private static ChartModel CreateWaterfallChart()
    {
        var sheetId = SheetId.New();
        return new ChartModel
        {
            Type = ChartType.Waterfall,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 2)),
        };
    }
}
