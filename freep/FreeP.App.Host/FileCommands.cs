using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Skia;
using Free.Shared.Pdf.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using FreeP.Core.Model;
using Microsoft.Win32;

namespace FreeP.App.Host;

/// <summary>
/// WPF compatibility facade over the portable presentation file command session.
/// Native dialogs, rendering, printing, encoding, and message boxes stay in the ports below.
/// </summary>
internal sealed class FileCommands
{
    private readonly PresentationFileCommandSession _session;
    private readonly WpfPresentationVideoPort _videoPort;

    public FileCommands(
        Window window,
        Func<Presentation> getModel,
        Action<Presentation> loadModel,
        Action onChanged,
        FreePOptions? options = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        IUserMessageService? messageService = null,
        Func<PresentationSlideRangeRequest?>? getImageExportRange = null,
        Func<int?>? getPrintCurrentSlideNumber = null,
        Func<IReadOnlyList<int>?>? getPrintSelectedSlideNumbers = null,
        LinuxVideoEncoderCapability? videoEncoderCapability = null,
        ILinuxVideoExportAdapter? videoExportAdapter = null,
        WpfNativePrintCapability? nativePrintCapability = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(getModel);
        ArgumentNullException.ThrowIfNull(loadModel);
        ArgumentNullException.ThrowIfNull(onChanged);

        var resolvedOptions = options ?? new FreePOptions();
        PresentationFileCommandSession? session = null;
        var workflow = new SisterWpfFileCommandWorkflow(
            "FreeP",
            () => resolvedOptions.RecentFilesCap,
            onChanged,
            () => session?.SaveAsync().GetAwaiter().GetResult().Succeeded == true,
            loadRecentFilesStore,
            messageService);
        var lifecycle = new PresentationFileLifecycleAdapter(workflow.Workflow);
        var picker = new WpfPresentationFilePickerPort(window);
        var render = new WpfPresentationFileRenderPort();
        var print = new WpfPresentationPrintPort(
            window,
            nativePrintCapability ?? WpfNativePrintCapabilityDetector.Detect(),
            () => session?.LastNativePrintHandoffPlan);
        var resolvedVideoCapability = videoEncoderCapability ??
            videoExportAdapter?.Capability ??
            WpfVideoEncoderCapabilityDetector.Detect();
        _videoPort = new WpfPresentationVideoPort(
            videoExportAdapter ?? new WpfVideoExportAdapter(resolvedVideoCapability),
            BuildVideoExportHostCapabilities(resolvedVideoCapability));
        session = new PresentationFileCommandSession(
            getModel,
            loadModel,
            lifecycle,
            picker,
            render,
            print,
            _videoPort,
            new WpfPresentationFileFeedbackPort(workflow),
            getImageExportRange,
            getPrintCurrentSlideNumber,
            getPrintSelectedSlideNumbers);
        _session = session;
    }

    public bool IsDirty => _session.IsDirty;
    public string? CurrentPath => _session.CurrentPath;
    public string DisplayName => _session.DisplayName;
    public IReadOnlyList<RecentFileEntry> RecentEntries => _session.RecentEntries;
    public bool CanPrint => _session.CanPrint;
    public bool CanExportVideo => _session.CanExportVideo;
    public PresentationPrintOutputPackage? LastPrintOutputPackage => _session.LastPrintOutputPackage;
    public PresentationPrintBackstagePlan? LastPrintBackstagePlan => _session.LastPrintBackstagePlan;
    public PresentationNativePrintHandoffPlan? LastNativePrintHandoffPlan => _session.LastNativePrintHandoffPlan;
    public PresentationPrintOutputPackageExecutionDescriptor? LastPrintExecutionDescriptor =>
        _session.LastPrintExecutionDescriptor;
    public PresentationVideoFramePackage? LastVideoFramePackage => _session.LastVideoFramePackage;
    public PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan => _session.LastVideoExportHandoffPlan;
    public LinuxVideoExportResult? LastVideoExportResult => _videoPort.LastResult;
    public PresentationVideoFramePackageExecutionDescriptor? LastVideoExecutionDescriptor =>
        _session.LastVideoExecutionDescriptor;

