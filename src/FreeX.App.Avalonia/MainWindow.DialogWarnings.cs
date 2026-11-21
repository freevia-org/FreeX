using Avalonia.Controls;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private readonly HashSet<Window> _modelessOwnedWindows = [];

    /// <summary>
    /// The top-most visible owned modal dialog (WPF <c>ShowDialog</c> equivalent), or null when no modal
    /// dialog is open. The modeless Find &amp; Replace style windows and an already-open message box are
    /// not candidates.
    /// </summary>
    internal Window? FindTopmostOpenModalDialog()
    {
        Window? top = null;
        Window current = this;
        while (true)
        {
            Window? next = null;
            foreach (var owned in current.OwnedWindows)
            {
                if (owned.IsVisible && !_modelessOwnedWindows.Contains(owned))
                    next = owned;
            }

            if (next is null)
                break;

            top = next;
            current = next;
        }

        return top is AvaloniaUserMessageDialog ? null : top;
    }

    /// <summary>
    /// WPF validation and failure messages inside a dialog are owned modal message boxes titled with the
    /// dialog; this shell's status bar sits behind the modal dialog and is invisible, so the same text is
    /// also shown in an owned warning above the dialog. The status text remains the accessible live region.
    /// </summary>
    private void ShowWarningOverOpenModalDialog(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || FindTopmostOpenModalDialog() is not { } dialog)
            return;

        Dispatcher.UIThread.Post(
            async () =>
            {
                if (!dialog.IsVisible || FindTopmostOpenModalDialog() != dialog)
                    return;

                try
                {
                    await AvaloniaUserMessageDialog.ShowWarningAsync(dialog, message, dialog.Title ?? string.Empty);
                }
                catch (InvalidOperationException)
                {
                    // The dialog closed while the warning was opening; the status bar already carries the text.
                }
            },
            DispatcherPriority.Background);
    }
}
