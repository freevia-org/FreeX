using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

namespace Free.Shared.Ribbon.Avalonia;

// Office's collapsed ribbon ("Show tabs only"): Ctrl+F1 or double-clicking a tab hides the command
// area; a click on a tab then shows it for one use, until a command runs or the user clicks elsewhere.
public static partial class AvaloniaRibbonRenderer
{
    private static readonly ConditionalWeakTable<TabControl, RibbonDisplayState> DisplayStates = new();

    /// <summary>
    /// The display state of a ribbon built by <see cref="BuildRibbon"/>. Hosts toggle it for Ctrl+F1
    /// and read <see cref="RibbonDisplayState.IsCollapsed"/> for the Collapse/Pin the Ribbon commands.
    /// </summary>
    public static RibbonDisplayState? GetDisplayState(Control ribbon)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        var tabControl = FindTabControl(ribbon);
        return tabControl is not null && DisplayStates.TryGetValue(tabControl, out var state) ? state : null;
    }

    private static void AttachDisplayState(TabControl tabControl)
    {
        var state = new RibbonDisplayState();
        DisplayStates.AddOrUpdate(tabControl, state);
        state.Changed += (_, _) => ApplyDisplayState(tabControl, state);

        tabControl.AddHandler(
            InputElement.DoubleTappedEvent,
            (_, e) =>
            {
                if (IsContentTabHeaderSource(tabControl, e.Source))
                {
                    state.ToggleCollapsed();
                    e.Handled = true;
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        tabControl.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) =>
            {
                if (state.IsCollapsed &&
                    e.GetCurrentPoint(tabControl).Properties.IsLeftButtonPressed &&
                    IsContentTabHeaderSource(tabControl, e.Source))
                {
                    state.OnTabClicked();
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // A button that runs a command (rather than opening a menu or gallery) closes the
        // temporarily shown ribbon, as in Office.
        tabControl.AddHandler(
            Button.ClickEvent,
            (_, e) =>
            {
                if (state.IsTemporarilyShown && e.Source is Button { Flyout: null } and not ToggleButton { IsChecked: null })
                    state.OnCommandExecuted();
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        TopLevel? attachedTopLevel = null;
        void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (state.IsTemporarilyShown && !IsInsideRibbonOrPopup(tabControl, e.Source))
                state.OnDismissed();
        }

        void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
        {
            if (state.IsTemporarilyShown && e.Key == Key.Escape)
                state.OnDismissed();
        }

        tabControl.AttachedToVisualTree += (_, _) =>
        {
            attachedTopLevel = TopLevel.GetTopLevel(tabControl);
            attachedTopLevel?.AddHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            attachedTopLevel?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        };
        tabControl.DetachedFromVisualTree += (_, _) =>
        {
            attachedTopLevel?.RemoveHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed);
            attachedTopLevel?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
            attachedTopLevel = null;
        };
    }

    /// <summary>Re-applies the display state to every tab body (after contextual tabs are added).</summary>
    private static void ApplyDisplayState(TabControl tabControl)
    {
        if (DisplayStates.TryGetValue(tabControl, out var state))
            ApplyDisplayState(tabControl, state);
    }

    private static void ApplyDisplayState(TabControl tabControl, RibbonDisplayState state)
    {
        foreach (var item in tabControl.Items.OfType<TabItem>())
        {
            if (item.Content is Control content)
                content.IsVisible = state.ShowsCommands;
        }
    }

    /// <summary>Key tips (Alt, H) select a tab: on a collapsed ribbon that shows it temporarily.</summary>
    private static void ShowCollapsedRibbonForKeyTip(TabControl tabControl)
    {
        if (DisplayStates.TryGetValue(tabControl, out var state))
            state.OnTabClicked();
    }

    // The ribbon's tab items render only their header (the selected body is drawn by the TabControl's
    // own presenter), so any source inside a TabItem is a click on that tab's header.
    private static bool IsContentTabHeaderSource(TabControl tabControl, object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, tabControl); visual = visual.GetVisualParent())
        {
            if (visual is TabItem item)
                return item.Tag is not string id || !string.Equals(id, FileRibbonTabId, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsInsideRibbonOrPopup(TabControl tabControl, object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, tabControl) || visual is PopupRoot or OverlayPopupHost)
                return true;
        }

        return false;
    }
}
