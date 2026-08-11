using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Pdf;
using Free.Shared.Pdf.Wpf;
using Free.Shared.Pdf.Skia;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Recording.Windows;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Microsoft.Win32;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's File lifecycle: New / Open / Save / Save As / Close over native <c>.pptx</c> packages.
///
/// <para>
/// The file-lifecycle <em>ceremony</em> — the dirty-gate before destructive actions, the Save-vs-Save-As
/// resolution, and recent-files registration — is decided by the shared, neutral
/// <see cref="FileLifecyclePlanner"/>. FreeP supplies only the thin host side: the native
/// <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/> for <c>.pptx</c> plus legacy <c>.fxp</c> compatibility
/// (via the shared <see cref="FileDialogRequestPlanner"/>), the actual read/write, and the message prompts. Dirty/path
/// state and lifecycle ceremony live in the shared <see cref="FileCommandWorkflow"/>; recent files in the
/// shared <see cref="RecentFilesStore"/>. Mirrors FreeW.FileCommands exactly (FreeW already adopted these seams).
/// </para>
///
/// <para>
/// FreeP has no live editor surface yet, so the host exposes the current model through a getter and accepts a
/// freshly loaded model through a loader callback (the placeholder canvas re-renders on change). The next
/// session swaps these seams for a real slide editor.
/// </para>
/// </summary>
internal sealed class FileCommands
{
    private readonly Window _window;
    private readonly Func<Presentation> _getModel;
    private readonly Action<Presentation> _loadModel;
    private readonly SisterWpfFileCommandWorkflow _workflow;
    private readonly FreePOptions _options;
    private readonly Func<PresentationSlideRangeRequest?> _getImageExportRange;
    private readonly Func<int?> _getPrintCurrentSlideNumber;
    private readonly Func<IReadOnlyList<int>?> _getPrintSelectedSlideNumbers;

    private readonly PresentationNativePrintHandoffHostCapabilities _nativePrintHostCapabilities;

    private readonly WpfVideoExportAdapter _videoExportAdapter;
    private readonly PresentationVideoExportHandoffHostCapabilities _videoExportHostCapabilities;

