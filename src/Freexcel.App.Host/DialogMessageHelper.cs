using System.Windows;

namespace Freexcel.App.Host;

/// <summary>
/// Thin wrapper around <see cref="MessageBox.Show"/> for use inside dialog windows.
/// Provides the same surface as <see cref="Freexcel.App.UI.IUserMessageService"/> but takes
/// the dialog's own window as owner so messages appear centred on it.
/// This is the only legitimate call site for <see cref="MessageBox.Show"/> in dialog classes.
/// </summary>
internal static class DialogMessageHelper
{
    public static void ShowError(Window owner, string? message, string title = "Error") =>
        MessageBox.Show(owner, message ?? string.Empty, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void ShowWarning(Window owner, string? message, string title = "Warning") =>
        MessageBox.Show(owner, message ?? string.Empty, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void ShowInfo(Window owner, string? message, string title = "Information") =>
        MessageBox.Show(owner, message ?? string.Empty, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool AskYesNo(Window owner, string? message, string title = "Confirm") =>
        MessageBox.Show(owner, message ?? string.Empty, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
}
