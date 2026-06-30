using Free.Shared.AppServices;

namespace FreeP.App.Host.Tests;

internal sealed class TestUserMessageService : IUserMessageService
{
    public static TestUserMessageService DiscardUnsavedChanges { get; } =
        new(UserMessageResult.No);

    private readonly UserMessageResult _confirmationResult;

    public TestUserMessageService(UserMessageResult confirmationResult)
    {
        _confirmationResult = confirmationResult;
    }

    public void ShowError(string message, string title = "Error") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

    public void ShowWarning(string message, string title = "Warning") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

    public void ShowInfo(string message, string title = "Information") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

    public bool AskYesNo(string message, string title = "Confirm") =>
        ShowMessage(message, title, UserMessageButtons.YesNo, UserMessageIcon.Question) == UserMessageResult.Yes;

    public UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon)
    {
        return buttons switch
        {
            UserMessageButtons.YesNoCancel => _confirmationResult,
            UserMessageButtons.YesNo => _confirmationResult,
            UserMessageButtons.OkCancel => UserMessageResult.Ok,
            _ => UserMessageResult.Ok,
        };
    }
}