    public void MarkDirty() => _session.MarkDirty();
    public bool New() => Run(_session.NewAsync());
    public bool Open() => Run(_session.OpenAsync());

    /// <summary>Loads a recent/startup path without adding a second dirty gate.</summary>
    public bool OpenPath(string path) => Run(_session.OpenPathAsync(path));

    public bool Save() => Run(_session.SaveAsync());
    public bool SaveAs() => Run(_session.SaveAsAsync());
    public bool ExportPdf() => Run(_session.ExportPdfAsync());

    public bool ExportNotesPagePdf(PresentationSlideRangeRequest? range = null) =>
        Run(_session.ExportNotesPagePdfAsync(range));

    public bool ExportImages() => Run(_session.ExportImagesAsync());

    public bool ExportImagesToFolder(
        string outputDirectory,
        PresentationSlideRangeRequest? range = null) =>
        Run(_session.ExportImagesToFolderAsync(outputDirectory, range));

    public PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        int? slidesPerPage = null,
        PresentationSlideRangeRequest? range = null) =>
        _session.BuildHandoutLayoutPlan(slidesPerPage, range);

    public PresentationNotesPagePdfRenderPlan BuildNotesPagePdfRenderPlan(
        PresentationSlideRangeRequest? range = null) =>
        _session.BuildNotesPagePdfRenderPlan(range);

    public PresentationPrintOutputPackage BuildPrintOutputPackage(PresentationPrintRequest? request = null) =>
        _session.BuildPrintOutputPackage(request);

    public PresentationNativePrintHandoffPlan BuildNativePrintHandoffPlan(
        PresentationPrintOutputPackagePlan packagePlan,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null) =>
        _session.BuildNativePrintHandoffPlan(packagePlan, hostCapabilities);

    public PresentationNativePrintHandoffPlan ExecuteNativePrintHandoff(
        PresentationPrintRequest? request = null) =>
        _session.ExecuteNativePrintHandoff(request);

    public PresentationPrintBackstagePlan BuildPrintBackstagePlan(PresentationPrintRequest? request = null) =>
        _session.BuildPrintBackstagePlan(request);

    public bool Print(PresentationPrintRequest? request = null) => Run(_session.PrintAsync(request));

    public PresentationVideoFramePackage BuildVideoFramePackage(PresentationVideoExportRequest? request = null) =>
        _session.BuildVideoFramePackage(request);

    public PresentationVideoExportHandoffPlan BuildVideoExportHandoffPlan(
        PresentationVideoFramePackagePlan packagePlan,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null) =>
        _session.BuildVideoExportHandoffPlan(packagePlan, hostCapabilities);

    public PresentationVideoExportPlan BuildVideoExportPlan(PresentationVideoExportRequest? request = null) =>
        _session.BuildVideoExportPlan(request);

    public async Task<bool> ExportVideoAsync(PresentationVideoExportRequest? request = null) =>
        (await _session.ExportVideoAsync(request).ConfigureAwait(true)).Succeeded;

    public bool ConfirmCloseAllowed() =>
        _session.ConfirmCloseAllowedAsync().GetAwaiter().GetResult();

    private static bool Run(Task<PresentationFileCommandResult> operation) =>
        operation.GetAwaiter().GetResult().Succeeded;

    private static PresentationVideoExportHandoffHostCapabilities BuildVideoExportHostCapabilities(
        LinuxVideoEncoderCapability capability) =>
        new(
            string.Equals(capability.ExecutablePath, WindowsNativeVideoExportAdapter.ExecutablePath, StringComparison.Ordinal)
                ? "WPF Windows video export host"
                : "WPF video export host",
            capability.CanEncodeMp4,
            capability.CanCaptureNarration,
            capability.CanCaptureCameraAndMedia,
            capability.Reason,
            capability.CanMuxTimedCaptions);
}

