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
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (dialog.IsVisible)
                    focusInitialControl();
            },
            DispatcherPriority.Input);
        dialog.KeyDown += (_, args) => CloseOwnedModelessWindowOnEscape(dialog, args);
        dialog.Closed += (_, _) =>
        {
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
