using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigurePageSetupTabCycle(Window dialog, Control root)
    {
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => HandlePageSetupTabCycle(dialog, root, args),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void HandlePageSetupTabCycle(Window dialog, Control root, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            dialog.Close();
            args.Handled = true;
            return;
        }

        if (args.Key != Key.Tab ||
            (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
        {
            return;
        }

        var tabStops = GetPageSetupTabStops(root);
        if (tabStops.Length == 0)
            return;

        var focused = dialog.FocusManager?.GetFocusedElement() as Control;
        var currentIndex = focused is null ? -1 : Array.IndexOf(tabStops, focused);
        var nextIndex = args.KeyModifiers == KeyModifiers.Shift
            ? currentIndex <= 0 ? tabStops.Length - 1 : currentIndex - 1
            : currentIndex < 0 || currentIndex == tabStops.Length - 1 ? 0 : currentIndex + 1;

        tabStops[nextIndex].Focus();
        args.Handled = true;
    }

    private static Control[] GetPageSetupTabStops(Control root)
    {
        var tabs = root.GetVisualDescendants()
            .OfType<TabControl>()
            .FirstOrDefault();
        var activeContent = (tabs?.SelectedItem as TabItem)?.Content as Control;
        var contentStops = activeContent is null
            ? []
            : activeContent.GetVisualDescendants()
                .OfType<Control>()
                .Prepend(activeContent)
                .Where(IsPageSetupTabStop)
                .ToArray();

        string[] footerOrder =
        [
            "PageSetupPrintButton",
            "PageSetupPrintPreviewButton",
            "PageSetupOptionsButton",
            "PageSetupOkButton",
            "PageSetupCancelButton",
        ];
        var footerStops = footerOrder
            .Select(id => root.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control =>
                    AutomationProperties.GetAutomationId(control) == id &&
                    IsPageSetupTabStop(control)))
            .OfType<Control>();

        return contentStops
            .Concat(footerStops)
            .Distinct()
            .ToArray();
    }

    private static bool IsPageSetupTabStop(Control control) =>
        control.Focusable &&
        KeyboardNavigation.GetIsTabStop(control) &&
        control.IsVisible &&
        control.IsEffectivelyEnabled &&
        control is not TabControl &&
        control is not TabItem;
}
