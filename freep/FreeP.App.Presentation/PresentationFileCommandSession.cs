using System.IO;
using System.Threading;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Pdf;
using Free.Shared.Shell;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationFileCommand
{
    New,
    Open,
    Save,
    SaveAs,
    ExportPdf,
    ExportNotesPagePdf,
    ExportImages,
    Print,
    ExportVideo,
}

public enum PresentationFileCommandStatus
{
    Succeeded,
    Cancelled,
    Unavailable,
    Invalid,
    Failed,
}

public sealed record PresentationFileCommandValidation(bool IsValid, string? FailureReason)
{
    public static PresentationFileCommandValidation Valid { get; } = new(true, null);

    public static PresentationFileCommandValidation Invalid(string failureReason) =>
        new(false, failureReason);

    internal static PresentationFileCommandValidation FromOperation(
        OperationValidation<string>? validation) =>
        validation is null ? Valid : Invalid(validation.Detail);
}

public sealed record PresentationFileCommandError(string Summary, Exception Exception)
{
    internal static PresentationFileCommandError? FromOperation(OperationError<string>? error) =>
        error is null ? null : new(error.Detail, error.Exception);
}

public sealed record PresentationFileCommandResult
{
    private PresentationFileCommandResult(
        PresentationFileCommand command,
        OperationOutcome<string, string, string> operation,
        IReadOnlyList<string>? imageDiagnostics = null)
    {
        Command = command;
        Operation = operation;
        ImageDiagnostics = imageDiagnostics?.ToArray() ?? [];
    }

    public PresentationFileCommandResult(
        PresentationFileCommand Command,
        PresentationFileCommandStatus Status,
        PresentationFileCommandValidation Validation,
        string? Path = null,
        string? Message = null,
        PresentationFileCommandError? Error = null,
        IReadOnlyList<string>? ImageDiagnostics = null)
        : this(
            Command,
            PresentationFileOperationOutcomeMapper.MapCommand(Status, Validation, Path, Message, Error),
            ImageDiagnostics)
    {
    }

    public PresentationFileCommand Command { get; }
    public OperationOutcome<string, string, string> Operation { get; }
    public PresentationFileCommandStatus Status => PresentationFileOperationOutcomeMapper.MapCommand(Operation);
    public PresentationFileCommandValidation Validation =>
        PresentationFileCommandValidation.FromOperation(Operation.Validation);
    public string? Path => Operation.Path;
    public string? Message => Operation.Value;
    public PresentationFileCommandError? Error => PresentationFileCommandError.FromOperation(Operation.Error);
    public IReadOnlyList<string> ImageDiagnostics { get; }
    public bool Succeeded => Operation.Succeeded;
    public bool Cancelled => Operation.Cancelled;

    public void Deconstruct(
        out PresentationFileCommand command,
        out PresentationFileCommandStatus status,
        out PresentationFileCommandValidation validation,
        out string? path,
        out string? message,
        out PresentationFileCommandError? error)
    {
        command = Command;
        status = Status;
        validation = Validation;
        path = Path;
        message = Message;
        error = Error;
    }

    public static PresentationFileCommandResult Success(
        PresentationFileCommand command,
        string? path = null,
        string? message = null,
        IReadOnlyList<string>? imageDiagnostics = null) =>
        new(command, OperationOutcome<string, string, string>.Completed(message, path), imageDiagnostics);

    public static PresentationFileCommandResult Cancel(
        PresentationFileCommand command,
        string? message = null) =>
        new(command, OperationOutcome<string, string, string>.Cancel(message));

    public static PresentationFileCommandResult Unavailable(
        PresentationFileCommand command,
        string message) =>
        new(command, OperationOutcome<string, string, string>.Unavailable(message));

    public static PresentationFileCommandResult Invalid(
        PresentationFileCommand command,
        string summary,
        string failureReason,
        string? path = null)
    {
        var exception = new InvalidDataException(failureReason);
        return new PresentationFileCommandResult(
            command,
            OperationOutcome<string, string, string>.ValidationFailure(
                failureReason,
                summary,
                exception,
                failureReason,
                path));
    }

    public static PresentationFileCommandResult Failure(
        PresentationFileCommand command,
        string summary,
        Exception exception,
        string? path = null,
        string? message = null) =>
        new(
            command,
            OperationOutcome<string, string, string>.Failure(
                summary,
                exception,
                exception.Message,
                message ?? exception.Message,
                path));
}

public sealed record PresentationFilePickerResult
{
    private PresentationFilePickerResult(PickerOutcome<string> outcome)
    {
        Outcome = outcome;
    }

    public PickerOutcome<string> Outcome { get; }
    public OperationOutcome<string, string, string> Operation => Outcome.Operation;
    public OperationStatus Status => Outcome.Status;
    public string? Path => Outcome.Selection;
    public string? Message => Outcome.Message;

    public void Deconstruct(
        out OperationStatus status,
        out string? path,
        out string? message)
    {
        status = Status;
        path = Path;
        message = Message;
    }

    public static PresentationFilePickerResult Selected(string path) =>
        new(PickerOutcome<string>.Selected(path));

    public static PresentationFilePickerResult Cancelled { get; } =
        new(PickerOutcome<string>.Cancelled);

    public static PresentationFilePickerResult Unavailable(string message) =>
        new(PickerOutcome<string>.Unavailable(message));

    public static PresentationFilePickerResult NonLocal(string message) =>
        new(PickerOutcome<string>.Invalid(message));
}