internal sealed class WpfPresentationFilePickerPort : IPresentationFilePickerPort
{
    private readonly Window _owner;

    public WpfPresentationFilePickerPort(Window owner) => _owner = owner;

    public Task<PresentationFilePickerResult> PickOpenFileAsync(
        PresentationFileOpenPickerRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = WpfFileDialogService.ShowOpenDialog(_owner, request.DialogPlan, title: request.Title);
        return Task.FromResult(result.Chosen
            ? PresentationFilePickerResult.Selected(result.FileName!)
            : PresentationFilePickerResult.Cancelled);
    }

    public Task<PresentationFilePickerResult> PickSaveFileAsync(
        PresentationFileSavePickerRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = WpfFileDialogService.ShowSaveDialog(_owner, request.DialogPlan, request.Title);
        return Task.FromResult(result.Chosen
            ? PresentationFilePickerResult.Selected(result.FileName!)
            : PresentationFilePickerResult.Cancelled);
    }

    public Task<PresentationFilePickerResult> PickFolderAsync(
        PresentationFolderPickerRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new OpenFolderDialog
        {
            Title = request.Title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(request.InitialDirectory) && Directory.Exists(request.InitialDirectory))
            dialog.InitialDirectory = request.InitialDirectory;

        return Task.FromResult(
            dialog.ShowDialog(_owner) == true && !string.IsNullOrWhiteSpace(dialog.FolderName)
                ? PresentationFilePickerResult.Selected(dialog.FolderName)
                : PresentationFilePickerResult.Cancelled);
    }
}

internal sealed class WpfPresentationFileRenderPort : IPresentationFileRenderPort
{
    public PresentationSlideImageRenderer RenderSlideToPng =>
        WpfPresentationSlideImageRenderer.RenderSlideToPng;
    public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
        WpfPresentationSlideImageRenderer.RenderSlideToPngWithPrintMarkup;
    public PresentationRasterPdfWriter WriteRasterPdf => WpfRasterPdfWriter.WriteToBytes;
    public PresentationPdfContentWriter WriteVectorPdf => SkiaPdfWriter.WriteToBytesWithPortableFallback;

    public byte[] WriteRasterPdfWithDiagnostics(
        PdfRasterDocument document,
        ICollection<string> imageDiagnostics) =>
        WpfRasterPdfWriter.WriteToBytes(document, imageDiagnostics);

    public byte[] WriteVectorPdfWithDiagnostics(
        PdfContentDocument document,
        ICollection<string> imageDiagnostics) =>
        SkiaPdfWriter.WriteToBytesWithPortableFallback(document, imageDiagnostics);
}

internal sealed class WpfPresentationPrintPort : IPresentationPrintPort
{
    private readonly Window _owner;
    private readonly Func<PresentationNativePrintHandoffPlan?> _getLastHandoffPlan;

    public WpfPresentationPrintPort(
        Window owner,
        WpfNativePrintCapability capability,
        Func<PresentationNativePrintHandoffPlan?> getLastHandoffPlan)
    {
        _owner = owner;
        _getLastHandoffPlan = getLastHandoffPlan ?? throw new ArgumentNullException(nameof(getLastHandoffPlan));
        Capabilities = capability.CanPrint
            ? PresentationNativePrintHandoffHostCapabilities.Available("WPF print host")
            : PresentationNativePrintHandoffHostCapabilities.Deferred("WPF print host", capability.Reason);
    }

    public PresentationNativePrintHandoffHostCapabilities Capabilities { get; }

