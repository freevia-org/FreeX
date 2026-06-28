using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Back-compat facade for small-document file command prompts. The lifecycle decisions live in
/// <see cref="FileCommandWorkflow"/> and the prompt policy lives on <see cref="IUserMessageService"/>
/// extensions; callers should inject an <see cref="IUserMessageService"/> directly.
/// </summary>
public static class FileCommandMessageBox
{
    public static SaveChangesPrompt PromptSaveChanges(
        IUserMessageService messageService,
        string displayName,
        string action,
        string appTitle)
    {
        return messageService.PromptSaveChanges(displayName, action, appTitle);
    }

    public static void ShowError(
        IUserMessageService messageService,
        string summary,
        Exception exception,
        string appTitle)
    {
        messageService.ShowFileCommandError(summary, exception, appTitle);
    }
}
