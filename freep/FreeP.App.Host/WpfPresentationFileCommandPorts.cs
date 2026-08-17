using System.IO;
using System.Windows;
using System.Windows.Threading;
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
/// Creates the portable file-command session with WPF-native ports.
/// </summary>
internal static class WpfPresentationFileCommandSessionFactory
{
    public static PresentationFileCommandSession Create(
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
        PresentationNativePrintHandoffHostCapabilities? nativePrintCapability = null,
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
            videoFramePackageArtifactFactory = null)
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
            nativePrintCapability ?? WpfPresentationPrintService.DetectCapabilities(),
            () => session?.LastNativePrintHandoffPlan);
        var resolvedVideoCapability = videoEncoderCapability ??
            videoExportAdapter?.Capability ??
            DetectVideoEncoderCapability();
        var videoPort = new WpfPresentationVideoPort(
            videoExportAdapter ?? WindowsNativePrintOutput.CreateVideoAdapter(resolvedVideoCapability),
            BuildVideoExportHostCapabilities(resolvedVideoCapability));
        session = PresentationFileCommandSessionFactory.Create(
            new PresentationFileCommandSessionComposition(
                getModel,
                loadModel,
                lifecycle,
                picker,
                render,
                print,
                videoPort,
                new WpfPresentationFileFeedbackPort(workflow),
                getImageExportRange,
                getPrintCurrentSlideNumber,
                getPrintSelectedSlideNumbers,
                VideoPackageArtifactFactory: videoFramePackageArtifactFactory,
                ConfirmExternallyModifiedOverwriteAsync: (path, ct) =>
                    workflow.ConfirmExternallyModifiedOverwriteAsync(path, ct).AsTask()));
        return session;
    }

    private static PresentationVideoExportHandoffHostCapabilities BuildVideoExportHostCapabilities(
        LinuxVideoEncoderCapability capability) =>
        PresentationNativeCommandOutcomePlanner.BuildVideoExportHostCapabilities(
            string.Equals(
                capability.ExecutablePath,
                WindowsNativeVideoExportAdapter.ExecutablePath,
                StringComparison.Ordinal)
                ? PresentationVideoExportHostProfile.WpfWindows
                : PresentationVideoExportHostProfile.Wpf,
            capability.CanEncodeMp4,
            capability.CanCaptureNarration,
            capability.CanCaptureCameraAndMedia,
            capability.CanMuxTimedCaptions,
            capability.Reason);

    private static LinuxVideoEncoderCapability DetectVideoEncoderCapability() =>
        OperatingSystem.IsWindows()
            ? WindowsNativePrintOutput.Detect().Video
            : new LinuxNativeOutputCapabilityDetector(
                    new PathLinuxRecordingExecutableLocator(),
                    new SystemLinuxRecordingProbeRunner())
                .Detect(canCaptureNarrationOverride: false)
                .Video;
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
        PresentationNativePrintHandoffHostCapabilities capability,
        Func<PresentationNativePrintHandoffPlan?> getLastHandoffPlan)
    {
        _owner = owner;
        _getLastHandoffPlan = getLastHandoffPlan ?? throw new ArgumentNullException(nameof(getLastHandoffPlan));
        Capabilities = capability;
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

        var printResult = WpfPresentationPrintService.ShowPrintDialogAndPrint(
            presentation,
            request,
            handoffPlan.SuggestedPrintJobName,
            _owner);
        return Task.FromResult(printResult.Outcome switch
        {
            WpfPaginatorPrintOutcome.Printed => PresentationNativePrintPortResult.Success(
                PresentationNativePrintStatusProfile.PresentationDialog),
            WpfPaginatorPrintOutcome.Cancelled => PresentationNativePrintPortResult.Cancel(
                PresentationNativePrintStatusProfile.PresentationDialog),
            WpfPaginatorPrintOutcome.Failed => PresentationNativePrintPortResult.Failure(
                PresentationNativePrintStatusProfile.PresentationDialog,
                printResult.Error?.Message),
            _ => throw new ArgumentOutOfRangeException(
                nameof(printResult),
                printResult.Outcome,
                "Unsupported WPF print outcome."),
        });
    }
}

internal sealed class WpfPresentationVideoPort : IPresentationVideoPort
{
    private readonly PresentationVideoExportSession _session;

    public WpfPresentationVideoPort(
        ILinuxVideoExportAdapter adapter,
        PresentationVideoExportHandoffHostCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _session = new PresentationVideoExportSession(() => adapter);
        Capabilities = capabilities;
    }

    public PresentationVideoExportHandoffHostCapabilities Capabilities { get; }
    public LinuxVideoExportResult? LastResult => _session.LastResult;

    public async Task<PresentationNativeCommandResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
        CancellationToken cancellationToken)
        => await _session.ExportAsync(
            package,
            outputPath,
            recordingMediaArtifacts,
            cancellationToken).ConfigureAwait(true);
}

internal sealed class WpfPresentationFileFeedbackPort : IPresentationFileCommandFeedbackPort
{
    private readonly SisterWpfFileCommandWorkflow _workflow;

    // r139-remediation: captured at construction time, which production always does on the UI
    // thread (WpfPresentationFileCommandSessionFactory.Create runs inside MainWindow's
    // constructor). This is the thread whose Window/MessageBox calls are legal -- see ReportAsync.
    private readonly Dispatcher _dispatcher;

    public WpfPresentationFileFeedbackPort(SisterWpfFileCommandWorkflow workflow)
    {
        _workflow = workflow;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // r139-remediation (sweep78-1 regression): ExportPdfArtifactAsync's ConfigureAwait(false)
        // chain can resume this call on a thread-pool thread while the UI thread sits blocked
        // inside RunFileCommand's `command.GetAwaiter().GetResult()` -- a plain synchronous wait
        // that does NOT pump the Dispatcher queue. WPF Window/MessageBox access is thread-affine
        // (DispatcherObject.VerifyAccess), so calling ReportOnUiThread from the wrong thread threw
        // InvalidOperationException ("a different thread owns it") deep inside MessageBox.Show,
        // turning a SUCCESSFUL export into a visible crash.
        //
        // Adding ConfigureAwait(false) here would NOT fix it -- that only controls where the
        // *continuation* resumes, not which thread touches the Window. A *blocking* marshal
        // (Dispatcher.Invoke) would not fix it either -- it would deadlock, because the blocked UI
        // thread never pumps the queue Invoke waits on. So: queue the work with BeginInvoke (does
        // not block) and return immediately; the feedback runs once the UI thread's message loop
        // is free again, which is exactly when GetResult() unblocks. When this is called ON the UI
        // thread already (the ordinary synchronous-completion case for New/Open/Save), run
        // directly so behavior there is unchanged -- CheckAccess() makes both branches safe to
        // call from either thread.
        if (_dispatcher.CheckAccess())
        {
            ReportOnUiThread(result);
        }
        else
        {
            _dispatcher.BeginInvoke(() => ReportOnUiThread(result));
        }

        return Task.CompletedTask;
    }

    private void ReportOnUiThread(PresentationFileCommandResult result)
    {
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
                result.Message ?? PresentationNativeCommandOutcomePlanner.ExportCompletedStatus,
                result.ImageDiagnostics);
        }
    }
}
