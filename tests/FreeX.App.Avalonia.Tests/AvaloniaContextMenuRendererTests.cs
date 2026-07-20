using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using FreeX.App.Services.Ribbon;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for <see cref="AvaloniaContextMenuRenderer"/>, the converter that turns a shared neutral
/// <see cref="RibbonMenu"/> into an Avalonia <see cref="ContextMenu"/> / <see cref="MenuItem"/> tree.
/// Also exercises the worksheet cell-menu factory built on top of the shared planner/adapter.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaContextMenuRendererTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static RibbonMenu SampleMenu() => new(new[]
    {
        new RibbonMenuItem("Cu_t", new RibbonCommandId("Cut")),
        new RibbonMenuItem("_Copy", new RibbonCommandId("Copy")),
        RibbonMenuItem.Separator(),
        new RibbonMenuItem(
            "_Clear",
            CommandId: null,
            Children: new[]
            {
                new RibbonMenuItem("Clear C_ontents", new RibbonCommandId("ClearContents")),
                new RibbonMenuItem("Clear _Hyperlinks", new RibbonCommandId("ClearHyperlinks")) { IsEnabled = false },
            }),
    });

    [Fact]
    public Task BuildContextMenu_TopLevelItemCountAndHeaders() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });

        var items = menu.Items.Cast<Control>().ToList();
        Assert.Equal(4, items.Count);

        Assert.Equal("Cu_t", Assert.IsType<MenuItem>(items[0]).Header);
        Assert.Equal("_Copy", Assert.IsType<MenuItem>(items[1]).Header);
        Assert.IsType<Separator>(items[2]);
        Assert.Equal("_Clear", Assert.IsType<MenuItem>(items[3]).Header);
    });

    [Fact]
    public Task BuildContextMenu_RendersSeparator() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });
        Assert.Contains(menu.Items.Cast<object>(), i => i is Separator);
    });

    [Fact]
    public Task BuildContextMenu_RecursesChildrenAndNesting() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });

        var clear = menu.Items.Cast<Control>().OfType<MenuItem>().Single(m => (string?)m.Header == "_Clear");
        var children = clear.Items.Cast<Control>().ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal("Clear C_ontents", Assert.IsType<MenuItem>(children[0]).Header);
        Assert.Equal("Clear _Hyperlinks", Assert.IsType<MenuItem>(children[1]).Header);
    });

    [Fact]
    public Task BuildContextMenu_HonorsIsEnabled() => RunOnUiThread(() =>
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(SampleMenu(), _ => { });

        var clear = menu.Items.Cast<Control>().OfType<MenuItem>().Single(m => (string?)m.Header == "_Clear");
        var children = clear.Items.Cast<MenuItem>().ToList();
        Assert.True(children[0].IsEnabled);
        Assert.False(children[1].IsEnabled);
    });

    [Fact]
    public Task BuildContextMenu_HonorsCheckedState() => RunOnUiThread(() =>
    {
        var plan = new RibbonMenu(
        [
            new RibbonMenuItem("Selected", new RibbonCommandId("selected")) { IsChecked = true },
            new RibbonMenuItem("Other", new RibbonCommandId("other")) { IsChecked = false },
        ]);

        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(plan, _ => { });
        var items = menu.Items.Cast<MenuItem>().ToArray();

        Assert.All(items, item => Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType));
        Assert.True(items[0].IsChecked);
        Assert.False(items[1].IsChecked);
    });

    [Fact]
    public Task BuildContextMenu_LeafClickDispatchesCommandId() => RunOnUiThread(() =>
    {
        var dispatched = new List<string>();
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
            SampleMenu(),
            id => dispatched.Add(id.Value));

        var cut = menu.Items.Cast<MenuItem>().First(m => (string?)m.Header == "Cu_t");
        cut.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(new[] { "Cut" }, dispatched);
    });

    [Fact]
    public Task BuildContextMenu_SubmenuParentDoesNotDispatch() => RunOnUiThread(() =>
    {
        var dispatched = new List<string>();
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
            SampleMenu(),
            id => dispatched.Add(id.Value));

        var clear = menu.Items.Cast<Control>().OfType<MenuItem>().Single(m => (string?)m.Header == "_Clear");
        clear.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Empty(dispatched);
    });

    [Fact]
    public Task WorksheetCellMenuFactory_ProducesPopulatedMenuFromPlanner() => RunOnUiThread(() =>
    {
        var commands = WorksheetContextMenuPlanner.BuildCommands(WorksheetContextMenuTargetKind.Worksheet);
        var ribbonMenu = WorksheetContextMenuRibbonAdapter.ToRibbonMenu(commands);
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(ribbonMenu, _ => { });

        var items = menu.Items.Cast<Control>().ToList();
        Assert.NotEmpty(items);

        // Cut/Copy/Paste are the first three commands from the worksheet planner.
        var headers = items.OfType<MenuItem>().Select(m => (string?)m.Header).ToList();
        Assert.Contains("Cu_t", headers);
        Assert.Contains("_Copy", headers);
        Assert.Contains("_Paste", headers);

        // The planner produces submenus (e.g. Clear) → at least one MenuItem has children.
        Assert.Contains(items.OfType<MenuItem>(), m => m.Items.Count > 0);

        // And separators are present in the worksheet menu.
        Assert.Contains(items, i => i is Separator);
    });
}
