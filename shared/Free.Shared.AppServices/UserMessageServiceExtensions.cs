namespace Free.Shared.AppServices;

/// <summary>Renderer-neutral convenience operations for asynchronous owned user messages.</summary>
public static class UserMessageServiceExtensions
{
    public static ValueTask<UserMessageResult> ShowWarningAsync(
        this IUserMessageService messageService,
        string message,
        string title = "Warning",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageService);

        return messageService.ShowMessageAsync(
            new UserMessageRequest(
                message,
                title,
                UserMessageButtons.Ok,
                UserMessageIcon.Warning),
            cancellationToken);
    }
}
