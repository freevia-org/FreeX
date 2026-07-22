using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Window? _findReplaceDialog;
    private Action<bool>? _switchFindReplaceMode;

    private void ShowOwnedModelessWindow(
        Window dialog,
        Action focusInitialControl,
        Action? onClosed = null,
        bool closeOnDeactivate = false)
    {
        var ownerFocusBeforeOpen = FocusManager?.GetFocusedElement();
        dialog.Focusable = true;
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        var focusEstablished = false;
        var focusAttemptInProgress = false;
        var retryCount = 0;
        var focusRetries = new DispatcherTimer(DispatcherPriority.Input)
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
                dialog.Activate();
                dialog.UpdateLayout();
                focusInitialControl();
                if (dialog.FocusManager?.GetFocusedElement() is Visual focused &&
                    ReferenceEquals(TopLevel.GetTopLevel(focused), dialog))
                {
                    focusEstablished = true;
                    return true;
                }

                dialog.Focus();
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
            if (!focusEstablished && !focusRetries.IsEnabled)
                focusRetries.Start();
        }

        focusRetries.Tick += (_, _) =>
        {
            retryCount++;
            if (EstablishInitialFocus() || !dialog.IsVisible || retryCount >= 8)
                focusRetries.Stop();
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
            EstablishInitialFocus();
            QueueFocusRetries();
        };
        dialog.Activated += (_, _) =>
        {
            EstablishInitialFocus();
            if (!focusEstablished)
                QueueFocusRetries();
        };
        dialog.KeyDown += (_, args) => CloseOwnedModelessWindowOnEscape(dialog, args);
        dialog.Closed += (_, _) =>
        {
            focusRetries.Stop();
            dialog.LayoutUpdated -= layoutUpdated;
            onClosed?.Invoke();
            Dispatcher.UIThread.Post(
                () => RestoreOwnedModelessOwnerFocus(ownerFocusBeforeOpen),
                DispatcherPriority.Input);
        };

        if (closeOnDeactivate)
        {
            dialog.Deactivated += (_, _) => Dispatcher.UIThread.Post(
                () =>
                {
                    if (dialog.IsVisible && !dialog.IsActive)
                        dialog.Close();
                },
                DispatcherPriority.Background);
        }

        dialog.Show(this);
    }

    private static void CloseOwnedModelessWindowOnEscape(Window dialog, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
            return;

        var cancelButton = dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                button.IsCancel && button.IsVisible && button.IsEffectivelyEnabled);
        cancelButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelButton));
        if (dialog.IsVisible)
            dialog.Close();
        args.Handled = true;
    }

    private void RestoreOwnedModelessOwnerFocus(IInputElement? ownerFocusBeforeOpen)
    {
        if (!IsVisible)
            return;

        Activate();
        if (ownerFocusBeforeOpen is InputElement priorFocus &&
            priorFocus.Focusable && priorFocus.IsEffectivelyEnabled &&
            IsFocusInside(this, priorFocus))
        {
            priorFocus.Focus();
            return;
        }

        _sheetGridHost.Focus();
    }
}
