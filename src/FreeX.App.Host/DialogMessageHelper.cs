using System.Windows;

namespace FreeX.App.Host;

/// <summary>
/// Thin wrapper around <see cref="MessageBox.Show"/> for use inside dialog windows.
/// Provides the same surface as <see cref="FreeX.App.UI.IUserMessageService"/> but takes
/// the dialog's own window as owner so messages appear centred on it.
/// This is the only legitimate call site for <see cref="MessageBox.Show"/> in dialog classes.
/// </summary>
internal static class DialogMessageHelper
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    public static void ShowError(Window owner, string? message, string title = DefaultErrorTitle) =>
        MessageBox.Show(
            owner,
            message ?? string.Empty,
            ResolveDefaultTitle(title, DefaultErrorTitle, UiText.ErrorTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

    public static void ShowWarning(Window owner, string? message, string title = DefaultWarningTitle) =>
        MessageBox.Show(
            owner,
            message ?? string.Empty,
            ResolveDefaultTitle(title, DefaultWarningTitle, UiText.WarningTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    public static void ShowInfo(Window owner, string? message, string title = DefaultInformationTitle) =>
        MessageBox.Show(
            owner,
            message ?? string.Empty,
            ResolveDefaultTitle(title, DefaultInformationTitle, UiText.InformationTitle),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    public static bool AskYesNo(Window owner, string? message, string title = DefaultConfirmTitle) =>
        MessageBox.Show(
            owner,
            message ?? string.Empty,
            ResolveDefaultTitle(title, DefaultConfirmTitle, UiText.ConfirmTitle),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    private static string ResolveDefaultTitle(string title, string defaultTitle, string localizedTitle) =>
        string.Equals(title, defaultTitle, StringComparison.Ordinal)
            ? localizedTitle
            : title;
}
