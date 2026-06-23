using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Shared message helper for use inside dialog windows.
/// Provides the same surface as <see cref="Free.Shared.AppServices.IUserMessageService"/> but takes
/// the dialog's own window as owner, when available, so messages appear centred on it.
/// </summary>
public static class DialogMessageHelper
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    public static void ShowError(Window? owner, string? message, string title = DefaultErrorTitle) =>
        ShowMessage(owner, message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

    public static void ShowWarning(Window? owner, string? message, string title = DefaultWarningTitle) =>
        ShowMessage(owner, message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

    public static void ShowInfo(Window? owner, string? message, string title = DefaultInformationTitle) =>
        ShowMessage(owner, message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

    public static bool AskYesNo(Window? owner, string? message, string title = DefaultConfirmTitle) =>
        ShowMessage(owner, message, title, UserMessageButtons.YesNo, UserMessageIcon.Question)
            == UserMessageResult.Yes;

    public static UserMessageResult ShowMessage(
        Window? owner,
        string? message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon) =>
        WpfMessageBoxRealizer.Show(owner, message, title, buttons, icon);
}