internal static class PresentationFileOperationOutcomeMapper
{
    internal static OperationOutcome<string, string, string> MapCommand(
        PresentationFileCommandStatus status,
        PresentationFileCommandValidation validation,
        string? path,
        string? message,
        PresentationFileCommandError? error)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return status switch
        {
            PresentationFileCommandStatus.Succeeded =>
                OperationOutcome<string, string, string>.Completed(message, path),
            PresentationFileCommandStatus.Cancelled =>
                OperationOutcome<string, string, string>.Cancel(message, path),
            PresentationFileCommandStatus.Unavailable =>
                OperationOutcome<string, string, string>.Unavailable(message, path),
            PresentationFileCommandStatus.Invalid => MapValidationFailure(validation, path, message, error),
            PresentationFileCommandStatus.Failed => MapFailure(validation, path, message, error),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported file command status."),
        };
    }

    internal static PresentationFileCommandStatus MapCommand(
        OperationOutcome<string, string, string> operation) => operation.Status switch
        {
            OperationStatus.Completed => PresentationFileCommandStatus.Succeeded,
            OperationStatus.Cancelled or OperationStatus.Declined => PresentationFileCommandStatus.Cancelled,
            OperationStatus.Unavailable => PresentationFileCommandStatus.Unavailable,
            OperationStatus.ValidationFailed => PresentationFileCommandStatus.Invalid,
            OperationStatus.Failed => PresentationFileCommandStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Status,
                "Unsupported shared file command outcome."),
        };

    private static OperationOutcome<string, string, string> MapValidationFailure(
        PresentationFileCommandValidation validation,
        string? path,
        string? message,
        PresentationFileCommandError? error)
    {
        var detail = validation.FailureReason ?? message ?? error?.Exception.Message ?? "The operation is invalid.";
        return error is null
            ? OperationOutcome<string, string, string>.ValidationFailure(detail, message, path)
            : OperationOutcome<string, string, string>.ValidationFailure(
                detail,
                error.Summary,
                error.Exception,
                message,
                path);
    }

    private static OperationOutcome<string, string, string> MapFailure(
        PresentationFileCommandValidation validation,
        string? path,
        string? message,
        PresentationFileCommandError? error)
    {
        var exception = error?.Exception ?? new InvalidOperationException(message ?? "The operation failed.");
        var summary = error?.Summary ?? "The operation failed.";
        return validation.IsValid
            ? OperationOutcome<string, string, string>.Failure(summary, exception, message, path)
            : OperationOutcome<string, string, string>.Failure(
                summary,
                exception,
                validation.FailureReason ?? exception.Message,
                message,
                path);
    }
}

public sealed record PresentationFileOpenPickerRequest(
    FileOpenDialogPlan DialogPlan,
    FileOpenPickerPlan PickerPlan,
    string Title);

public sealed record PresentationFileSavePickerRequest(
    PresentationFileCommand Command,
    FileSaveDialogPlan DialogPlan,
    FileSavePickerPlan PickerPlan,
    string Title,
    bool ShowOverwritePrompt = true);

public sealed record PresentationFolderPickerRequest(
    PresentationFileCommand Command,
    string Title,
    string? InitialDirectory = null);

public interface IPresentationFileLifecyclePort
{
    bool IsDirty { get; }
    int DirtyGeneration { get; }
    string? CurrentPath { get; }
    string? CurrentFileName { get; }
    string DisplayName { get; }
    IReadOnlyList<RecentFileEntry> RecentEntries { get; }

    void MarkDirty();

    /// <summary>
    /// Marks the presentation dirty while retargeting it at <paramref name="path"/>. Used by
    /// autosave recovery: a recovered snapshot is unsaved work that belongs to the original file,
    /// so it must reopen as dirty-with-that-path rather than as a clean document at the snapshot's
    /// own throwaway location. Mirrors the shared <c>FileCommandWorkflow.MarkDirtyWithPath</c>.
    /// </summary>
    void MarkDirtyWithPath(string? path);

    void MarkSavedWithoutPath();
    void MarkSavedWithPath(string path, bool suppressRecentFiles);

    Task<bool> NewAsync(string action, Func<Task> loadNewPresentationAsync);
    Task<bool> OpenAsync(
        string action,
        Func<Task<string?>> pickPathAsync,
        Func<string, Task<bool>> openPathAsync);
    Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync);
    Task<bool> ConfirmCloseAllowedAsync(string action);
}

public interface IPresentationFilePickerPort
{
    Task<PresentationFilePickerResult> PickOpenFileAsync(
        PresentationFileOpenPickerRequest request,
        CancellationToken cancellationToken);

    Task<PresentationFilePickerResult> PickSaveFileAsync(
        PresentationFileSavePickerRequest request,
        CancellationToken cancellationToken);

    Task<PresentationFilePickerResult> PickFolderAsync(
        PresentationFolderPickerRequest request,
        CancellationToken cancellationToken);
}

public interface IPresentationFileRenderPort
{
    PresentationSlideImageRenderer RenderSlideToPng { get; }
    PresentationSlideImageRendererWithPrintMarkup? RenderSlideToPngWithPrintMarkup { get; }
    PresentationRasterPdfWriter WriteRasterPdf { get; }
    PresentationPdfContentWriter WriteVectorPdf { get; }

    byte[] WriteRasterPdfWithDiagnostics(
        PdfRasterDocument document,
        ICollection<string> imageDiagnostics) =>
        WriteRasterPdf(document);

    byte[] WriteVectorPdfWithDiagnostics(
        PdfContentDocument document,
        ICollection<string> imageDiagnostics) =>
        WriteVectorPdf(document);
}

public sealed record PresentationNativeCommandResult(
    bool Succeeded,
    bool Cancelled,
    string StatusText,
    string? FailureReason = null)
{
    public static PresentationNativeCommandResult Success(string statusText) =>
        new(true, false, statusText);

    public static PresentationNativeCommandResult Cancel(string statusText) =>
        new(false, true, statusText);

    public static PresentationNativeCommandResult Failure(string statusText, string failureReason) =>
        new(false, false, statusText, failureReason);
}

