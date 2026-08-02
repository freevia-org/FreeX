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

    /// <summary>
    /// Closes the modeless Find &amp; Replace dialog (<see cref="_findReplaceDialog"/>) if it is
    /// currently open. Must be called from every workbook-replacing path (New, Open, Close
    /// Workbook, recovery-snapshot load -- see <see cref="ReplaceSession"/>) because the dialog
    /// captures its selection scope once at open time (<see cref="CaptureFindReplaceSelectionScopeAtOpen"/>)
    /// and reuses it for every Find Next/Find All/Replace/Replace All click for as long as it stays
    /// open (R119-avalonia-findreplace-stale-scope). A workbook replacement always creates fresh
    /// <see cref="SheetId"/>s, so a scope captured against the previous workbook can never again
    /// match any candidate in the new one -- silently returning zero matches forever instead of
    /// crashing or corrupting data. This mirrors the WPF host's
    /// <c>MainWindow.WorkbookUiState.CloseFindReplaceDialogIfOpen</c>, which exists for the exact
    /// same reason. <see cref="Window.Close()"/>'s <c>Closed</c> handler (registered in
    /// <see cref="ShowFindReplaceTabbedDialogAsync"/> via <see cref="ShowOwnedModelessWindow"/>)
    /// nulls out <see cref="_findReplaceDialog"/> and <see cref="_switchFindReplaceMode"/>, so there
    /// is nothing further to reset here.
    /// </summary>
    private void CloseFindReplaceDialogIfOpen()
    {
        _findReplaceDialog?.Close();
    }

    private void ShowOwnedModelessWindow(
        Window dialog,
        Action focusInitialControl,
        Action? onClosed = null,
        bool closeOnDeactivate = false)
    {
        var ownerFocusBeforeOpen = FocusManager?.GetFocusedElement();
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
                    !ReferenceEquals(focused, dialog) &&
                    ReferenceEquals(TopLevel.GetTopLevel(focused), dialog))
                {
                    focusEstablished = true;
                    return true;
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
