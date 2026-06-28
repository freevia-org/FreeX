using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Production WPF implementation of <see cref="IUserMessageService"/>.
/// </summary>
public sealed class WpfUserMessageService : IUserMessageService
{
    private const string DefaultErrorTitle = "Error";
    private const string DefaultWarningTitle = "Warning";
    private const string DefaultInformationTitle = "Information";
    private const string DefaultConfirmTitle = "Confirm";

    public void ShowError(string message, string title = DefaultErrorTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

    public void ShowWarning(string message, string title = DefaultWarningTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

    public void ShowInfo(string message, string title = DefaultInformationTitle)
        => ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

    public bool AskYesNo(string message, string title = DefaultConfirmTitle)
        => ShowMessage(message, title, UserMessageButtons.YesNo, UserMessageIcon.Question) == UserMessageResult.Yes;

    public UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon)
        => WpfMessageBoxRealizer.Show(Application.Current?.MainWindow, message, title, buttons, icon);
}