public interface IPresentationPrintPort
{
    PresentationNativePrintHandoffHostCapabilities Capabilities { get; }

    Task<PresentationNativePrintPortResult> PrintAsync(
        Presentation presentation,
        PresentationPrintRequest request,
        Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
        CancellationToken cancellationToken);
}

public interface IPresentationVideoPort
{
    PresentationVideoExportHandoffHostCapabilities Capabilities { get; }

    Task<PresentationNativeCommandResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
        CancellationToken cancellationToken);
}

public interface IPresentationFileCommandFeedbackPort
{
    Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Renderer-neutral dependencies used to compose the presentation file-command session.
/// Native hosts supply ports and callbacks; construction policy remains shared.
/// </summary>
public sealed record PresentationFileCommandSessionComposition(
    Func<Presentation> GetPresentation,
    Action<Presentation> LoadPresentation,
    IPresentationFileLifecyclePort Lifecycle,
    IPresentationFilePickerPort Picker,
    IPresentationFileRenderPort Render,
    IPresentationPrintPort Print,
    IPresentationVideoPort Video,
    IPresentationFileCommandFeedbackPort? Feedback = null,
    Func<PresentationSlideRangeRequest?>? GetImageExportRange = null,
    Func<int?>? GetPrintCurrentSlideNumber = null,
    Func<IReadOnlyList<int>?>? GetPrintSelectedSlideNumbers = null,
    Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? PrintPackageFactory = null,
    Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
        VideoPackageArtifactFactory = null,
    Func<string, CancellationToken, Task<bool>>? ConfirmExternallyModifiedOverwriteAsync = null);

public static class PresentationFileCommandSessionFactory
{
    public static PresentationFileCommandSession Create(
        PresentationFileCommandSessionComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        return new PresentationFileCommandSession(
            composition.GetPresentation,
            composition.LoadPresentation,
            composition.Lifecycle,
            composition.Picker,
            composition.Render,
            composition.Print,
            composition.Video,
            composition.Feedback,
            composition.GetImageExportRange,
            composition.GetPrintCurrentSlideNumber,
            composition.GetPrintSelectedSlideNumbers,
            composition.PrintPackageFactory,
            composition.VideoPackageArtifactFactory,
            composition.ConfirmExternallyModifiedOverwriteAsync);
    }
}

public sealed class PresentationFileCommandSession
{
    public const string CloseAction = "closing";

    private readonly Func<Presentation> _getPresentation;
    private readonly Action<Presentation> _loadPresentation;
    private readonly IPresentationFileLifecyclePort _lifecycle;
    private readonly IPresentationFilePickerPort _picker;
    private readonly IPresentationFileRenderPort _render;
    private readonly IPresentationPrintPort _print;
    private readonly IPresentationVideoPort _video;
    private readonly AtomicExportExecutor _atomicExportExecutor = new();
    private readonly IPresentationFileCommandFeedbackPort? _feedback;
    private readonly Func<PresentationSlideRangeRequest?> _getImageExportRange;
    private readonly Func<int?> _getPrintCurrentSlideNumber;
    private readonly Func<IReadOnlyList<int>?> _getPrintSelectedSlideNumbers;
    private readonly Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? _printPackageFactory;
    private readonly Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
        _videoPackageArtifactFactory;
    private readonly Func<string, CancellationToken, Task<bool>>? _confirmExternallyModifiedOverwriteAsync;

    // r137-remediation2: the write time observed on the CURRENT presentation's source file, captured
    // at open (PresentationFileOpenResult.SourceLastWriteTimeUtc) and rebased after each successful
    // save to the path it just wrote. SavePathCoreAsync only forwards this as the external-
    // modification guard's expected time when the save target is the SAME path this field tracks
    // (PlatformPathIdentityComparer) -- Save-As to a different path establishes a new identity with
    // nothing to compare, so the guard is naturally skipped there. Never explicitly reset on File>New:
    // New clears _lifecycle.CurrentPath to null, and the path-identity gate is already false against
    // a null CurrentPath, so a stale value here is inert.
    private DateTime? _currentFileSourceLastWriteTimeUtc;

    public PresentationFileCommandSession(
        Func<Presentation> getPresentation,
        Action<Presentation> loadPresentation,
        IPresentationFileLifecyclePort lifecycle,
        IPresentationFilePickerPort picker,
        IPresentationFileRenderPort render,
        IPresentationPrintPort print,
        IPresentationVideoPort video,
        IPresentationFileCommandFeedbackPort? feedback = null,
        Func<PresentationSlideRangeRequest?>? getImageExportRange = null,
        Func<int?>? getPrintCurrentSlideNumber = null,
        Func<IReadOnlyList<int>?>? getPrintSelectedSlideNumbers = null,
        Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? printPackageFactory = null,
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
            videoPackageArtifactFactory = null,
        // r137-remediation2: asks whether to overwrite a save target that another program changed
        // since it was opened/last saved. Null (the default) means the host wired nothing, which
        // SavePathCoreAsync treats as "always decline" -- never silently overwrite.
        Func<string, CancellationToken, Task<bool>>? confirmExternallyModifiedOverwriteAsync = null)
    {
        ArgumentNullException.ThrowIfNull(getPresentation);
        ArgumentNullException.ThrowIfNull(loadPresentation);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(print);
        ArgumentNullException.ThrowIfNull(video);

        _getPresentation = getPresentation;
        _loadPresentation = loadPresentation;
        _lifecycle = lifecycle;
        _picker = picker;
        _render = render;
        _print = print;
        _video = video;
        _feedback = feedback;
        _getImageExportRange = getImageExportRange ?? (() => null);
        _getPrintCurrentSlideNumber = getPrintCurrentSlideNumber ?? (() => null);
        _getPrintSelectedSlideNumbers = getPrintSelectedSlideNumbers ?? (() => null);
        _printPackageFactory = printPackageFactory;
        _videoPackageArtifactFactory = videoPackageArtifactFactory;
        _confirmExternallyModifiedOverwriteAsync = confirmExternallyModifiedOverwriteAsync;
    }

