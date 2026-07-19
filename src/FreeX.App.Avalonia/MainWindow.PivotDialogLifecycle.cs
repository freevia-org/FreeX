using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        void FocusInitialControl()
        {
            if (!dialog.IsVisible ||
                !initialFocus.IsVisible ||
                !initialFocus.IsEffectivelyEnabled)
            {
                return;
            }

            initialFocus.Focus();
            if (selectAllText && initialFocus is TextBox textBox)
                textBox.SelectAll();
        }

        dialog.Opened += (_, _) =>
        {
            FocusInitialControl();
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
        };
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
                {
                    var cancel = dialog.GetVisualDescendants()
                        .OfType<Button>()
                        .FirstOrDefault(button =>
                            button.IsCancel && button.IsVisible && button.IsEffectivelyEnabled);
                    cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancel));
                    if (dialog.IsVisible)
                        dialog.Close();
                    args.Handled = true;
                    return;
                }

                if (args.Key != Key.Tab ||
                    (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
                {
                    return;
                }

                var stops = dialog.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(control =>
                        control.Focusable && control.IsVisible && control.IsEffectivelyEnabled)
                    .ToList();
                if (stops.Count == 0)
                    return;

                var focused = dialog.FocusManager?.GetFocusedElement();
                var currentIndex = stops.FindIndex(control => ReferenceEquals(control, focused));
                if (currentIndex < 0)
                {
                    FocusInitialControl();
                    args.Handled = true;
                    return;
                }

                var delta = args.KeyModifiers == KeyModifiers.Shift ? -1 : 1;
                var nextIndex = (currentIndex + delta + stops.Count) % stops.Count;
                stops[nextIndex].Focus();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }
}
