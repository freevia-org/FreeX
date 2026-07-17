using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using FreeX.App.Services.Ribbon;

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
}