    public bool IsDirty => _lifecycle.IsDirty;
    public int DirtyGeneration => _lifecycle.DirtyGeneration;
    public string? CurrentPath => _lifecycle.CurrentPath;
    public string? CurrentFileName => _lifecycle.CurrentFileName;
    public string DisplayName => _lifecycle.DisplayName;
    public IReadOnlyList<RecentFileEntry> RecentEntries => _lifecycle.RecentEntries;
    public bool CanPrint => _print.Capabilities.CanOpenNativePrintDialog ||
        _print.Capabilities.CanSubmitToNativePrinter;
    public bool CanExportVideo => _video.Capabilities.CanEncodeMp4;

    public PresentationFileCommandResult? LastResult { get; private set; }
    public PresentationFileCommandError? LastError => LastResult?.Error;
    public PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }
    public PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }
    public PresentationNativePrintHandoffPlan? LastNativePrintHandoffPlan { get; private set; }
    public PresentationPrintOutputPackageExecutionDescriptor? LastPrintExecutionDescriptor { get; private set; }
    public PresentationVideoExportPlan? LastVideoExportPlan { get; private set; }
    public PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }
    public PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan { get; private set; }
    public PresentationVideoFramePackageExecutionDescriptor? LastVideoExecutionDescriptor { get; private set; }
    public PresentationImageExportResult? LastImageExportResult { get; private set; }
    public IReadOnlyList<string> LastVideoFrameImageDiagnostics { get; private set; } = [];
    public IReadOnlyList<string> LastPrintImageDiagnostics { get; private set; } = [];
    public PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }

    private int _videoExportInProgress;

    public void MarkDirty() => _lifecycle.MarkDirty();

    /// <summary>
    /// Loads a recovered autosave snapshot, retargeting the presentation at
    /// <paramref name="originalPath"/> and marking it dirty. Returns <c>false</c> (never throws)
    /// when the snapshot cannot be read -- e.g. truncated by the crash that produced it. The caller
    /// must NOT delete the snapshot on <c>false</c>: it may be the user's only copy of the unsaved
    /// presentation. Mirrors FreeW's <c>FileCommands.OpenSnapshot</c>.
    /// </summary>
    public bool RestoreAutosaveSnapshot(string snapshotPath, string? originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        Presentation recovered;
        try
        {
            recovered = PptxPackageReader.Read(snapshotPath);
        }
        catch
        {
            return false;
        }

        _loadPresentation(recovered);
        _lifecycle.MarkDirtyWithPath(originalPath);
        return true;
    }

    public async Task<PresentationFileCommandResult> NewAsync(CancellationToken cancellationToken = default)
    {
        var accepted = await _lifecycle.NewAsync(
            PresentationFileTextResources.Presentation.NewAction,
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _loadPresentation(Presentation.CreateEmpty());
                return Task.CompletedTask;
            });

        return await CompleteAsync(
            accepted
                ? PresentationFileCommandResult.Success(PresentationFileCommand.New)
                : PresentationFileCommandResult.Cancel(PresentationFileCommand.New),
            cancellationToken);
    }

    public async Task<PresentationFileCommandResult> OpenAsync(CancellationToken cancellationToken = default)
    {
        PresentationFileCommandResult? operationResult = null;
        var accepted = await _lifecycle.OpenAsync(
            PresentationFileTextResources.Presentation.OpenAction,
            async () =>
            {
                var selection = await _picker.PickOpenFileAsync(
                    new PresentationFileOpenPickerRequest(
                        PresentationFileDialogPlanner.BuildOpenDialogPlan(),
                        PresentationFileDialogPlanner.BuildOpenPickerPlan(),
                        PresentationFileTextResources.Presentation.OpenPickerTitle),
                    cancellationToken);
                var pickerResult = PickerResult(PresentationFileCommand.Open, selection);
                operationResult = pickerResult is null
                    ? null
                    : await CompleteAsync(pickerResult, cancellationToken);
                return selection.Status == OperationStatus.Completed ? selection.Path : null;
            },
            async path =>
            {
                operationResult = await OpenPathCoreAsync(path, suppressRecentFiles: false, cancellationToken);
                return operationResult.Succeeded;
            });

        if (operationResult is not null)
            return operationResult;

        return await CompleteAsync(
            accepted
                ? PresentationFileCommandResult.Success(PresentationFileCommand.Open)
                : PresentationFileCommandResult.Cancel(PresentationFileCommand.Open),
            cancellationToken);
    }

    public Task<PresentationFileCommandResult> OpenPathAsync(
        string path,
        bool suppressRecentFiles = false,
        CancellationToken cancellationToken = default) =>
        OpenPathCoreAsync(path, suppressRecentFiles, cancellationToken);

    internal Task<PresentationFileCommandResult> OpenStartupPathAsync(
        string path,
        bool reportFeedback,
        CancellationToken cancellationToken = default) =>
        OpenPathCoreAsync(path, suppressRecentFiles: false, cancellationToken, reportFeedback);

    internal Task ReportResultAsync(
        PresentationFileCommandResult result,
        CancellationToken cancellationToken = default) =>
        ReportFeedbackAsync(result, cancellationToken);

    public async Task<PresentationFileCommandResult> OpenRecentPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        PresentationFileCommandResult? operationResult = null;
        var accepted = await _lifecycle.OpenAsync(
            PresentationFileTextResources.Presentation.OpenAction,
            () => Task.FromResult<string?>(path),
            async selectedPath =>
            {
                operationResult = await OpenPathCoreAsync(selectedPath, suppressRecentFiles: false, cancellationToken);
                return operationResult.Succeeded;
            });

        return operationResult ?? await CompleteAsync(
            accepted
                ? PresentationFileCommandResult.Success(PresentationFileCommand.Open, path)
                : PresentationFileCommandResult.Cancel(PresentationFileCommand.Open),
            cancellationToken);
    }

    public async Task<PresentationFileCommandResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        PresentationFileCommandResult? operationResult = null;
        var accepted = await _lifecycle.SaveAsync(
            async path =>
            {
                operationResult = await SavePathCoreAsync(
                    PresentationFileCommand.Save,
                    path,
                    cancellationToken);
                return operationResult.Succeeded;
            },
            async () =>
            {
                operationResult = await SaveAsCoreAsync(cancellationToken);
                return operationResult.Succeeded;
            });

        return operationResult ?? await CompleteAsync(
            accepted
                ? PresentationFileCommandResult.Success(PresentationFileCommand.Save, CurrentPath)
                : PresentationFileCommandResult.Cancel(PresentationFileCommand.Save),
            cancellationToken);
    }

    public Task<PresentationFileCommandResult> SaveAsAsync(CancellationToken cancellationToken = default) =>
        SaveAsCoreAsync(cancellationToken);

    public Task<PresentationFileCommandResult> SavePathAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        SavePathCoreAsync(PresentationFileCommand.Save, path, cancellationToken);

    public Task<bool> ConfirmCloseAllowedAsync(string action = CloseAction) =>
        _lifecycle.ConfirmCloseAllowedAsync(action);

    public async Task<PresentationFileCommandResult> ExportPdfAsync(CancellationToken cancellationToken = default)
    {
        var command = PresentationFileCommand.ExportPdf;
        var selection = await _picker.PickSaveFileAsync(
            new PresentationFileSavePickerRequest(
                command,
                PresentationExportPlanner.BuildPdfExportDialogPlan(CurrentFileName),
                PresentationExportPlanner.BuildPdfExportPickerPlan(CurrentFileName),
                PresentationExportPlanner.PdfExportPickerTitle,
                ShowOverwritePrompt: false),
            cancellationToken);
        var pickerResult = PickerResult(command, selection);
        if (pickerResult is not null)
            return await CompleteAsync(pickerResult, cancellationToken);

        return await ExportPdfArtifactAsync(
            command,
            selection.Path!,
            () => PresentationFilePdfExportExecutor.ExportRaster(
                _getPresentation(),
                request: null,
                _render),
            cancellationToken);
    }

    public async Task<PresentationFileCommandResult> ExportNotesPagePdfAsync(
        PresentationSlideRangeRequest? range = null,
        CancellationToken cancellationToken = default)
    {
        var command = PresentationFileCommand.ExportNotesPagePdf;
        var presentation = _getPresentation();
        var exportPlan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(range, presentation.Slides.Count);
        if (!exportPlan.CanExecute)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Invalid(
                    command,
                    PresentationFileTextResources.ErrorSummary(command),
                    exportPlan.DisabledReason ?? "No notes pages can be exported."),
                cancellationToken);
        }

        var selection = await _picker.PickSaveFileAsync(
            new PresentationFileSavePickerRequest(
                command,
                PresentationExportPlanner.BuildNotesPagePdfExportDialogPlan(CurrentFileName),
                PresentationExportPlanner.BuildNotesPagePdfExportPickerPlan(CurrentFileName),
                PresentationExportPlanner.NotesPagePdfExportPickerTitle,
                ShowOverwritePrompt: false),
            cancellationToken);
        var pickerResult = PickerResult(command, selection);
        if (pickerResult is not null)
            return await CompleteAsync(pickerResult, cancellationToken);

        var request = new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            range));
        LastNotesPagePdfRenderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            presentation,
            request);
        return await ExportPdfArtifactAsync(
            command,
            selection.Path!,
            () => PresentationFilePdfExportExecutor.ExportNotesPages(
                presentation,
                request,
                _render),
            cancellationToken);
    }

    private async Task<PresentationFileCommandResult> ExportPdfArtifactAsync(
        PresentationFileCommand command,
        string path,
        Func<PresentationPdfExportArtifact> render,
        CancellationToken cancellationToken)
    {
        var execution = await _atomicExportExecutor.ExecuteAsync<PresentationPdfExportArtifact>(
            path,
            async (output, token) =>
            {
                token.ThrowIfCancellationRequested();
                var artifact = render();
                await output.WriteAsync(artifact.Bytes, token);
                return artifact;
            },
            cancellationToken);

        PresentationFileCommandResult result;
        if (execution.Succeeded)
        {
            var artifact = execution.Value!;
            result = PresentationFileCommandResult.Success(
                command,
                execution.Path,
                $"Exported {Path.GetFileName(execution.Path)}",
                artifact.ImageDiagnostics);
        }
        else if (execution.Cancelled)
        {
            result = PresentationFileCommandResult.Cancel(command);
        }
        else
        {
            var exception = execution.Exception ?? new IOException(
                execution.Error?.Detail.Message ??
                execution.Validation?.Detail.ToString() ??
                "PDF export did not complete.");
            result = PresentationFileCommandResult.Failure(
                command,
                PresentationFileTextResources.ErrorSummary(command),
                exception,
                execution.Path ?? path);
        }

        return await CompleteAsync(result, cancellationToken);
    }

    public async Task<PresentationFileCommandResult> ExportImagesAsync(CancellationToken cancellationToken = default)
    {
        var currentDirectory = CurrentPath is null ? null : Path.GetDirectoryName(CurrentPath);
        var selection = await _picker.PickFolderAsync(
            new PresentationFolderPickerRequest(
                PresentationFileCommand.ExportImages,
                PresentationExportPlanner.ImageExportPickerTitle,
                currentDirectory),
            cancellationToken);
        var pickerResult = PickerResult(PresentationFileCommand.ExportImages, selection);
        if (pickerResult is not null)
            return await CompleteAsync(pickerResult, cancellationToken);

        return await ExportImagesToFolderAsync(selection.Path!, _getImageExportRange(), cancellationToken);
    }

    public async Task<PresentationFileCommandResult> ExportImagesToFolderAsync(
        string outputDirectory,
        PresentationSlideRangeRequest? range = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var artifact = PresentationImageExportExecutor.ExportWithDiagnostics(
                _getPresentation(),
                new PresentationImageExportRequest(
                    outputDirectory,
                    BaseFileName: Path.GetFileNameWithoutExtension(CurrentFileName),
                    SlideRange: range),
                _render.RenderSlideToPng);
            LastImageExportResult = artifact.Result;
            return await CompleteAsync(
                PresentationFileCommandResult.Success(
                    PresentationFileCommand.ExportImages,
                    outputDirectory,
                    $"Exported slides to {outputDirectory}",
                    artifact.ImageDiagnostics),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.ExportImages,
                    PresentationFileTextResources.ErrorSummary(PresentationFileCommand.ExportImages),
                    ex,
                    outputDirectory),
                cancellationToken);
        }
    }

    public PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        int? slidesPerPage = null,
        PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getPresentation();
        return PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                range,
                HandoutSlidesPerPage: slidesPerPage),
            presentation,
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
    }

    public PresentationNotesPagePdfRenderPlan BuildNotesPagePdfRenderPlan(
        PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            _getPresentation(),
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range)));
        return LastNotesPagePdfRenderPlan;
    }

    public PresentationPrintOutputPackage BuildPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        if (_printPackageFactory is not null)
        {
            LastPrintOutputPackage = _printPackageFactory(request);
            LastPrintImageDiagnostics = [];
        }
        else
        {
            var imageDiagnostics = new List<string>();
            LastPrintOutputPackage = PresentationPrintOutputPackageExecutor.BuildPackageWithDiagnostics(
                _getPresentation(),
                request,
                _render,
                imageDiagnostics);
            LastPrintImageDiagnostics = imageDiagnostics;
        }

        LastPrintExecutionDescriptor = PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(
            LastPrintOutputPackage,
            _print.Capabilities,
            CurrentFileName);
        LastNativePrintHandoffPlan = LastPrintExecutionDescriptor.HandoffPlan;
        return LastPrintOutputPackage;
    }

    public PresentationNativePrintHandoffPlan BuildNativePrintHandoffPlan(
        PresentationPrintOutputPackagePlan packagePlan,
        PresentationNativePrintHandoffHostCapabilities? capabilities = null)
    {
        LastNativePrintHandoffPlan = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(
            packagePlan,
            capabilities ?? _print.Capabilities,
            CurrentFileName);
        return LastNativePrintHandoffPlan;
    }

    public PresentationNativePrintHandoffPlan ExecuteNativePrintHandoff(PresentationPrintRequest? request = null)
    {
        BuildPrintOutputPackage(request);
        return LastPrintExecutionDescriptor!.HandoffPlan;
    }

    public PresentationPrintBackstagePlan BuildPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        var presentation = _getPresentation();
        LastPrintBackstagePlan = PresentationPrintBackstagePlanner.Build(
            request,
            presentation,
            _getPrintCurrentSlideNumber(),
            _getPrintSelectedSlideNumbers() ?? request?.SlideRange?.SelectedSlideNumbers,
            _print.Capabilities,
            CurrentFileName);
        LastNativePrintHandoffPlan = LastPrintBackstagePlan.NativePrintHandoff;
        return LastPrintBackstagePlan;
    }

    public async Task<PresentationFileCommandResult> PrintAsync(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanPrint)
        {
            var reason = _print.Capabilities.UnavailableReason ?? "No native printer is available.";
            return await CompleteAsync(
                PresentationFileCommandResult.Unavailable(PresentationFileCommand.Print, reason),
                cancellationToken);
        }

        var normalized = request ?? new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);
        try
        {
            var native = await _print.PrintAsync(
                _getPresentation(),
                normalized,
                BuildPrintOutputPackage,
                cancellationToken);
            return await CompleteNativeAsync(
                PresentationFileCommand.Print,
                PresentationNativeCommandOutcomePlanner.BuildPrintCommandResult(native),
                path: null,
                cancellationToken,
                LastPrintImageDiagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Print,
                    PresentationFileTextResources.ErrorSummary(PresentationFileCommand.Print),
                    ex),
                cancellationToken);
        }
    }

    public PresentationVideoExportPlan BuildVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = PresentationExportPlanner.BuildVideoExportPlan(
            request,
            _getPresentation(),
            _video.Capabilities);
        return LastVideoExportPlan;
    }

    public PresentationVideoFramePackage BuildVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        var artifact = _videoPackageArtifactFactory?.Invoke(request) ??
            PresentationVideoFramePackageExecutor.BuildPackageWithDiagnostics(
                _getPresentation(),
                request,
                _render.RenderSlideToPng,
                _video.Capabilities);

        LastVideoFramePackage = artifact.Package;
        LastVideoFrameImageDiagnostics = artifact.ImageDiagnostics;

        LastVideoExportPlan = LastVideoFramePackage.Plan.ExportPlan;
        LastVideoExecutionDescriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
            LastVideoFramePackage,
            _video.Capabilities,
            CurrentFileName);
        LastVideoExportHandoffPlan = LastVideoExecutionDescriptor.HandoffPlan;
        return LastVideoFramePackage;
    }

    public async Task<PresentationFileCommandResult> ExportVideoAsync(
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryBeginVideoExport())
        {
            return await CompleteAsync(VideoExportAlreadyRunningResult(), cancellationToken);
        }

        try
        {
            if (!CanExportVideo)
            {
                return await CompleteAsync(
                    PresentationFileCommandResult.Unavailable(
                        PresentationFileCommand.ExportVideo,
                        _video.Capabilities.UnavailableReason ?? "No MP4 encoder is available."),
                    cancellationToken);
            }

            var plan = BuildVideoExportPlan(request);
            if (!plan.CanExecute)
            {
                return await CompleteAsync(
                    PresentationFileCommandResult.Invalid(
                        PresentationFileCommand.ExportVideo,
                        PresentationFileTextResources.ErrorSummary(PresentationFileCommand.ExportVideo),
                        plan.DisabledReason ?? "Video export requires at least one slide."),
                    cancellationToken);
            }

            var selection = await _picker.PickSaveFileAsync(
                new PresentationFileSavePickerRequest(
                    PresentationFileCommand.ExportVideo,
                    PresentationExportPlanner.BuildVideoExportDialogPlan(CurrentFileName),
                    PresentationExportPlanner.BuildVideoExportPickerPlan(CurrentFileName),
                    PresentationExportPlanner.VideoExportPickerTitle),
                cancellationToken);
            var pickerResult = PickerResult(PresentationFileCommand.ExportVideo, selection);
            if (pickerResult is not null)
                return await CompleteAsync(pickerResult, cancellationToken);

            // Already holding the re-entrancy guard for this call -- go straight to the core
            // export so the public ExportVideoToPathAsync's own guard check doesn't see it as busy.
            return await ExportVideoToPathCoreAsync(selection.Path!, request, cancellationToken);
        }
        finally
        {
            EndVideoExport();
        }
    }

    public async Task<PresentationFileCommandResult> ExportVideoToPathAsync(
        string outputPath,
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!TryBeginVideoExport())
        {
            return await CompleteAsync(VideoExportAlreadyRunningResult(), cancellationToken);
        }

        try
        {
            return await ExportVideoToPathCoreAsync(outputPath, request, cancellationToken);
        }
        finally
        {
            EndVideoExport();
        }
    }

    private async Task<PresentationFileCommandResult> ExportVideoToPathCoreAsync(
        string outputPath,
        PresentationVideoExportRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = BuildVideoFramePackage(request);
            var native = await _video.ExportAsync(
                package,
                outputPath,
                _getPresentation().RecordingMediaArtifacts,
                cancellationToken);
            return await CompleteNativeAsync(
                PresentationFileCommand.ExportVideo,
                native,
                outputPath,
                cancellationToken,
                LastVideoFrameImageDiagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.ExportVideo,
                    PresentationFileTextResources.ErrorSummary(PresentationFileCommand.ExportVideo),
                    ex,
                    outputPath),
                cancellationToken);
        }
    }

    /// <summary>
    /// Guards against a second Export Video invocation starting while one is already writing the
    /// output file (both FreeP shells fire Export Video as fire-and-forget from a menu/backstage
    /// click, so a double click previously started two concurrent exports racing on the same path).
    /// </summary>
    private bool TryBeginVideoExport() =>
        Interlocked.CompareExchange(ref _videoExportInProgress, 1, 0) == 0;

    private void EndVideoExport() => Interlocked.Exchange(ref _videoExportInProgress, 0);

    private static PresentationFileCommandResult VideoExportAlreadyRunningResult() =>
        PresentationFileCommandResult.Unavailable(
            PresentationFileCommand.ExportVideo,
            "A video export is already running.");

    private async Task<PresentationFileCommandResult> OpenPathCoreAsync(
        string path,
        bool suppressRecentFiles,
        CancellationToken cancellationToken,
        bool reportFeedback = true)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = PresentationFilePersistenceWorkflow.Open(path);
            _loadPresentation(result.Presentation);
            _currentFileSourceLastWriteTimeUtc = result.SourceLastWriteTimeUtc;
            SetSaved(result.SavedPath, suppressRecentFiles || result.SuppressRecentFiles);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(
                    PresentationFileCommand.Open,
                    result.SavedPath,
                    SisterAppFileTextPlanner.FormatOpened(
                        PresentationFileTextResources.Presentation,
                        Path.GetFileName(path))),
                cancellationToken,
                reportFeedback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Open,
                    PresentationFileTextResources.ErrorSummary(PresentationFileCommand.Open),
                    ex,
                    path),
                cancellationToken,
                reportFeedback);
        }
    }

    private async Task<PresentationFileCommandResult> SaveAsCoreAsync(CancellationToken cancellationToken)
    {
        var selection = await _picker.PickSaveFileAsync(
            new PresentationFileSavePickerRequest(
                PresentationFileCommand.SaveAs,
                PresentationFileDialogPlanner.BuildSaveAsDialogPlan(CurrentFileName),
                PresentationFileDialogPlanner.BuildSavePickerPlan(CurrentFileName),
                PresentationFileTextResources.Presentation.SavePickerTitle,
                ShowOverwritePrompt: true),
            cancellationToken);
        var pickerResult = PickerResult(PresentationFileCommand.SaveAs, selection);
        if (pickerResult is not null)
            return await CompleteAsync(pickerResult, cancellationToken);

        if (!PresentationFileDialogPlanner.TryResolveSavePickerPath(selection.Path!, out var resolvedPath))
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Invalid(
                    PresentationFileCommand.SaveAs,
                    PresentationFileTextResources.ErrorSummary(PresentationFileCommand.SaveAs),
                    PresentationFileDialogPlanner.UnsupportedSavePathMessage,
                    selection.Path),
                cancellationToken);
        }

        return await SavePathCoreAsync(PresentationFileCommand.SaveAs, resolvedPath, cancellationToken);
    }

    private static readonly Func<PresentationFileCommand, PresentationFileCommandResult>
        ExternalWriteConflictResult = static command => PresentationFileCommandResult.Cancel(
            command,
            "Save canceled -- the file was changed by another program.");

    private async Task<PresentationFileCommandResult> SavePathCoreAsync(
        PresentationFileCommand command,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only guard a save that overwrites the SAME path this session's identity already
            // tracks (CurrentPath). Save-As to a different path -- or the first save of a
            // never-saved presentation, where CurrentPath is null -- has nothing to compare
            // against, so the comparer's null-safe Equals naturally turns the guard off.
            var expectedLastWriteTimeUtc = PlatformPathIdentityComparer.Current.Equals(CurrentPath, path)
                ? _currentFileSourceLastWriteTimeUtc
                : null;

            if (expectedLastWriteTimeUtc is { } expectedWriteTimeUtc &&
                File.Exists(path) &&
                File.GetLastWriteTimeUtc(path) != expectedWriteTimeUtc)
            {
                // Someone else wrote path since it was last observed (another FreeP instance, a
                // sync client, a colleague on a shared path). Ask before clobbering their write; a
                // null callback (host wired nothing) or a declined prompt both refuse the overwrite.
                var observedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
                var confirmed = _confirmExternallyModifiedOverwriteAsync is not null &&
                    await _confirmExternallyModifiedOverwriteAsync(path, cancellationToken);
                if (!confirmed)
                    return await CompleteAsync(ExternalWriteConflictResult(command), cancellationToken);

                // The user accepted the write visible at the prompt. Advance the baseline to that
                // accepted version so Save's own check-then-act guard (last line of defense
                // against a race between the prompt and the write below) compares against what was
                // just approved rather than the stale value that triggered this prompt.
                expectedLastWriteTimeUtc = observedWriteTimeUtc;
            }

            var result = PresentationFilePersistenceWorkflow.Save(path, _getPresentation(), expectedLastWriteTimeUtc);
            _currentFileSourceLastWriteTimeUtc =
                File.Exists(result.SavedPath) ? File.GetLastWriteTimeUtc(result.SavedPath) : null;
            _lifecycle.MarkSavedWithPath(result.SavedPath, result.SuppressRecentFiles);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(
                    command,
                    result.SavedPath,
                    SisterAppFileTextPlanner.FormatSaved(
                        PresentationFileTextResources.Presentation,
                        Path.GetFileName(result.SavedPath))),
                cancellationToken);
        }
        catch (PresentationExternallyModifiedException)
        {
            // A second writer landed between the check above and the write (race). Report the
            // same conflict outcome rather than re-prompting for a version the user never saw.
            return await CompleteAsync(ExternalWriteConflictResult(command), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    command,
                    PresentationFileTextResources.ErrorSummary(command),
                    ex,
                    path),
                cancellationToken);
        }
    }

    private void SetSaved(string? path, bool suppressRecentFiles)
    {
        if (path is null)
            _lifecycle.MarkSavedWithoutPath();
        else
            _lifecycle.MarkSavedWithPath(path, suppressRecentFiles);
    }

    private static PresentationFileCommandResult? PickerResult(
        PresentationFileCommand command,
        PresentationFilePickerResult selection) =>
        selection.Status switch
        {
            OperationStatus.Completed => null,
            OperationStatus.Cancelled or OperationStatus.Declined =>
                PresentationFileCommandResult.Cancel(command),
            OperationStatus.Unavailable => PresentationFileCommandResult.Unavailable(
                command,
                selection.Message ?? PresentationFileTextResources.NativePickerUnavailable),
            OperationStatus.ValidationFailed => PresentationFileCommandResult.Invalid(
                command,
                PresentationFileTextResources.ErrorSummary(command),
                selection.Message ?? PresentationFileTextResources.NonLocalSelection),
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection.Status, "Unsupported picker result."),
        };

    private async Task<PresentationFileCommandResult> CompleteNativeAsync(
        PresentationFileCommand command,
        PresentationNativeCommandResult native,
        string? path,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? imageDiagnostics = null)
    {
        var result = native.Succeeded
            ? PresentationFileCommandResult.Success(command, path, native.StatusText, imageDiagnostics)
            : native.Cancelled
                ? PresentationFileCommandResult.Cancel(command, native.StatusText)
                : PresentationFileCommandResult.Failure(
                    command,
                    PresentationFileTextResources.ErrorSummary(command),
                    new InvalidOperationException(native.FailureReason ?? native.StatusText),
                    path,
                    native.StatusText);
        return await CompleteAsync(result, cancellationToken);
    }

    private async Task<PresentationFileCommandResult> CompleteAsync(
        PresentationFileCommandResult result,
        CancellationToken cancellationToken,
        bool reportFeedback = true)
    {
        LastResult = result;
        if (reportFeedback)
            await ReportFeedbackAsync(result, cancellationToken);
        return result;
    }

    private Task ReportFeedbackAsync(
        PresentationFileCommandResult result,
        CancellationToken cancellationToken) =>
        _feedback?.ReportAsync(result, cancellationToken) ?? Task.CompletedTask;

}
