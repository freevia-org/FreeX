using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureDialogTabCycle(Window dialog, Control root)
    {
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => HandleDialogTabCycle(dialog, root, args),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void HandleDialogTabCycle(Window dialog, Control root, KeyEventArgs args)
    {
        if (args.Handled)
            return;

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            dialog.Close();
            args.Handled = true;
            return;
        }

        if (args.Key != Key.Tab ||
            (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
            return;

        var tabStops = GetDialogTabStops(root);
        if (tabStops.Length == 0)
            return;

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        var currentIndex = FindDialogTabStopIndex(tabStops, focused);
        var nextIndex = args.KeyModifiers == KeyModifiers.Shift
            ? currentIndex <= 0 ? tabStops.Length - 1 : currentIndex - 1
            : currentIndex < 0 || currentIndex == tabStops.Length - 1 ? 0 : currentIndex + 1;

        args.Handled = true;
        if (FocusDialogControl(tabStops[nextIndex]))
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (dialog.IsVisible)
                    FocusDialogControl(tabStops[nextIndex]);
            },
            DispatcherPriority.Input);
    }

    private static bool FocusDialogControl(Control target)
    {
        if (target.Focus())
            return true;

        if (target is ListBox listBox)
        {
            var selectedItem = listBox.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .FirstOrDefault(item => Equals(item.Content, listBox.SelectedItem));
            if (selectedItem?.Focus() == true)
                return true;
        }

        var firstFocusable = target.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control =>
                control.Focusable &&
                KeyboardNavigation.GetIsTabStop(control) &&
                control.IsVisible &&
                control.IsEffectivelyEnabled);
        return firstFocusable?.Focus() == true;
    }

    private static Control[] GetDialogTabStops(Control root)
    {
        var controls = root.GetLogicalDescendants()
            .OfType<Control>()
            .Prepend(root)
            .Select(GetAuthoredDialogTabStop)
            .Distinct()
            .Where(control =>
                control != root &&
                IsFocusableAuthoredDialogStop(control) &&
                KeyboardNavigation.GetIsTabStop(control) &&
                IsEffectivelyVisibleWithin(control, root) &&
                control.IsEffectivelyEnabled &&
                control is not TabControl &&
                control is not TabItem)
            .ToArray();

        return controls;
    }

    private static bool IsFocusableAuthoredDialogStop(Control control) =>
        control.Focusable ||
        control is ListBox && control.GetVisualDescendants().OfType<ListBoxItem>().Any(item =>
            item.Focusable && item.IsVisible && item.IsEffectivelyEnabled);

    private static Control GetAuthoredDialogTabStop(Control control)
    {
        // Item containers and their focusable content are implementation details of one
        // authored list stop. Linux commonly reports the selected ListBoxItem as focused,
        // so keep the ListBox in the graph and map focus back to it when moving onward.
        return control.GetLogicalAncestors().OfType<ListBox>().FirstOrDefault() ?? control;
    }

    private static int FindDialogTabStopIndex(Control[] tabStops, Control? focused)
    {
        if (focused is null)
            return -1;

        var exactIndex = tabStops.IndexOf(focused);
        if (exactIndex >= 0)
            return exactIndex;

        var visualAncestors = focused.GetVisualAncestors().OfType<Control>().ToHashSet();
        var logicalAncestors = focused.GetLogicalAncestors().OfType<Control>().ToHashSet();
        for (var index = 0; index < tabStops.Length; index++)
        {
            if (visualAncestors.Contains(tabStops[index]) || logicalAncestors.Contains(tabStops[index]))
                return index;
        }

        return -1;
    }

    private static bool IsEffectivelyVisibleWithin(Control control, Control root)
    {
        if (control.GetLogicalAncestors().OfType<TabItem>().Any(tabItem =>
            tabItem.GetLogicalParent<TabControl>() is { } tabControl &&
            !ReferenceEquals(tabControl.SelectedItem, tabItem)))
        {
            return false;
        }

        Visual? current = control;
        while (current is not null)
        {
            if (current is Control ancestor && !ancestor.IsVisible)
                return false;
            if (ReferenceEquals(current, root))
                return true;
            current = current.GetVisualParent();
        }

        return false;
    }
}
