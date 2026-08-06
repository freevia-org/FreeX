using System.IO;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookImportExecutionOutcome
{
    Succeeded,
    EmptyWorkbook,
    CommandFailed,
    Canceled,
    Failed
}

public sealed record WorkbookImportExecutionResult(
    WorkbookImportExecutionOutcome Outcome,
    int WorksheetCount,
    CommandOutcome? CommandOutcome = null,
    Workbook? ImportedWorkbook = null,
    Exception? Exception = null,
    string? Reason = null,
    string? UserMessage = null,
    string? ErrorDetail = null)
{
    public bool Succeeded => Outcome == WorkbookImportExecutionOutcome.Succeeded;
}

public sealed record WorkbookImportFailureDiagnostic(
    string Reason,
    string UserMessage,
    string? Detail);

public static class WorkbookImportFailurePlanner
{
    public static WorkbookImportFailureDiagnostic FromException(string extension, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsXsltTransformFailure(extension, exception))
        {
            return new WorkbookImportFailureDiagnostic(
                "xslt_transform_failed",
                $"Failed to import XML data after applying the XSLT transform:\n{exception.Message}",
                exception.Message);
        }

        return new WorkbookImportFailureDiagnostic(
            exception.GetType().Name,
            $"Failed to import data:\n{exception.Message}",
            Detail: null);
    }

    private static bool IsXsltTransformFailure(string extension, Exception exception) =>
        string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) &&
        exception is InvalidDataException &&
        ExceptionChainContainsXslt(exception);

    private static bool ExceptionChainContainsXslt(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("XSLT", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Loads and applies imported workbook data without owning renderer controls, focus, or progress UI.
/// </summary>
public static class WorkbookImportWorkflow
{
    public static async Task<WorkbookImportExecutionResult> ImportPathAsync(
        string path,
        string extension,
        IFileAdapter adapter,
        SheetId targetSheetId,
        CellAddress destination,
        Func<ImportSheetCommand, CommandOutcome> executeCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(executeCommand);

        try
        {
            var imported = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                var workbook = adapter.Load(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return workbook;
            }, cancellationToken).ConfigureAwait(true);

            return ApplyImportedWorkbook(imported, targetSheetId, destination, executeCommand);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new WorkbookImportExecutionResult(
                WorkbookImportExecutionOutcome.Canceled,
                WorksheetCount: 0,
                Exception: ex,
                Reason: "canceled",
                UserMessage: "Import canceled.");
        }
        catch (Exception ex)
        {
            var diagnostic = WorkbookImportFailurePlanner.FromException(extension, ex);
            return new WorkbookImportExecutionResult(
                WorkbookImportExecutionOutcome.Failed,
                WorksheetCount: 0,
                Exception: ex,
                Reason: diagnostic.Reason,
                UserMessage: diagnostic.UserMessage,
                ErrorDetail: diagnostic.Detail);
        }
    }

    public static WorkbookImportExecutionResult ApplyImportedWorkbook(
        Workbook imported,
        SheetId targetSheetId,
        CellAddress destination,
        Func<ImportSheetCommand, CommandOutcome> executeCommand)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(executeCommand);

        if (imported.Sheets.Count == 0)
        {
            return new WorkbookImportExecutionResult(
                WorkbookImportExecutionOutcome.EmptyWorkbook,
                WorksheetCount: 0,
                ImportedWorkbook: imported,
                Reason: "empty_workbook");
        }

        var outcome = executeCommand(new ImportSheetCommand(targetSheetId, destination, imported.Sheets[0]));
        if (!outcome.Success)
        {
            return new WorkbookImportExecutionResult(
                WorkbookImportExecutionOutcome.CommandFailed,
                imported.Sheets.Count,
                outcome,
                imported,
                Reason: "command_failed",
                UserMessage: outcome.ErrorMessage);
        }

        return new WorkbookImportExecutionResult(
            WorkbookImportExecutionOutcome.Succeeded,
            imported.Sheets.Count,
            outcome,
            imported);
    }
}
