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
        if (args.Handled && args.Key != Key.Tab)
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

        var tabStops = GetDialogTabStops(dialog, root);
        if (tabStops.Length == 0)
            return;

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        var currentIndex = focused is null ? -1 : tabStops.IndexOf(focused);
        var nextIndex = args.KeyModifiers == KeyModifiers.Shift
            ? currentIndex <= 0 ? tabStops.Length - 1 : currentIndex - 1
            : currentIndex < 0 || currentIndex == tabStops.Length - 1 ? 0 : currentIndex + 1;

        args.Handled = true;
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

    private static Control[] GetDialogTabStops(Window dialog, Control root)
    {
        var controls = dialog.GetVisualDescendants()
            .OfType<Control>()
            .Prepend(root)
            .Where(control =>
                control != root &&
                control.Focusable &&
                KeyboardNavigation.GetIsTabStop(control) &&
                IsEffectivelyVisibleWithin(control, root) &&
                control.IsEffectivelyEnabled &&
                control is not TabControl &&
                control is not TabItem &&
                IsSelectedListBoxItem(control))
            .Select(control =>
            {
                var origin = control.TranslatePoint(default, root) ?? default;
                return (Control: control, Origin: origin);
            })
            .OrderBy(item => item.Origin.Y)
            .ThenBy(item => item.Origin.X)
            .Select(item => item.Control)
            .ToArray();

        return controls;
    }

    private static bool IsSelectedListBoxItem(Control control)
    {
        if (control is not ListBoxItem listBoxItem)
            return true;

        var listBox = listBoxItem.GetLogicalAncestors().OfType<ListBox>().FirstOrDefault();
        return listBox is null || Equals(listBox.SelectedItem, listBoxItem.Content);
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