    public Task<PresentationNativePrintPortResult> PrintAsync(
        Presentation presentation,
        PresentationPrintRequest request,
        Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = buildPackage(request);
        var validation = PresentationPrintOutputPackageExecutor.ValidatePackage(package);
        if (!validation.IsValid)
        {
            return Task.FromResult(PresentationNativePrintPortResult.Failure(
                PresentationNativePrintStatusProfile.PresentationDialog,
                validation.FailureReason ?? PresentationPrintOutputPackageExecutor.InvalidPackageReason));
        }

        var handoffPlan = _getLastHandoffPlan();
        if (handoffPlan is null)
        {
            return Task.FromResult(PresentationNativePrintPortResult.Failure(
                PresentationNativePrintStatusProfile.PresentationDialog,
                PresentationNativeCommandOutcomePlanner.PrintHandoffPlanNotBuiltFailure));
        }

        var printed = WpfPresentationPrintService.ShowPrintDialogAndPrint(
            presentation,
            request,
            handoffPlan.SuggestedPrintJobName,
            _owner);
        return Task.FromResult(printed
            ? PresentationNativePrintPortResult.Success(
                PresentationNativePrintStatusProfile.PresentationDialog)
            : PresentationNativePrintPortResult.Cancel(
                PresentationNativePrintStatusProfile.PresentationDialog));
    }
}

internal sealed class WpfPresentationVideoPort : IPresentationVideoPort
{
    private readonly ILinuxVideoExportAdapter _adapter;

    public WpfPresentationVideoPort(
        ILinuxVideoExportAdapter adapter,
        PresentationVideoExportHandoffHostCapabilities capabilities)
    {
        _adapter = adapter;
        Capabilities = capabilities;
    }

    public PresentationVideoExportHandoffHostCapabilities Capabilities { get; }
    public LinuxVideoExportResult? LastResult { get; private set; }

    public async Task<PresentationNativeCommandResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
        CancellationToken cancellationToken)
    {
        var result = await _adapter.ExportAsync(
            package,
            outputPath,
            cancellationToken,
            recordingMediaArtifacts).ConfigureAwait(true);
        LastResult = result with { StatusText = BuildWpfStatusText(result) };
        return LastResult.Succeeded
            ? PresentationNativeCommandResult.Success(LastResult.StatusText)
            : LastResult.Canceled
                ? PresentationNativeCommandResult.Cancel(LastResult.StatusText)
                : PresentationNativeCommandResult.Failure(
                    LastResult.StatusText,
                    LastResult.FailureReason ?? PresentationFileTextResources.VideoExportFailed);
    }

    private static string BuildWpfStatusText(LinuxVideoExportResult result)
    {
        if (result.Canceled)
            return "Video export canceled";
        if (!result.Succeeded)
            return "Video export failed";

        return result.MuxedNarrationTrackCount == 0 &&
            result.MuxedCameraTrackCount == 0 &&
            result.MuxedCaptionTrackCount == 0
                ? "Video export completed (video-only)"
                : $"Video export completed with {result.MuxedNarrationTrackCount} narration track(s), " +
                    $"{result.MuxedCameraTrackCount} camera track(s), and " +
                    $"{result.MuxedCaptionTrackCount} caption track(s)";
    }
}

internal sealed class WpfPresentationFileFeedbackPort : IPresentationFileCommandFeedbackPort
{
    private readonly SisterWpfFileCommandWorkflow _workflow;

    public WpfPresentationFileFeedbackPort(SisterWpfFileCommandWorkflow workflow) => _workflow = workflow;

    public Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = PresentationNativeCommandOutcomePlanner.BuildFileFeedback(result);
        if (plan.Error is { } error)
        {
            _workflow.ShowError(error.Summary, error.Exception);
        }
        else if (plan.UnavailableDialogTitle is not null)
        {
            _workflow.ShowError(
                plan.UnavailableDialogTitle,
                new InvalidOperationException(plan.UnavailableDialogMessage!));
        }
        else if (result.Succeeded && result.ImageDiagnostics.Count > 0)
        {
            _workflow.ShowExportImageWarnings(
                result.Message ?? "Export completed",
                result.ImageDiagnostics);
        }
        return Task.CompletedTask;
    }
}
