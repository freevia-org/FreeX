using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureNativeDialogInitialFocus(
        Window dialog,
        Control root,
        Control initialFocus)
    {
        dialog.Focusable = true;
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);

        var focusEstablished = false;
        var focusAttemptInProgress = false;
        var timedRetryCount = 0;
        var timedRetries = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(25),
        };

        bool EstablishInitialFocus()
        {
            if (focusEstablished || focusAttemptInProgress || !dialog.IsVisible)
                return focusEstablished;

            focusAttemptInProgress = true;
            try
            {
                if (initialFocus.IsVisible && initialFocus.IsEffectivelyEnabled)
                {
                    initialFocus.BringIntoView();
                    if (initialFocus.Focus() &&
                        ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), initialFocus))
                    {
                        focusEstablished = true;
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                focusAttemptInProgress = false;
            }
        }

        void QueueFocusRetries()
        {
            Dispatcher.UIThread.Post(() => EstablishInitialFocus(), DispatcherPriority.Input);
            Dispatcher.UIThread.Post(() => EstablishInitialFocus(), DispatcherPriority.Background);
            if (!focusEstablished && !timedRetries.IsEnabled)
                timedRetries.Start();
        }

        timedRetries.Tick += (_, _) =>
        {
            timedRetryCount++;
            if (EstablishInitialFocus() || !dialog.IsVisible || timedRetryCount >= 8)
                timedRetries.Stop();
        };

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
            QueueFocusRetries();
        };
        dialog.Activated += (_, _) =>
        {
            EstablishInitialFocus();
            if (!focusEstablished)
                QueueFocusRetries();
        };
        dialog.Closed += (_, _) =>
        {
            timedRetries.Stop();
            dialog.LayoutUpdated -= layoutUpdated;
        };
    }

    private static void ConfigureDeferredDialogCancel(Window dialog, Button cancelButton)
    {
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
                    return;

                args.Handled = true;
                Dispatcher.UIThread.Post(
                    () => InvokeDeferredDialogCancel(dialog, cancelButton),
                    DispatcherPriority.Input);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void InvokeDeferredDialogCancel(Window dialog, Button cancelButton)
    {
        if (!dialog.IsVisible)
            return;

        cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelButton));
        if (dialog.IsVisible)
            dialog.Close();
    }
}
