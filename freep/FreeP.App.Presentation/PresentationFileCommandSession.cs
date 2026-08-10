using System.IO;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Pdf;
using Free.Shared.Shell;
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
        OperationOutcome<string, string, string> operation)
    {
        Command = command;
        Operation = operation;
    }

    public PresentationFileCommandResult(
        PresentationFileCommand Command,
        PresentationFileCommandStatus Status,
        PresentationFileCommandValidation Validation,
        string? Path = null,
        string? Message = null,
        PresentationFileCommandError? Error = null)
        : this(Command, PresentationFileOperationOutcomeMapper.MapCommand(Status, Validation, Path, Message, Error))
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
        string? message = null) =>
        new(command, OperationOutcome<string, string, string>.Completed(message, path));

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

public enum PresentationFilePickerStatus
{
    Selected,
    Cancelled,
    Unavailable,
    NonLocalSelection,
}

public sealed record PresentationFilePickerResult
{
    private PresentationFilePickerResult(OperationOutcome<string, string, string> operation)
    {
        Operation = operation;
    }

    public PresentationFilePickerResult(
        PresentationFilePickerStatus Status,
        string? Path = null,
        string? Message = null)
        : this(PresentationFileOperationOutcomeMapper.MapPicker(Status, Path, Message))
    {
    }

    public OperationOutcome<string, string, string> Operation { get; }
    public PresentationFilePickerStatus Status => PresentationFileOperationOutcomeMapper.MapPicker(Operation);
    public string? Path => Operation.Path;
    public string? Message => Operation.Value;

    public void Deconstruct(
        out PresentationFilePickerStatus status,
        out string? path,
        out string? message)
    {
        status = Status;
        path = Path;
        message = Message;
    }

    public static PresentationFilePickerResult Selected(string path) =>
        new(OperationOutcome<string, string, string>.Completed(path: path));

    public static PresentationFilePickerResult Cancelled { get; } =
        new(OperationOutcome<string, string, string>.Cancel());

    public static PresentationFilePickerResult Unavailable(string message) =>
        new(OperationOutcome<string, string, string>.Unavailable(message));

    public static PresentationFilePickerResult NonLocal(string message) =>
        new(OperationOutcome<string, string, string>.ValidationFailure(message, message));
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

    internal static OperationOutcome<string, string, string> MapPicker(
        PresentationFilePickerStatus status,
        string? path,
        string? message) => status switch
    {
        PresentationFilePickerStatus.Selected =>
            OperationOutcome<string, string, string>.Completed(message, path),
        PresentationFilePickerStatus.Cancelled =>
            OperationOutcome<string, string, string>.Cancel(message, path),
        PresentationFilePickerStatus.Unavailable =>
            OperationOutcome<string, string, string>.Unavailable(message, path),
        PresentationFilePickerStatus.NonLocalSelection =>
            OperationOutcome<string, string, string>.ValidationFailure(
                message ?? "The selected item does not have a local path.",
                message,
                path),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported picker status."),
    };

