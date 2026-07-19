using Avalonia;
using Avalonia.Controls;
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

        void FocusInitialControl()
        {
            if (!dialog.IsVisible ||
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

            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        }

        layoutUpdated = (_, _) => FocusInitialControl();
        dialog.Opened += (_, _) =>
        {
            FocusInitialControl();
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
            Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Background);
        };
        dialog.Activated += (_, _) => Dispatcher.UIThread.Post(FocusInitialControl, DispatcherPriority.Input);
        dialog.LayoutUpdated += layoutUpdated;
        dialog.Closed += (_, _) =>
        {
            if (layoutUpdated is not null)
                dialog.LayoutUpdated -= layoutUpdated;
        };
    }
}
