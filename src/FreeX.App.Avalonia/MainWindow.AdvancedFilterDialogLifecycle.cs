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
    {
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
                    return;

                args.Handled = true;
                Dispatcher.UIThread.Post(
                    () => InvokeAdvancedFilterCancel(dialog, cancelButton),
                    DispatcherPriority.Input);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void InvokeAdvancedFilterCancel(Window dialog, Button cancelButton)
    {
        if (!dialog.IsVisible)
            return;

        cancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancelButton));
        if (dialog.IsVisible)
            dialog.Close();
    }
}
