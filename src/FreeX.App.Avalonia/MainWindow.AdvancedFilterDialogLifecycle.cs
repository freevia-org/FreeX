using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureAdvancedFilterDialogEscape(
        Window dialog,
        Button cancelButton)
        => ConfigureDialogCancelOnEscape(dialog, cancelButton);

    private static void ConfigureDialogCancelOnEscape(
        Window dialog,
        Button cancelButton)
    {
        var cancelQueued = false;

        void InvokeCancel()
        {
            cancelQueued = false;
            if (!dialog.IsVisible)
                return;

            cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelButton));
            if (dialog.IsVisible)
                dialog.Close();
        }

        void QueueCancel(KeyEventArgs args)
        {
            if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
                return;

            args.Handled = true;
            if (cancelQueued)
                return;

            cancelQueued = true;
            Dispatcher.UIThread.Post(InvokeCancel, DispatcherPriority.Input);
        }

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => QueueCancel(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        dialog.AddHandler(
            InputElement.KeyUpEvent,
            (_, args) => QueueCancel(args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }
}
