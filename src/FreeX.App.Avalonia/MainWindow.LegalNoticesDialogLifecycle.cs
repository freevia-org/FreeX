using Avalonia.Controls;
using Free.Shared.Shell.Avalonia;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static void ConfigureLegalNoticesDialogKeyboard(
        Window dialog,
        TabControl tabControl,
        Button closeButton)
    {
        AvaloniaLegalNoticesDialog.ConfigureKeyboardLifecycle(dialog, tabControl, closeButton);
    }
}