    internal static PresentationFilePickerStatus MapPicker(
        OperationOutcome<string, string, string> operation) => operation.Status switch
        {
            OperationStatus.Completed => PresentationFilePickerStatus.Selected,
            OperationStatus.Cancelled or OperationStatus.Declined => PresentationFilePickerStatus.Cancelled,
            OperationStatus.Unavailable => PresentationFilePickerStatus.Unavailable,
            OperationStatus.ValidationFailed => PresentationFilePickerStatus.NonLocalSelection,
            _ => throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation.Status,
                "Unsupported shared picker outcome."),
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
    private readonly IPresentationFileCommandFeedbackPort? _feedback;
    private readonly Func<PresentationSlideRangeRequest?> _getImageExportRange;
    private readonly Func<int?> _getPrintCurrentSlideNumber;
    private readonly Func<IReadOnlyList<int>?> _getPrintSelectedSlideNumbers;
    private readonly Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? _printPackageFactory;
    private readonly Func<PresentationVideoExportRequest?, PresentationVideoFramePackage>? _videoPackageFactory;

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
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackage>? videoPackageFactory = null)
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
        _videoPackageFactory = videoPackageFactory;
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
    public PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }

    public void MarkDirty() => _lifecycle.MarkDirty();

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
                return selection.Status == PresentationFilePickerStatus.Selected ? selection.Path : null;
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

        try
        {
            var bytes = PresentationRasterPdfExporter.ExportToBytes(
                _getPresentation(),
                request: null,
                _render.RenderSlideToPng,
                _render.WriteRasterPdf);
            ExportAtomicWriter.WriteAllBytes(selection.Path!, bytes);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(command, selection.Path, $"Exported {Path.GetFileName(selection.Path)}"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    command,
                    "Could not export the presentation to PDF",
                    ex,
                    selection.Path),
                cancellationToken);
        }
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
                    "Could not export the presentation notes pages to PDF",
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

        try
        {
            var request = new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range));
            LastNotesPagePdfRenderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
                presentation,
                request);
            var bytes = PresentationNotesPagePdfExporter.ExportToBytes(
                presentation,
                request,
                _render.WriteVectorPdf);
            ExportAtomicWriter.WriteAllBytes(selection.Path!, bytes);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(command, selection.Path, $"Exported {Path.GetFileName(selection.Path)}"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    command,
                    "Could not export the presentation notes pages to PDF",
                    ex,
                    selection.Path),
                cancellationToken);
        }
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
            LastImageExportResult = PresentationImageExportExecutor.Export(
                _getPresentation(),
                new PresentationImageExportRequest(
                    outputDirectory,
                    BaseFileName: Path.GetFileNameWithoutExtension(CurrentFileName),
                    SlideRange: range),
                _render.RenderSlideToPng);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(
                    PresentationFileCommand.ExportImages,
                    outputDirectory,
                    $"Exported slides to {outputDirectory}"),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.ExportImages,
                    "Could not export the presentation slides to images",
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
        LastPrintOutputPackage = _printPackageFactory?.Invoke(request) ??
            PresentationPrintOutputPackageExecutor.BuildPackage(
                _getPresentation(),
                request,
                _render.RenderSlideToPng,
                _render.WriteRasterPdf,
                _render.WriteVectorPdf,
                _render.RenderSlideToPngWithPrintMarkup);
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
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Print,
                    "Could not print the presentation",
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
        LastVideoFramePackage = _videoPackageFactory?.Invoke(request) ??
            PresentationVideoFramePackageExecutor.BuildPackage(
                _getPresentation(),
                request,
                _render.RenderSlideToPng,
                _video.Capabilities);
        LastVideoExportPlan = LastVideoFramePackage.Plan.ExportPlan;
        LastVideoExecutionDescriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
            LastVideoFramePackage,
            _video.Capabilities,
            CurrentFileName);
        LastVideoExportHandoffPlan = LastVideoExecutionDescriptor.HandoffPlan;
        return LastVideoFramePackage;
    }

    public PresentationVideoExportHandoffPlan BuildVideoExportHandoffPlan(
        PresentationVideoFramePackagePlan packagePlan,
        PresentationVideoExportHandoffHostCapabilities? capabilities = null)
    {
        LastVideoExportHandoffPlan = PresentationVideoFramePackageExecutor.BuildHandoffPlan(
            packagePlan,
            capabilities ?? _video.Capabilities);
        return LastVideoExportHandoffPlan;
    }

    public async Task<PresentationFileCommandResult> ExportVideoAsync(
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
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
                    "Could not export the presentation video",
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

        return await ExportVideoToPathAsync(selection.Path!, request, cancellationToken);
    }

    public async Task<PresentationFileCommandResult> ExportVideoToPathAsync(
        string outputPath,
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
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
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.ExportVideo,
                    "Could not export the presentation video",
                    ex,
                    outputPath),
                cancellationToken);
        }
    }

    private async Task<PresentationFileCommandResult> OpenPathCoreAsync(
        string path,
        bool suppressRecentFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = PresentationFilePersistenceWorkflow.Open(path);
            _loadPresentation(result.Presentation);
            SetSaved(result.SavedPath, suppressRecentFiles || result.SuppressRecentFiles);
            return await CompleteAsync(
                PresentationFileCommandResult.Success(
                    PresentationFileCommand.Open,
                    result.SavedPath,
                    SisterAppFileTextPlanner.FormatOpened(
                        PresentationFileTextResources.Presentation,
                        Path.GetFileName(path))),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    PresentationFileCommand.Open,
                    "Could not open the presentation",
                    ex,
                    path),
                cancellationToken);
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
                    "Could not save the presentation",
                    PresentationFileDialogPlanner.UnsupportedSavePathMessage,
                    selection.Path),
                cancellationToken);
        }

        return await SavePathCoreAsync(PresentationFileCommand.SaveAs, resolvedPath, cancellationToken);
    }

    private async Task<PresentationFileCommandResult> SavePathCoreAsync(
        PresentationFileCommand command,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = PresentationFilePersistenceWorkflow.Save(path, _getPresentation());
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await CompleteAsync(
                PresentationFileCommandResult.Failure(
                    command,
                    "Could not save the presentation",
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
            PresentationFilePickerStatus.Selected => null,
            PresentationFilePickerStatus.Cancelled => PresentationFileCommandResult.Cancel(command),
            PresentationFilePickerStatus.Unavailable => PresentationFileCommandResult.Unavailable(
                command,
                selection.Message ?? "The native picker is unavailable."),
            PresentationFilePickerStatus.NonLocalSelection => PresentationFileCommandResult.Invalid(
                command,
                ErrorSummary(command),
                selection.Message ?? "The selected item does not have a local path."),
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection.Status, "Unsupported picker result."),
        };

    private async Task<PresentationFileCommandResult> CompleteNativeAsync(
        PresentationFileCommand command,
        PresentationNativeCommandResult native,
        string? path,
        CancellationToken cancellationToken)
    {
        var result = native.Succeeded
            ? PresentationFileCommandResult.Success(command, path, native.StatusText)
            : native.Cancelled
                ? PresentationFileCommandResult.Cancel(command, native.StatusText)
                : PresentationFileCommandResult.Failure(
                    command,
                    ErrorSummary(command),
                    new InvalidOperationException(native.FailureReason ?? native.StatusText),
                    path,
                    native.StatusText);
        return await CompleteAsync(result, cancellationToken);
    }

    private async Task<PresentationFileCommandResult> CompleteAsync(
        PresentationFileCommandResult result,
        CancellationToken cancellationToken)
    {
        LastResult = result;
        if (_feedback is not null)
            await _feedback.ReportAsync(result, cancellationToken);
        return result;
    }

    private static string ErrorSummary(PresentationFileCommand command) => command switch
    {
        PresentationFileCommand.Open => "Could not open the presentation",
        PresentationFileCommand.Save or PresentationFileCommand.SaveAs => "Could not save the presentation",
        PresentationFileCommand.ExportPdf => "Could not export the presentation to PDF",
        PresentationFileCommand.ExportNotesPagePdf => "Could not export the presentation notes pages to PDF",
        PresentationFileCommand.ExportImages => "Could not export the presentation slides to images",
        PresentationFileCommand.Print => "Could not print the presentation",
        PresentationFileCommand.ExportVideo => "Could not export the presentation video",
        _ => "Could not complete the presentation file command",
    };
}
