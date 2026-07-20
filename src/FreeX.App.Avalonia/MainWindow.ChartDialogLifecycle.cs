using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureChartDialogKeyboardLifecycle(Window dialog, Control initialFocus)
    {
        dialog.Focusable = true;
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        var initialFocusEstablished = false;
        var focusing = false;

        bool EstablishInitialFocus()
        {
            if (initialFocusEstablished || focusing || !dialog.IsVisible)
                return initialFocusEstablished;

            focusing = true;
            try
            {
                if (initialFocus.IsVisible && initialFocus.IsEffectivelyEnabled)
                {
                    initialFocus.BringIntoView();
                    if (FocusDialogControl(initialFocus) &&
                        ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), initialFocus))
                    {
                        initialFocusEstablished = true;
                        if (initialFocus is TextBox textBox)
                            textBox.SelectAll();
                        return true;
                    }
                }

                // Keep raw keyboard input rooted in the native dialog while its content is still
                // being realized. Activated/LayoutUpdated retries replace this fallback with the
                // WPF-matched initial control as soon as it can accept focus.
                dialog.Focus();
                return false;
            }
            finally
            {
                focusing = false;
            }
        }

        void QueueInitialFocusRetries()
        {
            Dispatcher.UIThread.Post(() => EstablishInitialFocus(), DispatcherPriority.Input);
            Dispatcher.UIThread.Post(() => EstablishInitialFocus(), DispatcherPriority.Background);
        }

        EventHandler? layoutUpdated = null;
        layoutUpdated = (_, _) =>
        {
            if (EstablishInitialFocus())
                dialog.LayoutUpdated -= layoutUpdated;
        };

        dialog.LayoutUpdated += layoutUpdated;
        dialog.Opened += (_, _) =>
        {
            dialog.Activate();
            dialog.UpdateLayout();
            EstablishInitialFocus();
            QueueInitialFocusRetries();
        };
        dialog.Activated += (_, _) =>
        {
            EstablishInitialFocus();
            if (!initialFocusEstablished)
                QueueInitialFocusRetries();
        };
        dialog.Closed += (_, _) => dialog.LayoutUpdated -= layoutUpdated;

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
                {
                    args.Handled = true;
                    if (dialog.IsVisible)
                        dialog.Close();
                    return;
                }

                if (args.Key == Key.Tab &&
                    (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) &&
                    !initialFocusEstablished)
                {
                    EstablishInitialFocus();
                    args.Handled = initialFocusEstablished;
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }
}
