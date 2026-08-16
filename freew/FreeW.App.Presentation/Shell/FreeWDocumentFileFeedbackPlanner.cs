using Free.Shared.AppServices;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWDocumentFileFeedback(
    bool Succeeded,
    string Message,
    string? ErrorSummary = null,
    Exception? Exception = null,
    bool RequiresSaveAs = false)
{
    public bool ShouldShowError => ErrorSummary is not null && Exception is not null;
}

public static class FreeWDocumentFileFeedbackPlanner
{
    public const string ImportPdfAction = "importing a PDF";
    public const string ImportPdfPickerTitle = "Import PDF (text only)";
    public const string SaveCopyCommand = "Save a Copy";

    private static SisterAppFileTextSpec Text => FreeWFileTextResources.Document;

    public static FreeWDocumentFileFeedback PlanOpen(
        DocumentOpenWorkflowResult execution,
        string path)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (execution.Succeeded)
        {
            return Success(SisterAppFileTextPlanner.FormatOpened(
                Text,
                Path.GetFileName(execution.OpenResult?.SavedPath ?? path)));
        }

        if (execution.Outcome == DocumentFileExecutionOutcome.UnsupportedFormat)
        {
            return Failure(
                SisterAppFileTextPlanner.FormatUnsupportedFileType(
                    Text,
                    Text.OpenCommand,
                    Path.GetExtension(path)),
                "Unrecognized file type");
        }

        return FailedCommand(
            Text.OpenCommand,
            "Could not open the document",
            execution.Exception,
            "The open operation was canceled.");
    }

    public static FreeWDocumentFileFeedback PlanImport(
        DocumentImportWorkflowResult execution,
        string path)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (execution.Succeeded)
            return Success($"Imported PDF text from {Path.GetFileName(path)}");

        return FailedCommand(
            "PDF import",
            execution.Exception is InvalidOperationException
                ? "Unrecognized PDF import file"
                : "Could not import PDF text",
            execution.Exception,
            "The PDF import was canceled.");
    }

    public static FreeWDocumentFileFeedback PlanSnapshot(
        DocumentSnapshotWorkflowResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return execution.Succeeded
            ? Success("Recovered the unsaved document.")
            : FailedCommand(
                "Recovery",
                "Could not recover the document",
                execution.Exception,
                "The recovery operation was canceled.");
    }

    public static FreeWDocumentFileFeedback PlanSave(
        DocumentSaveWorkflowResult execution,
        DocumentSaveExecutionKind kind,
        string attemptedPath)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptedPath);
        var isCopy = kind == DocumentSaveExecutionKind.SaveCopy;
        var command = isCopy ? SaveCopyCommand : Text.SaveCommand;

        if (execution.Succeeded)
        {
            var saved = SisterAppFileTextPlanner.FormatSaved(
                Text,
                Path.GetFileName(execution.Target?.Path ?? attemptedPath));
            return Success(isCopy ? saved + " (copy)" : saved);
        }

        if (execution.RequiresSaveAs)
            return new(false, string.Empty, RequiresSaveAs: true);

        if (execution.Outcome == DocumentFileExecutionOutcome.CompatibilityDeclined)
            return new(false, isCopy ? "Save a Copy canceled." : "Save canceled.");

        if (execution.Outcome == DocumentFileExecutionOutcome.ExternalWriteConflict)
        {
            return new(
                false,
                isCopy
                    ? "Save a Copy canceled -- the file was changed by another program."
                    : "Save canceled -- the file was changed by another program.");
        }

        if (execution.Outcome == DocumentFileExecutionOutcome.UnsupportedFormat)
        {
            return Failure(
                SisterAppFileTextPlanner.FormatUnsupportedFileType(
                    Text,
                    command,
                    Path.GetExtension(attemptedPath)),
                isCopy ? "Could not save a copy" : "Could not save the document");
        }

        return FailedCommand(
            command,
            isCopy ? "Could not save a copy" : "Could not save the document",
            execution.Exception,
            "The save operation was canceled.");
    }

    private static FreeWDocumentFileFeedback Success(string message) => new(true, message);

    private static FreeWDocumentFileFeedback Failure(string message, string summary) =>
        new(false, message, summary, new InvalidOperationException(message));

    private static FreeWDocumentFileFeedback FailedCommand(
        string command,
        string summary,
        Exception? exception,
        string fallbackMessage)
    {
        var effectiveException = exception ?? new InvalidOperationException(fallbackMessage);
        return new(
            false,
            SisterAppFileTextPlanner.FormatCommandFailed(Text, command, effectiveException.Message),
            summary,
            effectiveException);
    }
}
