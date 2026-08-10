namespace Free.Shared.AppServices;

/// <summary>
/// Abstracts modal message dialogs so that callers remain testable
/// without triggering real WPF MessageBox windows.
/// </summary>
public interface IUserMessageService
{
    void ShowError(string message, string title = "Error");
    void ShowWarning(string message, string title = "Warning");
    void ShowInfo(string message, string title = "Information");
    bool AskYesNo(string message, string title = "Confirm");
    UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon);
}

public static class UserMessageServiceFileCommandExtensions
{
    public static SaveChangesPrompt PromptSaveChanges(
        this IUserMessageService messageService,
        string displayName,
        string action,
        string appTitle)
    {
        ArgumentNullException.ThrowIfNull(messageService);

        var result = messageService.ShowMessage(
            $"Do you want to save changes to {displayName} before {action}?",
            appTitle,
            UserMessageButtons.YesNoCancel,
            UserMessageIcon.Warning);

        return result switch
        {
            UserMessageResult.Yes => SaveChangesPrompt.Save,
            UserMessageResult.No => SaveChangesPrompt.DontSave,
            _ => SaveChangesPrompt.Cancel,
        };
    }

    public static void ShowFileCommandError(
        this IUserMessageService messageService,
        string summary,
        Exception exception,
        string appTitle)
    {
        ArgumentNullException.ThrowIfNull(messageService);
        ArgumentNullException.ThrowIfNull(exception);

        messageService.ShowMessage(
            $"{summary}:\n{exception.Message}",
            appTitle,
            UserMessageButtons.Ok,
            UserMessageIcon.Error);
    }

    /// <summary>
    /// Surfaces non-fatal image-decode losses collected during an export (e.g. an embedded picture
    /// that could not be decoded) instead of letting the export look silently clean. A no-op when
    /// <paramref name="imageDiagnostics"/> is empty. Shared so every sister app that plumbs an
    /// <c>imageDiagnostics</c> sink through a PDF/export writer reports it identically, rather than
    /// each app growing its own message-box formatting.
    /// </summary>
    public static void ShowExportImageWarnings(
        this IUserMessageService messageService,
        string exportedSummary,
        IReadOnlyCollection<string> imageDiagnostics,
        string appTitle)
    {
        ArgumentNullException.ThrowIfNull(messageService);
        ArgumentNullException.ThrowIfNull(imageDiagnostics);

        if (imageDiagnostics.Count == 0)
            return;

        messageService.ShowMessage(
            $"{exportedSummary}, but {imageDiagnostics.Count} image warning(s) occurred:\n" +
            string.Join("\n", imageDiagnostics),
            appTitle,
            UserMessageButtons.Ok,
            UserMessageIcon.Warning);
    }
}
