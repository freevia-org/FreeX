namespace Free.Shared.AppServices;

/// <summary>
/// Abstracts modal message dialogs so that callers remain testable
/// without triggering real WPF MessageBox windows.
/// </summary>
public interface IUserMessageService
{
    void ShowError(string message, string title = "Error") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

    void ShowWarning(string message, string title = "Warning") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

    void ShowInfo(string message, string title = "Information") =>
        ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

    bool AskYesNo(string message, string title = "Confirm") =>
        ShowMessage(message, title, UserMessageButtons.YesNo, UserMessageIcon.Question)
            == UserMessageResult.Yes;

    UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon) =>
        throw new NotSupportedException(
            "This user-message service is asynchronous. Call ShowMessageAsync instead.");

    /// <summary>
    /// Shows an owned message without imposing a synchronous modal API on asynchronous
    /// renderers. Existing synchronous implementations remain valid through this default bridge.
    /// </summary>
    ValueTask<UserMessageResult> ShowMessageAsync(
        UserMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ShowMessage(
            request.Message,
            request.Title,
            request.Buttons,
            request.Kind));
    }
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

        var message = BuildExportImageWarningMessage(exportedSummary, imageDiagnostics);
        if (message is null)
            return;

        messageService.ShowMessage(
            message,
            appTitle,
            UserMessageButtons.Ok,
            UserMessageIcon.Warning);
    }

    public static string? BuildExportImageWarningMessage(
        string exportedSummary,
        IReadOnlyCollection<string> imageDiagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportedSummary);
        ArgumentNullException.ThrowIfNull(imageDiagnostics);

        return imageDiagnostics.Count == 0
            ? null
            : $"{exportedSummary}, but {imageDiagnostics.Count} image warning(s) occurred:\n" +
                string.Join("\n", imageDiagnostics);
    }
}
