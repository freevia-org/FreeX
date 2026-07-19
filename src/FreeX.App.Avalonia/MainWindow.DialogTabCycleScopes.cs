using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
            RoutingStrategies.Tunnel);
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

        var tabStops = GetDialogTabStops(dialog, root);
        if (tabStops.Length == 0)
            return;

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        var currentIndex = focused is null ? -1 : tabStops.IndexOf(focused);
        var nextIndex = args.KeyModifiers == KeyModifiers.Shift
            ? currentIndex <= 0 ? tabStops.Length - 1 : currentIndex - 1
            : currentIndex < 0 || currentIndex == tabStops.Length - 1 ? 0 : currentIndex + 1;

        tabStops[nextIndex].Focus();
        args.Handled = true;
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
                control.IsVisible &&
                control.IsEffectivelyEnabled &&
                control is not TabControl &&
                control is not TabItem)
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
}
