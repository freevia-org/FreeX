using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureLegalNoticesDialogKeyboard(
        Window dialog,
        TabControl tabControl,
        Button closeButton)
    {
        KeyboardNavigation.SetIsTabStop(closeButton, true);
        closeButton.Focusable = true;

        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Enter)
                {
                    args.Handled = true;
                    closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, closeButton));
                    if (dialog.IsVisible)
                        dialog.Close();
                    return;
                }

                if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Escape)
                {
                    args.Handled = true;
                    dialog.Close();
                    return;
                }

                if (args.Key != Key.Tab ||
                    (args.KeyModifiers != KeyModifiers.None && args.KeyModifiers != KeyModifiers.Shift))
                {
                    return;
                }

                var tabStops = GetLegalNoticesTabStops(tabControl, closeButton);
                if (tabStops.Count == 0)
                    return;

                var focused = dialog.FocusManager?.GetFocusedElement() as Control;
                var currentIndex = focused is null ? -1 : tabStops.IndexOf(focused);
                var nextIndex = args.KeyModifiers == KeyModifiers.Shift
                    ? currentIndex <= 0 ? tabStops.Count - 1 : currentIndex - 1
                    : currentIndex < 0 || currentIndex == tabStops.Count - 1 ? 0 : currentIndex + 1;

                tabStops[nextIndex].Focus();
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static List<Control> GetLegalNoticesTabStops(TabControl tabControl, Button closeButton)
    {
        var tabStops = new List<Control>();
        if (tabControl.SelectedItem is TabItem
            {
                Content: ScrollViewer { Content: TextBox textBox },
            } && textBox.IsVisible && textBox.IsEffectivelyEnabled)
        {
            textBox.Focusable = true;
            KeyboardNavigation.SetIsTabStop(textBox, true);
            tabStops.Add(textBox);
        }

        if (closeButton.IsVisible && closeButton.IsEffectivelyEnabled)
            tabStops.Add(closeButton);

        return tabStops;
    }
}