    private static readonly FileOpenDialogPlan OpenDialogPlan =
        PresentationFileDialogPlanner.BuildOpenDialogPlan();

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
        WpfVideoEncoderCapability? videoEncoderCapability = null,
        WpfVideoExportAdapter? videoExportAdapter = null,
        WpfNativePrintCapability? nativePrintCapability = null)
    {
        _window = window;
        _getModel = getModel;
        _loadModel = loadModel;
        _options = options ?? new FreePOptions();
        _getImageExportRange = getImageExportRange ?? (() => null);
        _getPrintCurrentSlideNumber = getPrintCurrentSlideNumber ?? (() => null);
        _getPrintSelectedSlideNumbers = getPrintSelectedSlideNumbers ?? (() => null);
        var resolvedPrintCapability = nativePrintCapability ?? WpfNativePrintCapabilityDetector.Detect();
        _nativePrintHostCapabilities = resolvedPrintCapability.CanPrint
            ? PresentationNativePrintHandoffHostCapabilities.Available("WPF print host")
            : PresentationNativePrintHandoffHostCapabilities.Deferred(
                "WPF print host",
                resolvedPrintCapability.Reason);
        var resolvedVideoCapability = videoEncoderCapability ??
            videoExportAdapter?.Capability ??
            WpfVideoEncoderCapabilityDetector.Detect();
        _videoExportAdapter = videoExportAdapter ?? new WpfVideoExportAdapter(resolvedVideoCapability);
        _videoExportHostCapabilities = BuildVideoExportHostCapabilities(resolvedVideoCapability);
        _workflow = new SisterWpfFileCommandWorkflow(
            "FreeP",
            () => _options.RecentFilesCap,
            onChanged,
            Save,
            loadRecentFilesStore,
            messageService);
    }

    public bool IsDirty => _workflow.IsDirty;

    public string? CurrentPath => _workflow.CurrentPath;

    public string DisplayName => _workflow.DisplayName;

    public PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }

    public PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }

    public PresentationNativePrintHandoffPlan? LastNativePrintHandoffPlan { get; private set; }

    public PresentationPrintOutputPackageExecutionDescriptor? LastPrintExecutionDescriptor { get; private set; }

    public bool CanPrint => _nativePrintHostCapabilities.CanOpenNativePrintDialog;

    public PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }

    public PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan { get; private set; }

    public WpfVideoExportResult? LastVideoExportResult { get; private set; }

    public bool CanExportVideo => _videoExportHostCapabilities.CanEncodeMp4;

    public PresentationVideoFramePackageExecutionDescriptor? LastVideoExecutionDescriptor { get; private set; }

    public void MarkDirty()
    {
        _workflow.MarkDirty();
    }

    /// <summary>File &gt; New. Dirty-gates so unsaved work is not silently lost. Returns false on cancel.</summary>
    public bool New() =>
        _workflow.New("creating a new presentation", () => _loadModel(Presentation.CreateEmpty()));

    /// <summary>File &gt; Open. Dirty-gates, then shows the open dialog and loads the chosen file.</summary>
    public bool Open() =>
        _workflow.Open("opening another presentation", PromptOpenPath, OpenPath);

    /// <summary>Loads a specific path (recent-files click / startup). Does NOT dirty-gate.</summary>
    public bool OpenPath(string path) => OpenPath(path, suppressRecentFiles: false);

    private bool OpenPath(string path, bool suppressRecentFiles)
    {
        try
        {
            var result = PresentationFilePersistenceWorkflow.Open(path);
            _loadModel(result.Presentation);
            SetSaved(result.SavedPath, suppressRecentFiles || result.SuppressRecentFiles);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not open the presentation", ex);
            return false;
        }
    }

    /// <summary>Recent files (most recent first) from the shared store; never throws.</summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    /// <summary>File &gt; Save. Resolves Save-vs-Save-As via the shared planner.</summary>
    public bool Save() => _workflow.Save(SaveTo, SaveAs);

    /// <summary>File &gt; Save As. Always prompts for a target.</summary>
    public bool SaveAs()
    {
        var plan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        if (!result.Chosen)
            return false;

        if (!PresentationFileDialogPlanner.TryResolveSavePickerPath(result.FileName!, out var resolvedPath))
        {
            ShowError(
                "Could not save the presentation",
                new InvalidDataException(PresentationFileDialogPlanner.UnsupportedSavePathMessage));
            return false;
        }

        return SaveTo(resolvedPath);
    }

    /// <summary>
    /// File &gt; Export to PDF. Prompts for a target and writes a fixed-layout PDF (one raster page per slide)
    /// through the shared raster export route. Does not change the dirty/saved state (the presentation document is the source of record).
    /// </summary>
    public bool ExportPdf()
    {
        var plan = PresentationExportPlanner.BuildPdfExportDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, plan);
        if (!result.Chosen)
            return false;

        try
        {
            var imageDiagnostics = new List<string>();
            var bytes = ExportPdfRasterBytes(_getModel(), imageDiagnostics);
            ExportAtomicWriter.WriteAllBytes(result.FileName!, bytes);
            _workflow.ShowExportImageWarnings("Exported to PDF", imageDiagnostics);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation to PDF", ex);
            return false;
        }
    }

    /// <summary>
    /// Renders every slide to a raster PDF exactly as <see cref="ExportPdf"/> does, and appends into
    /// <paramref name="imageDiagnostics"/> both loss points that can occur along the way:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="WpfRasterPdfWriter.WriteToBytes(Free.Shared.Pdf.PdfRasterDocument, ICollection{string}?)"/>
    /// reports a page whose already-composited slide PNG cannot be decoded (rare in practice -- that
    /// PNG is one <see cref="WpfPresentationSlideImageRenderer"/> just encoded, so it is always
    /// well-formed).
    /// </description></item>
    /// <item><description>
    /// The <see cref="SlideImageRenderDiagnostics"/> capture installed around the render reports a
    /// picture <see cref="FreeP.App.Rendering.Wpf.SlideCanvas"/> dropped one layer further down, while
    /// compositing the slide itself -- the actual loss point for an undecodable embedded picture, which
    /// the writer-level diagnostics above can never observe on their own. Internal (not private) so
    /// FreeP.App.Host.Tests can exercise this exact production composition.
    /// </description></item>
    /// </list>
    /// </summary>
    internal static byte[] ExportPdfRasterBytes(Presentation presentation, List<string> imageDiagnostics)
    {
        using (SlideImageRenderDiagnostics.Capture(imageDiagnostics))
        {
            return PresentationRasterPdfExporter.ExportToBytes(
                presentation,
                request: null,
                WpfPresentationSlideImageRenderer.RenderSlideToPng,
                document => WpfRasterPdfWriter.WriteToBytes(document, imageDiagnostics));
        }
    }

    /// <summary>
    /// File &gt; Export &gt; Notes Page PDF. Prompts for a target and writes one speaker-notes page per selected slide.
    /// </summary>
    public bool ExportNotesPagePdf(PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        var exportPlan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(range, presentation.Slides.Count);
        if (!exportPlan.CanExecute)
            return false;

        var savePlan = PresentationExportPlanner.BuildNotesPagePdfExportDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, savePlan);
        if (!result.Chosen)
            return false;

        try
        {
            var request = new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range));
            // Populated by the shared writer when an embedded picture's bytes cannot be decoded
            // (corrupt or an unrecognized format), so that loss is surfaced instead of the export
            // looking clean. SkiaPdfWriter.WriteToBytesWithPortableFallback is deliberately kept
            // single-parameter (see its doc comment) so this replicates its Skia/portable fallback
            // here to forward diagnostics through.
            var imageDiagnostics = new List<string>();
            var bytes = PresentationNotesPagePdfExporter.ExportToBytes(
                presentation,
                request,
                document => WriteVectorPdfWithPortableFallback(document, imageDiagnostics));
            ExportAtomicWriter.WriteAllBytes(result.FileName!, bytes);
            _workflow.ShowExportImageWarnings("Exported to PDF", imageDiagnostics);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation notes pages to PDF", ex);
            return false;
        }
    }

    /// <summary>
    /// Mirrors <see cref="SkiaPdfWriter.WriteToBytesWithPortableFallback"/>'s Skia-then-portable
    /// fallback, but forwards <paramref name="imageDiagnostics"/> through to whichever writer actually
    /// runs. WriteToBytesWithPortableFallback itself is deliberately kept single-parameter (bound as a
    /// bare method group elsewhere), so callers that need diagnostics reproduce its logic here instead.
    /// Internal (not private) so FreeP.App.Host.Tests can exercise the exact delegate
    /// <see cref="ExportNotesPagePdf"/> passes to <c>PresentationNotesPagePdfExporter.ExportToBytes</c>.
    /// </summary>
    internal static byte[] WriteVectorPdfWithPortableFallback(
        Free.Shared.Pdf.PdfContentDocument document,
        List<string> imageDiagnostics)
    {
        try
        {
            return SkiaPdfWriter.WriteToBytes(document, imageDiagnostics);
        }
        catch (Exception ex) when (SkiaPdfAvailabilityHelper.IsSkiaUnavailable(ex))
        {
            imageDiagnostics.Clear();
            return PortablePdfWriter.WriteToBytes(document, imageDiagnostics: imageDiagnostics);
        }
    }

    /// <summary>
    /// File &gt; Export &gt; Images. Prompts for a folder, then exports the host-selected slide range.
    /// </summary>
    public bool ExportImages()
    {
        var outputDirectory = PromptImageExportFolder();
        return outputDirectory is not null && ExportImagesToFolder(outputDirectory, _getImageExportRange());
    }

    /// <summary>
    /// Exports one PNG per requested slide to an already chosen folder. The host owns folder picking;
    /// shared code owns PowerPoint-style range policy, naming, and atomic writes.
    /// </summary>
    public bool ExportImagesToFolder(string outputDirectory, PresentationSlideRangeRequest? range = null)
    {
        try
        {
            var imageDiagnostics = new List<string>();
            ExportImagesToFolderCore(
                _getModel(),
                new PresentationImageExportRequest(
                    outputDirectory,
                    BaseFileName: Path.GetFileNameWithoutExtension(_workflow.CurrentFileName),
                    SlideRange: range),
                imageDiagnostics);
            _workflow.ShowExportImageWarnings("Exported the presentation slides to images", imageDiagnostics);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not export the presentation slides to images", ex);
            return false;
        }
    }

    /// <summary>
    /// Renders every requested slide to a PNG exactly as <see cref="ExportImagesToFolder"/> does, and
    /// appends into <paramref name="imageDiagnostics"/> any picture <see cref="FreeP.App.Rendering.Wpf.SlideCanvas"/>
    /// drops while compositing a slide -- the same loss point <see cref="ExportPdfRasterBytes"/> surfaces for
    /// the raster PDF path (see <see cref="SlideImageRenderDiagnostics"/>), but was previously unwired here,
    /// so an undecodable embedded picture silently disappeared from File &gt; Export &gt; Images with no
    /// diagnostic at all. Internal (not private) so FreeP.App.Host.Tests can exercise this exact production
    /// composition.
    /// </summary>
    internal static PresentationImageExportResult ExportImagesToFolderCore(
        Presentation presentation,
        PresentationImageExportRequest request,
        List<string> imageDiagnostics)
    {
        using (SlideImageRenderDiagnostics.Capture(imageDiagnostics))
        {
            return PresentationImageExportExecutor.Export(
                presentation,
                request,
                WpfPresentationSlideImageRenderer.RenderSlideToPng);
        }
    }

    /// <summary>
    /// Builds the shared PowerPoint-style handout page plan. WPF owns native print/preview UI later;
    /// page slots and range policy stay in the shared presentation planner.
    /// </summary>
    public PresentationHandoutLayoutPlan BuildHandoutLayoutPlan(
        int? slidesPerPage = null,
        PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        return PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                range,
                HandoutSlidesPerPage: slidesPerPage),
            presentation,
            presentation.SlideSizeCxEmu,
            presentation.SlideSizeCyEmu);
    }

    /// <summary>
    /// Builds the shared notes-page PDF render plan. WPF owns native print/export UI later;
    /// notes-page range, geometry, slide thumbnail placement, and speaker-note drawing stay shared.
    /// </summary>
    public PresentationNotesPagePdfRenderPlan BuildNotesPagePdfRenderPlan(
        PresentationSlideRangeRequest? range = null)
    {
        var presentation = _getModel();
        return PresentationNotesPagePdfExporter.BuildRenderPlan(
            presentation,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range)));
    }

    /// <summary>
    /// Builds the shared printable PDF package. WPF native print dialog handoff remains a later host shell step.
    /// </summary>
    public PresentationPrintOutputPackage BuildPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        LastPrintOutputPackage = PresentationPrintOutputPackageExecutor.BuildPackage(
            _getModel(),
            request,
            WpfPresentationSlideImageRenderer.RenderSlideToPng,
            WpfRasterPdfWriter.WriteToBytes,
            SkiaPdfWriter.WriteToBytesWithPortableFallback,
            WpfPresentationSlideImageRenderer.RenderSlideToPngWithPrintMarkup);
        LastPrintExecutionDescriptor = PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(
            LastPrintOutputPackage,
            _nativePrintHostCapabilities,
            _workflow.CurrentFileName);
        LastNativePrintHandoffPlan = LastPrintExecutionDescriptor.HandoffPlan;
        return LastPrintOutputPackage;
    }

    /// <summary>
    /// Builds the shared native print handoff state over an already planned/created printable package.
    /// </summary>
    public PresentationNativePrintHandoffPlan BuildNativePrintHandoffPlan(
        PresentationPrintOutputPackagePlan packagePlan,
        PresentationNativePrintHandoffHostCapabilities? hostCapabilities = null)
    {
        LastNativePrintHandoffPlan = PresentationPrintOutputPackageExecutor.BuildNativePrintHandoffPlan(
            packagePlan,
            hostCapabilities ?? _nativePrintHostCapabilities,
            _workflow.CurrentFileName);
        return LastNativePrintHandoffPlan;
    }

    /// <summary>
    /// Produces the shared printable package and records the native host handoff state without opening a print dialog.
    /// </summary>
    public PresentationNativePrintHandoffPlan ExecuteNativePrintHandoff(PresentationPrintRequest? request = null)
    {
        BuildPrintOutputPackage(request);
        return LastPrintExecutionDescriptor!.HandoffPlan;
    }

    /// <summary>
    /// Builds the shared PowerPoint-style Backstage Print pane model without opening a native print dialog.
    /// </summary>
    public PresentationPrintBackstagePlan BuildPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        var presentation = _getModel();
        LastPrintBackstagePlan = PresentationPrintBackstagePlanner.Build(
            request,
            presentation,
            _getPrintCurrentSlideNumber(),
            _getPrintSelectedSlideNumbers(),
            _nativePrintHostCapabilities,
            _workflow.CurrentFileName);
        return LastPrintBackstagePlan;
    }

    /// <summary>
    /// Executes the selected PowerPoint-style print layout through the WPF native printer dialog.
    /// The shared layout plan owns page selection; WPF owns only raster-page preparation and printer submission.
    /// </summary>
    public bool Print(PresentationPrintRequest? request = null)
    {
        if (!CanPrint)
        {
            ShowError(
                "Could not print the presentation",
                new InvalidOperationException(_nativePrintHostCapabilities.UnavailableReason ??
                    "No native WPF printer is available."));
            return false;
        }

        var normalizedRequest = request ?? new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);
        try
        {
            BuildPrintOutputPackage(normalizedRequest);
            if (LastPrintExecutionDescriptor?.Validation.IsValid != true)
                throw new InvalidOperationException(
                    LastPrintExecutionDescriptor?.DisabledReason ??
                    PresentationPrintOutputPackageExecutor.InvalidPackageReason);

            return WpfPresentationPrintService.ShowPrintDialogAndPrint(
                _getModel(),
                normalizedRequest,
                _window);
        }
        catch (Exception ex)
        {
            ShowError("Could not print the presentation", ex);
            return false;
        }
    }

    /// <summary>
    /// Diagnostics collected the last time <see cref="BuildVideoFramePackage"/> rendered slide frames --
    /// pictures <see cref="FreeP.App.Rendering.Wpf.SlideCanvas"/> dropped while compositing a frame (see
    /// <see cref="SlideImageRenderDiagnostics"/>). Reset on every call, so it always reflects only the most
    /// recent build.
    /// </summary>
    internal IReadOnlyList<string> LastVideoFrameImageDiagnostics { get; private set; } = [];

    /// <summary>
    /// Builds the shared PowerPoint-style video frame package. WPF supplies the slide raster callback;
    /// native MP4 execution is performed by <see cref="ExportVideoAsync"/> through the detected host adapter.
    /// </summary>
    public PresentationVideoFramePackage BuildVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        var imageDiagnostics = new List<string>();
        LastVideoFramePackage = BuildVideoFramePackageCore(
            _getModel(),
            request,
            _videoExportHostCapabilities,
            imageDiagnostics);
        LastVideoFrameImageDiagnostics = imageDiagnostics;
        LastVideoExecutionDescriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
            LastVideoFramePackage,
            _videoExportHostCapabilities,
            _workflow.CurrentFileName);
        LastVideoExportHandoffPlan = LastVideoExecutionDescriptor.HandoffPlan;
        return LastVideoFramePackage;
    }

    /// <summary>
    /// Renders every video frame exactly as <see cref="BuildVideoFramePackage"/> does, and appends into
    /// <paramref name="imageDiagnostics"/> any picture <see cref="FreeP.App.Rendering.Wpf.SlideCanvas"/> drops
    /// while compositing a frame -- the same loss point <see cref="ExportPdfRasterBytes"/> surfaces for the
    /// raster PDF path (see <see cref="SlideImageRenderDiagnostics"/>), but was previously unwired here, so an
    /// undecodable embedded picture silently disappeared from File &gt; Export &gt; Video with no diagnostic at
    /// all. Internal (not private) so FreeP.App.Host.Tests can exercise this exact production composition.
    /// </summary>
    internal static PresentationVideoFramePackage BuildVideoFramePackageCore(
        Presentation presentation,
        PresentationVideoExportRequest? request,
        PresentationVideoExportHandoffHostCapabilities hostCapabilities,
        List<string> imageDiagnostics)
    {
        using (SlideImageRenderDiagnostics.Capture(imageDiagnostics))
        {
            return PresentationVideoFramePackageExecutor.BuildPackage(
                presentation,
                request,
                WpfPresentationSlideImageRenderer.RenderSlideToPng,
                hostCapabilities);
        }
    }

    public PresentationVideoExportHandoffPlan BuildVideoExportHandoffPlan(
        PresentationVideoFramePackagePlan packagePlan,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        LastVideoExportHandoffPlan = PresentationVideoFramePackageExecutor.BuildHandoffPlan(
            packagePlan,
            hostCapabilities ?? _videoExportHostCapabilities);
        return LastVideoExportHandoffPlan;
    }

    /// <summary>
    /// Builds the shared PowerPoint-style video export workflow plan without rendering frames.
    /// </summary>
    public PresentationVideoExportPlan BuildVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        var presentation = _getModel();
        return PresentationExportPlanner.BuildVideoExportPlan(
            request,
            presentation,
            _videoExportHostCapabilities);
    }

    /// <summary>
    /// Executes the WPF video export command: checks the shared plan, prompts for an MP4 target,
    /// materializes the shared frame package, and invokes the host ffmpeg adapter.
    /// </summary>
    public async Task<bool> ExportVideoAsync(PresentationVideoExportRequest? request = null)
    {
        if (!_videoExportHostCapabilities.CanEncodeMp4)
        {
            ShowError(
                "Could not export the presentation video",
                new InvalidOperationException(_videoExportHostCapabilities.UnavailableReason ??
                    "No WPF MP4 encoder is available."));
            return false;
        }

        var storyboard = PresentationExportPlanner.BuildVideoStoryboardPlan(request, _getModel());
        if (storyboard.Segments.Count == 0)
        {
            ShowError(
                "Could not export the presentation video",
                new InvalidOperationException("Video export requires at least one slide."));
            return false;
        }

        var savePlan = PresentationExportPlanner.BuildVideoExportDialogPlan(_workflow.CurrentFileName);
        var result = WpfFileDialogService.ShowSaveDialog(_window, savePlan);
        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return false;

        try
        {
            BuildVideoFramePackage(request);
            LastVideoExportResult = await _videoExportAdapter.ExportAsync(
                LastVideoFramePackage!,
                result.FileName,
                CancellationToken.None,
                _getModel().RecordingMediaArtifacts).ConfigureAwait(true);
            if (!LastVideoExportResult.Succeeded && !LastVideoExportResult.Canceled &&
                LastVideoExportResult.FailureReason is not null)
            {
                ShowError(
                    "Could not export the presentation video",
                    new InvalidOperationException(LastVideoExportResult.FailureReason));
            }
            else if (LastVideoExportResult.Succeeded)
            {
                _workflow.ShowExportImageWarnings("Exported the presentation video", LastVideoFrameImageDiagnostics);
            }

            return LastVideoExportResult.Succeeded;
        }
        catch (Exception ex)
        {
            LastVideoExportResult = WpfVideoExportResult.Failed(ex.Message, result.FileName);
            ShowError("Could not export the presentation video", ex);
            return false;
        }
    }

    private static PresentationVideoExportHandoffHostCapabilities BuildVideoExportHostCapabilities(
        WpfVideoEncoderCapability capability) =>
        new(
            string.Equals(capability.ExecutablePath, WindowsNativeVideoExportAdapter.ExecutablePath, StringComparison.Ordinal)
                ? "WPF Windows video export host"
                : "WPF video export host",
            capability.CanEncodeMp4,
            capability.CanCaptureNarration,
            capability.CanCaptureCameraAndMedia,
            capability.Reason,
            capability.CanMuxTimedCaptions);

    /// <summary>Save-before-close gate, called from the window's Closing handler.</summary>
    public bool ConfirmCloseAllowed() => _workflow.ConfirmCloseAllowed();

    private bool SaveTo(string path)
    {
        try
        {
            var result = PresentationFilePersistenceWorkflow.Save(path, _getModel());
            SetSaved(result.SavedPath, result.SuppressRecentFiles);
            return true;
        }
        catch (Exception ex)
        {
            ShowError("Could not save the presentation", ex);
            return false;
        }
    }

    private void SetSaved(string? path, bool suppressRecentFiles)
    {
        if (path is null)
            _workflow.MarkSavedWithoutPath();
        else
            _workflow.MarkSavedWithPath(path, suppressRecentFiles);
    }

    private string? PromptOpenPath()
    {
        var result = WpfFileDialogService.ShowOpenDialog(_window, OpenDialogPlan);
        return result.Chosen ? result.FileName : null;
    }

    private string? PromptImageExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = PresentationExportPlanner.ImageExportPickerTitle,
            Multiselect = false,
        };

        var currentDirectory = _workflow.CurrentPath is null
            ? null
            : Path.GetDirectoryName(_workflow.CurrentPath);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
            dialog.InitialDirectory = currentDirectory;

        return dialog.ShowDialog(_window) == true && !string.IsNullOrWhiteSpace(dialog.FolderName)
            ? dialog.FolderName
            : null;
    }

    // ── Host seams (WPF) ─────────────────────────────────────────────────────
    private void ShowError(string summary, Exception ex) =>
        _workflow.ShowError(summary, ex);
}
