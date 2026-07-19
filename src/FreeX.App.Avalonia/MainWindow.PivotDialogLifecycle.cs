using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// Applies the WPF keyboard contract shared by the Pivot dialog family. Avalonia's default traversal can
    /// skip the initial control or leave a nested tab/panel scope when the dialog is built dynamically, so the
    /// production dialog owns a bounded, visible focus graph for both Tab directions.
    /// </summary>
    private static void ConfigurePivotDialogLifecycle(
        Window dialog,
        Control initialFocus,
        bool selectAllText = false)
    {
        var root = dialog.Content as Control ?? dialog;
        ConfigureDialogTabCycle(dialog, root);

        EventHandler? layoutUpdated = null;
        var focusEstablished = false;

        void FocusInitialControl()
        {
            if (focusEstablished ||
                !dialog.IsVisible ||
                !initialFocus.IsVisible ||
                !initialFocus.IsEffectivelyEnabled)
            {
                return;
            }

            if (!ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), initialFocus) &&
                !FocusDialogControl(initialFocus))
            {
                return;
            }

            if (selectAllText && initialFocus is TextBox textBox && textBox.IsFocused)
                textBox.SelectAll();

            focusEstablished = true;
            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        }

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Tab ||
                    (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
                {
                    return;
                }

                // A user Tab is authoritative navigation. Do not let a queued activation/layout retry
                // reassert the opener's initial control after the user has already moved focus.
                focusEstablished = true;
                if (layoutUpdated is not null)
                    dialog.LayoutUpdated -= layoutUpdated;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        layoutUpdated = (_, _) => FocusInitialControl();
        dialog.Opened += (_, _) =>
        {
            FocusInitialControl();
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Background);
        };
        dialog.Activated += (_, _) =>
        {
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
        };
        dialog.LayoutUpdated += layoutUpdated;
        dialog.Closed += (_, _) =>
        {
            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        };
    }
}
