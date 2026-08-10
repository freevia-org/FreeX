using System.IO;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFileCommandSessionTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("freep-file-session-");
    private string TempDirectory => _temporaryDirectory.Path;

    [Fact]
    public async Task SaveAsAndOpen_UseSharedPersistenceAndLifecycleState()
    {
        var selectedPath = Path.Combine(TempDirectory, "Quarterly Review");
        var lifecycle = new FakeLifecyclePort();
        var picker = new FakePickerPort
        {
            SaveResult = PresentationFilePickerResult.Selected(selectedPath),
        };
        var original = Presentation.CreateEmpty();
        var loaded = original;
        var session = CreateSession(() => original, value => loaded = value, lifecycle, picker);

        var save = await session.SaveAsAsync();

        var resolvedPath = selectedPath + ".pptx";
        save.Status.Should().Be(PresentationFileCommandStatus.Succeeded);
        save.Operation.Status.Should().Be(OperationStatus.Completed);
        save.Path.Should().Be(resolvedPath);
        save.Message.Should().Be("Saved Quarterly Review.pptx");
        File.Exists(resolvedPath).Should().BeTrue();
        lifecycle.CurrentPath.Should().Be(resolvedPath);
        lifecycle.IsDirty.Should().BeFalse();
        picker.LastSaveRequest!.Title.Should().Be(PresentationFileTextResources.Presentation.SavePickerTitle);

        lifecycle.MarkDirty();
        var open = await session.OpenPathAsync(resolvedPath);

        open.Status.Should().Be(PresentationFileCommandStatus.Succeeded);
        open.Message.Should().Be("Opened Quarterly Review.pptx");
        loaded.Should().NotBeSameAs(original);
        lifecycle.CurrentPath.Should().Be(resolvedPath);
        lifecycle.IsDirty.Should().BeFalse();
        session.LastError.Should().BeNull();
    }

    [Fact]
    public async Task NewAndPickerCancellation_PreserveWorkflowOutcomes()
    {
        var lifecycle = new FakeLifecyclePort { AllowNew = false };
        var picker = new FakePickerPort { OpenResult = PresentationFilePickerResult.Cancelled };
        var original = Presentation.CreateEmpty();
        var loaded = original;
        var session = CreateSession(() => original, value => loaded = value, lifecycle, picker);

        var cancelledNew = await session.NewAsync();
        var cancelledOpen = await session.OpenAsync();

        cancelledNew.Status.Should().Be(PresentationFileCommandStatus.Cancelled);
        cancelledNew.Operation.Status.Should().Be(OperationStatus.Cancelled);
        loaded.Should().BeSameAs(original);
        lifecycle.LastAction.Should().Be(PresentationFileTextResources.Presentation.OpenAction);
        cancelledOpen.Status.Should().Be(PresentationFileCommandStatus.Cancelled);
        session.LastResult.Should().BeSameAs(cancelledOpen);
        picker.LastOpenRequest!.Title.Should().Be(PresentationFileTextResources.Presentation.OpenPickerTitle);
    }

    [Fact]
    public async Task InvalidOpen_RecordsValidationErrorAndReportsFeedback()
    {
        var lifecycle = new FakeLifecyclePort();
        var picker = new FakePickerPort();
        var feedback = new FakeFeedbackPort();
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            lifecycle,
            picker,
            feedback: feedback);
        var missingPath = Path.Combine(TempDirectory, "missing", "missing.pptx");

        var result = await session.OpenPathAsync(missingPath);

        result.Status.Should().Be(PresentationFileCommandStatus.Failed);
        result.Operation.Status.Should().Be(OperationStatus.Failed);
        result.Operation.Error!.Detail.Should().Be("Could not open the presentation");
        result.Operation.Validation!.Detail.Should().Be(result.Error!.Exception.Message);
        result.Validation.IsValid.Should().BeFalse();
        result.Error!.Summary.Should().Be("Could not open the presentation");
        result.Path.Should().Be(missingPath);
        session.LastError.Should().Be(result.Error);
        feedback.Results.Should().ContainSingle().Which.Should().BeSameAs(result);
    }

    [Fact]
    public async Task ExportCommands_OrchestratePortableRenderAndNativeVideoPorts()
    {
        var pdfPath = Path.Combine(TempDirectory, "deck.pdf");
        var imageDirectory = Path.Combine(TempDirectory, "images");
        var videoPath = Path.Combine(TempDirectory, "deck.mp4");
        var lifecycle = new FakeLifecyclePort();
        var picker = new FakePickerPort
        {
            SaveResult = PresentationFilePickerResult.Selected(pdfPath),
            FolderResult = PresentationFilePickerResult.Selected(imageDirectory),
        };
        var render = new FakeRenderPort();
        var video = new FakeVideoPort();
        var presentation = Presentation.CreateEmpty();
        var session = CreateSession(
            () => presentation,
            _ => { },
            lifecycle,
            picker,
            render,
            video: video);

        var pdf = await session.ExportPdfAsync();
        var images = await session.ExportImagesAsync();
        picker.SaveResult = PresentationFilePickerResult.Selected(videoPath);
        var exportedVideo = await session.ExportVideoAsync(new PresentationVideoExportRequest(
            SecondsPerSlide: 0.1,
            UseRecordedTimings: false,
            IncludeNarration: false));

        pdf.Succeeded.Should().BeTrue();
        File.ReadAllBytes(pdfPath).Should().Equal(FakeRenderPort.PdfBytes);
        images.Succeeded.Should().BeTrue();
        session.LastImageExportResult!.ExportedSlides.Should().ContainSingle();
        File.Exists(session.LastImageExportResult.ExportedSlides[0].Path).Should().BeTrue();
        exportedVideo.Succeeded.Should().BeTrue();
        video.ExportCount.Should().Be(1);
        video.OutputPath.Should().Be(videoPath);
        session.LastVideoFramePackage.Should().NotBeNull();
        render.RenderCount.Should().BeGreaterThanOrEqualTo(3);
        lifecycle.CurrentPath.Should().BeNull();
    }

    [Fact]
    public async Task Print_DelegatesNativeExecutionAndRetainsPackageState()
    {
        var print = new FakePrintPort();
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            new FakePickerPort(),
            print: print);
        var request = new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);

        var result = await session.PrintAsync(request);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("Printed presentation");
        print.PrintCount.Should().Be(1);
        print.Request.Should().Be(request);
        session.LastPrintOutputPackage.Should().NotBeNull();
        session.LastPrintExecutionDescriptor.Should().NotBeNull();
    }

    [Fact]
    public void Result_and_picker_compatibility_types_project_the_shared_outcome()
    {
        var invalid = PresentationFileCommandResult.Invalid(
            PresentationFileCommand.SaveAs,
            "Could not save the presentation",
            "Unsupported path",
            "deck.unsupported");
        var unavailable = PresentationFileCommandResult.Unavailable(
            PresentationFileCommand.ExportVideo,
            "No encoder");
        var nonLocal = PresentationFilePickerResult.NonLocal("A local path is required");

        invalid.Status.Should().Be(PresentationFileCommandStatus.Invalid);
        invalid.Operation.Status.Should().Be(OperationStatus.ValidationFailed);
        invalid.Operation.Validation!.Detail.Should().Be("Unsupported path");
        invalid.Operation.Error!.Detail.Should().Be("Could not save the presentation");
        invalid.Validation.FailureReason.Should().Be("Unsupported path");
        invalid.Error!.Summary.Should().Be("Could not save the presentation");
        invalid.Path.Should().Be("deck.unsupported");

        unavailable.Status.Should().Be(PresentationFileCommandStatus.Unavailable);
        unavailable.Operation.Status.Should().Be(OperationStatus.Unavailable);
        unavailable.Validation.IsValid.Should().BeTrue();
        unavailable.Error.Should().BeNull();

        nonLocal.Status.Should().Be(PresentationFilePickerStatus.NonLocalSelection);
        nonLocal.Operation.Status.Should().Be(OperationStatus.ValidationFailed);
        nonLocal.Operation.Validation!.Detail.Should().Be("A local path is required");
        nonLocal.Message.Should().Be("A local path is required");
    }

    public void Dispose() => _temporaryDirectory.Dispose();

    private static PresentationFileCommandSession CreateSession(
        Func<Presentation> getPresentation,
        Action<Presentation> loadPresentation,
        FakeLifecyclePort lifecycle,
        FakePickerPort picker,
        FakeRenderPort? render = null,
        FakePrintPort? print = null,
        FakeVideoPort? video = null,
        FakeFeedbackPort? feedback = null) =>
        new(
            getPresentation,
            loadPresentation,
            lifecycle,
            picker,
            render ?? new FakeRenderPort(),
            print ?? new FakePrintPort(),
            video ?? new FakeVideoPort(),
            feedback);

    private sealed class FakeLifecyclePort : IPresentationFileLifecyclePort
    {
        public bool AllowNew { get; set; } = true;
        public bool IsDirty { get; private set; }
        public int DirtyGeneration { get; private set; }
        public string? CurrentPath { get; private set; }
        public string? CurrentFileName => Path.GetFileName(CurrentPath);
        public string DisplayName => CurrentFileName ?? "Presentation";
        public IReadOnlyList<RecentFileEntry> RecentEntries { get; } = [];
        public string? LastAction { get; private set; }

        public void MarkDirty()
        {
            IsDirty = true;
            DirtyGeneration++;
        }

        public void MarkSavedWithoutPath()
        {
            CurrentPath = null;
            IsDirty = false;
        }

        public void MarkSavedWithPath(string path, bool suppressRecentFiles)
        {
            CurrentPath = path;
            IsDirty = false;
        }

        public async Task<bool> NewAsync(string action, Func<Task> loadNewPresentationAsync)
        {
            LastAction = action;
            if (!AllowNew)
                return false;
            await loadNewPresentationAsync();
            MarkSavedWithoutPath();
            return true;
        }

        public async Task<bool> OpenAsync(
            string action,
            Func<Task<string?>> pickPathAsync,
            Func<string, Task<bool>> openPathAsync)
        {
            LastAction = action;
            var path = await pickPathAsync();
            return path is not null && await openPathAsync(path);
        }

        public Task<bool> SaveAsync(
            Func<string, Task<bool>> saveToCurrentPathAsync,
            Func<Task<bool>> saveAsAsync) =>
            CurrentPath is { } path ? saveToCurrentPathAsync(path) : saveAsAsync();

        public Task<bool> ConfirmCloseAllowedAsync(string action)
        {
            LastAction = action;
            return Task.FromResult(true);
        }
    }

    private sealed class FakePickerPort : IPresentationFilePickerPort
    {
        public PresentationFilePickerResult OpenResult { get; set; } = PresentationFilePickerResult.Cancelled;
        public PresentationFilePickerResult SaveResult { get; set; } = PresentationFilePickerResult.Cancelled;
        public PresentationFilePickerResult FolderResult { get; set; } = PresentationFilePickerResult.Cancelled;
        public PresentationFileOpenPickerRequest? LastOpenRequest { get; private set; }
        public PresentationFileSavePickerRequest? LastSaveRequest { get; private set; }

        public Task<PresentationFilePickerResult> PickOpenFileAsync(
            PresentationFileOpenPickerRequest request,
            CancellationToken cancellationToken)
        {
            LastOpenRequest = request;
            return Task.FromResult(OpenResult);
        }

        public Task<PresentationFilePickerResult> PickSaveFileAsync(
            PresentationFileSavePickerRequest request,
            CancellationToken cancellationToken)
        {
            LastSaveRequest = request;
            return Task.FromResult(SaveResult);
        }

        public Task<PresentationFilePickerResult> PickFolderAsync(
            PresentationFolderPickerRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(FolderResult);
    }

    private sealed class FakeRenderPort : IPresentationFileRenderPort
    {
        public static readonly byte[] PdfBytes = "%PDF-session-test"u8.ToArray();

        public int RenderCount { get; private set; }
        public PresentationSlideImageRenderer RenderSlideToPng => Render;
        public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
            (presentation, slideIndex, widthPx, heightPx, _) => Render(presentation, slideIndex, widthPx, heightPx);
        public PresentationRasterPdfWriter WriteRasterPdf => _ => PdfBytes;
        public PresentationPdfContentWriter WriteVectorPdf => _ => PdfBytes;

        private byte[] Render(Presentation presentation, int slideIndex, int widthPx, int heightPx)
        {
            RenderCount++;
            return [0x89, 0x50, 0x4E, 0x47];
        }
    }

    private sealed class FakePrintPort : IPresentationPrintPort
    {
        public PresentationNativePrintHandoffHostCapabilities Capabilities { get; } =
            PresentationNativePrintHandoffHostCapabilities.Available("test print host");
        public int PrintCount { get; private set; }
        public PresentationPrintRequest? Request { get; private set; }

        public Task<PresentationNativePrintPortResult> PrintAsync(
            Presentation presentation,
            PresentationPrintRequest request,
            Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
            CancellationToken cancellationToken)
        {
            PrintCount++;
            Request = request;
            buildPackage(request);
            return Task.FromResult(PresentationNativePrintPortResult.Success(
                PresentationNativePrintStatusProfile.PresentationDialog));
        }
    }

    private sealed class FakeVideoPort : IPresentationVideoPort
    {
        public PresentationVideoExportHandoffHostCapabilities Capabilities { get; } = new(
            "test video host",
            CanEncodeMp4: true,
            CanCaptureNarration: false,
            CanCaptureCameraAndMedia: false,
            UnavailableReason: string.Empty);
        public int ExportCount { get; private set; }
        public string? OutputPath { get; private set; }

        public Task<PresentationNativeCommandResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
            CancellationToken cancellationToken)
        {
            ExportCount++;
            OutputPath = outputPath;
            return Task.FromResult(PresentationNativeCommandResult.Success("Exported video"));
        }
    }

    private sealed class FakeFeedbackPort : IPresentationFileCommandFeedbackPort
    {
        public List<PresentationFileCommandResult> Results { get; } = [];

        public Task ReportAsync(PresentationFileCommandResult result, CancellationToken cancellationToken)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }
    }
}
