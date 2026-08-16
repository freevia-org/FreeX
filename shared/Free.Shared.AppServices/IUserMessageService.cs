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

    /// <summary>
    /// Asks whether to overwrite a save target that was changed by another program (another
    /// instance of this app, a sync client, a colleague on a shared path) since the caller last
    /// observed it. Shared so every sister app's external-modification guard (FreeX's
    /// WorkbookExternallyModifiedException, FreeW's DocumentExternallyModifiedException, FreeP's
    /// presentation equivalent) prompts with identical wording instead of each growing its own.
    /// Works for both synchronous and asynchronous <see cref="IUserMessageService"/>
    /// implementations via <see cref="IUserMessageService.ShowMessageAsync"/>.
    /// </summary>
    public static async ValueTask<bool> AskExternallyModifiedOverwriteAsync(
        this IUserMessageService messageService,
        string path,
        string appTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageService);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var result = await messageService.ShowMessageAsync(
            new UserMessageRequest(
                $"'{Path.GetFileName(path)}' has been changed by another program since it was opened. " +
                "Do you want to overwrite those changes with your version?",
                appTitle,
                UserMessageButtons.YesNo,
                UserMessageIcon.Warning),
            cancellationToken);
        return result == UserMessageResult.Yes;
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
