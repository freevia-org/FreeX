using System.Collections.Generic;
using Free.Shared.AppServices;

internal sealed class RecordingUserMessageService : IUserMessageService
{
    public UserMessageResult NextResult { get; set; } = UserMessageResult.Ok;

    public List<MessageCall> Messages { get; } = new();

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
        Messages.Add(new MessageCall(message, title, buttons, icon));
        return NextResult;
    }

    public sealed class MessageCall
    {
        public MessageCall(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            Message = message;
            Title = title;
            Buttons = buttons;
            Icon = icon;
        }

        public string Message { get; }

        public string Title { get; }

        public UserMessageButtons Buttons { get; }

        public UserMessageIcon Icon { get; }
    }
}
