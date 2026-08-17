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
    public void StartupPlanner_FiltersToPresentationsAndRoutesAdditionalDecksToNativeWindows()
    {
        var first = Path.Combine(TempDirectory, "first.pptx");
        var second = Path.Combine(TempDirectory, "second.fxp");

        var plan = PresentationStartupOpenPlanner.Plan(
            [Path.Combine(TempDirectory, "ignored.docx"), first, second],
            fileExists: _ => true);

        plan.Entries.Should().Equal(
            new StartupFileOpenEntry(first, OpenInNewWindow: false),
            new StartupFileOpenEntry(second, OpenInNewWindow: true));
    }

    [Fact]
    public async Task StartupSession_UsesFileCommandFailureAndCanDeferNativeFeedback()
    {
        var lifecycle = new FakeLifecyclePort();
        var feedback = new FakeFeedbackPort();
        var commands = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            lifecycle,
            new FakePickerPort(),
            feedback: feedback);
        var startup = new PresentationStartupOpenSession(commands);
        var missingPath = Path.Combine(TempDirectory, "missing.pptx");
        var plan = startup.Plan([missingPath], fileExists: _ => false);

        var result = await startup.ReportFirstUnopenableAsync(plan, reportFeedback: false);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PresentationFileCommandStatus.Failed);
        result.Path.Should().Be(missingPath);
        lifecycle.CurrentPath.Should().BeNull();
        feedback.Results.Should().BeEmpty();

        await startup.ReportFeedbackAsync(result);

        feedback.Results.Should().ContainSingle().Which.Should().BeSameAs(result);
    }

    [Fact]
    public void Hosts_DelegateStartupOpeningToPortableSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("new PresentationStartupOpenSession(_fileSession)");
            source.Should().Contain("startupOpenSession.Plan(");
            source.Should().NotContain("PresentationFilePersistenceWorkflow.Open(startupPresentation)");
        }
    }

    [Fact]
    public async Task ExportCommands_OrchestratePortableRenderAndNativeVideoPorts()
    {
        var pdfPath = Path.Combine(TempDirectory, "deck.pdf");
        var notesPdfPath = Path.Combine(TempDirectory, "deck-notes.pdf");
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
        picker.SaveResult = PresentationFilePickerResult.Selected(notesPdfPath);
        var notesPdf = await session.ExportNotesPagePdfAsync();
        var images = await session.ExportImagesAsync();
        picker.SaveResult = PresentationFilePickerResult.Selected(videoPath);
        var exportedVideo = await session.ExportVideoAsync(new PresentationVideoExportRequest(
            SecondsPerSlide: 0.1,
            UseRecordedTimings: false,
            IncludeNarration: false));

        pdf.Succeeded.Should().BeTrue();
        File.ReadAllBytes(pdfPath).Should().Equal(FakeRenderPort.PdfBytes);
        notesPdf.Succeeded.Should().BeTrue();
        File.ReadAllBytes(notesPdfPath).Should().Equal(FakeRenderPort.PdfBytes);
        session.LastNotesPagePdfRenderPlan.Should().NotBeNull();
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
    public async Task ExportPdf_RenderFailurePreservesExistingTargetAndCleansTemporaryFile()
    {
        var target = Path.Combine(TempDirectory, "deck.pdf");
        await File.WriteAllTextAsync(target, "old");
        var picker = new FakePickerPort
        {
            SaveResult = PresentationFilePickerResult.Selected(target),
        };
        var render = new FakeRenderPort
        {
            PdfFailure = new InvalidOperationException("render failed"),
        };
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            picker,
            render);

        var result = await session.ExportPdfAsync();

        result.Status.Should().Be(PresentationFileCommandStatus.Failed);
        result.Error!.Exception.Message.Should().Contain("render failed");
        (await File.ReadAllTextAsync(target)).Should().Be("old");
        Directory.GetFiles(TempDirectory).Should().Equal(target);
    }

    /// <summary>
    /// r139 sweep78-1: FreeP.App.Host and FreeW.App.Host both invoke this whole chain with
    /// `command.GetAwaiter().GetResult()` on the UI thread (see
    /// FreeP.App.Host/MainWindow.cs:3792 RunFileCommand). AtomicExportExecutor opens the
    /// temporary export file with real async I/O (FileOptions.Asynchronous); if that write
    /// genuinely completes on a thread-pool/IOCP thread rather than synchronously, any
    /// un-configured await in the chain above it tries to resume by posting its continuation
    /// back to the SynchronizationContext captured on the (now permanently blocked) UI thread --
    /// which never happens, hanging the app forever. This test reproduces exactly that shape: a
    /// dedicated thread installs a SynchronizationContext that queues posted continuations but
    /// never pumps them (standing in for a WPF Dispatcher thread parked in GetResult()), then
    /// blocks on ExportPdfAsync() the same way RunFileCommand does. The injected
    /// AtomicExportExecutor's temp-file write forces a genuine thread-pool hop via a
    /// properly-ConfigureAwait(false)'d Task.Run before delegating to the real file write, so the
    /// only await left that could need the blocked context is production code's own.
    /// Before the sweep78-1 fix (missing .ConfigureAwait(false) on the awaits chaining
    /// ExportPdfAsync -> ExportPdfArtifactAsync -> AtomicExportExecutor.ExecuteAsync -> the
    /// render delegate's output.WriteAsync), this test times out (thread.Join returns false).
    /// After the fix, the export completes promptly even though the pump context is never
    /// serviced.
    /// </summary>
    [Fact]
    public void ExportPdfAsync_DoesNotDeadlockUiThreadWhenTemporaryFileWriteCompletesOnThreadPool()
    {
        var target = Path.Combine(TempDirectory, "deadlock-repro.pdf");
        var picker = new FakePickerPort { SaveResult = PresentationFilePickerResult.Selected(target) };
        var render = new FakeRenderPort();
        var session = new PresentationFileCommandSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            picker,
            render,
            new FakePrintPort(),
            new FakeVideoPort(),
            atomicExportExecutor: CreateDeferredWriteAtomicExportExecutor());

        PresentationFileCommandResult? result = null;
        Exception? threadException = null;
        var uiThread = new Thread(() =>
        {
            // Stands in for the WPF Dispatcher: continuations posted here are queued, never run --
            // exactly like a real Dispatcher thread that is itself parked in GetResult() and so
            // never pumps its own message queue.
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());
            try
            {
                // Mirrors RunFileCommand: `command.GetAwaiter().GetResult().Succeeded`.
                result = session.ExportPdfAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        })
        {
            IsBackground = true,
        };
        uiThread.Start();
        var completedWithoutDeadlock = uiThread.Join(TimeSpan.FromSeconds(10));

        completedWithoutDeadlock.Should().BeTrue(
            "ExportPdfAsync must not require the blocked UI thread's SynchronizationContext to " +
            "resume once the temporary file write completes off-thread -- see sweep78-1");
        threadException.Should().BeNull();
        result.Should().NotBeNull();
        result!.Succeeded.Should().BeTrue();
        File.ReadAllBytes(target).Should().Equal(FakeRenderPort.PdfBytes);
    }

    /// <summary>
    /// Sibling of <see cref="ExportPdfAsync_DoesNotDeadlockUiThreadWhenTemporaryFileWriteCompletesOnThreadPool"/>:
    /// proves the sweep78-1 fix did not stop ExportPdfAsync from working when called the ordinary
    /// way (no blocked UI thread, default AtomicExportExecutor) -- the neighbouring behaviour the
    /// happy-path tests above already exercise.
    /// </summary>
    [Fact]
    public async Task ExportPdfAsync_StillSucceedsOnOrdinaryCall()
    {
        var target = Path.Combine(TempDirectory, "ordinary.pdf");
        var picker = new FakePickerPort { SaveResult = PresentationFilePickerResult.Selected(target) };
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            picker,
            new FakeRenderPort());

        var result = await session.ExportPdfAsync();

        result.Succeeded.Should().BeTrue();
        File.ReadAllBytes(target).Should().Equal(FakeRenderPort.PdfBytes);
    }

    private static AtomicExportExecutor CreateDeferredWriteAtomicExportExecutor() =>
        new(
            createTemporaryFile: targetPath =>
            {
                var fullTargetPath = Path.GetFullPath(targetPath);
                var directory = Path.GetDirectoryName(fullTargetPath);
                var tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
                return TemporaryFileLease.Create(
                    $".{Path.GetFileName(fullTargetPath)}.",
                    ".tmp",
                    tempDirectory,
                    fileSystem: new DeferredWriteFileSystem());
            },
            replaceDestination: AtomicFileWriter.ReplaceTarget);

    /// <summary>
    /// Stands in for the WPF Dispatcher's SynchronizationContext for the sweep78-1 deadlock
    /// repro: continuations posted to it are counted and dropped, never executed -- modeling a UI
    /// thread that is itself synchronously blocked in GetAwaiter().GetResult() and therefore never
    /// pumps its own message queue.
    /// </summary>
    private sealed class NeverPumpedSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state) => PostCount++;

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>
    /// <see cref="ITemporaryResourceFileSystem"/> whose write stream forces a genuine thread-pool
    /// hop (via a properly ConfigureAwait(false)'d Task.Run) before delegating to the real file
    /// write, mimicking real overlapped file I/O whose completion arrives on an IOCP thread-pool
    /// thread rather than synchronously. This isolates the PRODUCTION await chain under test --
    /// not this helper's own timing -- as the thing that must tolerate a captured
    /// SynchronizationContext that never resumes.
    /// </summary>
    private sealed class DeferredWriteFileSystem : ITemporaryResourceFileSystem
    {
        public string GetTemporaryDirectoryPath() => Path.GetTempPath();
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public Stream CreateNewFile(string path) =>
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        public Stream OpenFileForWrite(string path, bool useAsync, int bufferSize)
        {
            var real = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.None, bufferSize, useAsync);
            return new DeferredAsyncStream(real);
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    }

    private sealed class DeferredAsyncStream : Stream
    {
        private readonly Stream _inner;

        public DeferredAsyncStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            // Genuine, properly-configured asynchrony: forces the continuation below onto a
            // thread-pool thread (never via SynchronizationContext), exactly like a real overlapped
            // write's IOCP completion callback.
            await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() => { }, cancellationToken).ConfigureAwait(false);
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    [Fact]
    public void BuildVideoFramePackage_InjectedArtifactRetainsPackageAndImageDiagnostics()
    {
        var presentation = Presentation.CreateEmpty();
        var request = new PresentationVideoExportRequest(
            Quality: PresentationVideoQualityKind.Standard,
            SecondsPerSlide: 0.25,
            IncludeNarration: false);
        var video = new FakeVideoPort();
        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            request,
            static (_, _, _, _) => [0x89, 0x50, 0x4E, 0x47],
            video.Capabilities);
        string[] diagnostics = ["Slide 1: injected image diagnostic"];
        var artifact = new PresentationVideoFramePackageArtifact(package, diagnostics);
        PresentationVideoExportRequest? receivedRequest = null;
        var session = CreateSession(
            () => presentation,
            _ => { },
            new FakeLifecyclePort(),
            new FakePickerPort(),
            video: video,
            videoPackageArtifactFactory: value =>
            {
                receivedRequest = value;
                return artifact;
            });

        var result = session.BuildVideoFramePackage(request);

        result.Should().BeSameAs(package);
        receivedRequest.Should().BeSameAs(request);
        session.LastVideoFramePackage.Should().BeSameAs(package);
        session.LastVideoFrameImageDiagnostics.Should().BeSameAs(diagnostics);
        session.LastVideoExportPlan.Should().BeSameAs(package.Plan.ExportPlan);
        session.LastVideoExecutionDescriptor!.PackagePlan.Should().BeSameAs(package.Plan);
        session.LastVideoExportHandoffPlan.Should().BeSameAs(session.LastVideoExecutionDescriptor.HandoffPlan);
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
        result.ImageDiagnostics.Should().BeEmpty(
            "sibling no-regression: a deck with no undecodable pictures must not report an image warning");
    }

    // R136: PDF/Image/Video export all surface the "an embedded picture could not be decoded" warning
    // (see SlideImageRenderDiagnostics), but Print built its output package with the plain
    // (non-diagnostics) render/writer delegates, so the exact same undecodable picture silently
    // produced pages with a picture missing and no indication anything was wrong when printed.
    [Fact]
    public async Task PrintAsync_SurfacesImageDiagnostics_WhenSlideRenderReportsUndecodablePicture()
    {
        var print = new FakePrintPort();
        var render = new FakeRenderPort(reportUndecodableImage: true);
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            new FakePickerPort(),
            render,
            print: print);
        var request = new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);

        var result = await session.PrintAsync(request);

        result.Succeeded.Should().BeTrue();
        result.ImageDiagnostics.Should().NotBeEmpty(
            "Print must surface the same undecodable-image warning that PDF/Image/Video export show, " +
            "not silently omit the picture");
    }

    // R136: both FreeP shells fire Export Video as fire-and-forget ("() => _ = _fileSession.ExportVideoAsync()"),
    // so a second invocation while the first is still writing the output file previously started a
    // second, concurrent export racing on the same output. This drives the guard through the lowest
    // shared entry point both ExportVideoAsync (after its picker) and the direct-path callers land on.
    [Fact]
    public async Task ExportVideoToPathAsync_SecondCallWhileRunning_ReportsAlreadyRunningWithoutStartingASecondExport()
    {
        var video = new BlockingVideoPort();
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            new FakePickerPort(),
            video: video);
        var firstPath = Path.Combine(TempDirectory, "first.mp4");
        var secondPath = Path.Combine(TempDirectory, "second.mp4");

        try
        {
            var firstTask = session.ExportVideoToPathAsync(firstPath);
            await video.EnteredExportAsync.Task;

            // Without the guard, a second call would itself enter the (still-blocked) video port and
            // wait behind the first export forever, so bound the wait instead of awaiting it directly
            // -- an unguarded second call must fail this assertion within the timeout, not hang the run.
            var secondTask = session.ExportVideoToPathAsync(secondPath);
            var winner = await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(2)));
            winner.Should().BeSameAs(secondTask,
                "the guard must turn the second call away immediately instead of letting it block " +
                "behind the first, still-running export");

            var second = await secondTask;
            second.Status.Should().Be(PresentationFileCommandStatus.Unavailable);
            second.Message.Should().Contain("already running");
            video.ExportCount.Should().Be(1,
                "the second invocation must be turned away by the guard instead of starting a second export");

            video.Release();
            var first = await firstTask;
            first.Succeeded.Should().BeTrue();
        }
        finally
        {
            video.Release();
        }
    }

    // Sibling no-regression: the guard must release once an export finishes, so a legitimate later
    // export is not permanently blocked by an earlier, already-completed one.
    [Fact]
    public async Task ExportVideoToPathAsync_SequentialCalls_BothSucceedOnceTheGuardReleases()
    {
        var video = new FakeVideoPort();
        var session = CreateSession(
            Presentation.CreateEmpty,
            _ => { },
            new FakeLifecyclePort(),
            new FakePickerPort(),
            video: video);
        var firstPath = Path.Combine(TempDirectory, "first.mp4");
        var secondPath = Path.Combine(TempDirectory, "second.mp4");

        var first = await session.ExportVideoToPathAsync(firstPath);
        var second = await session.ExportVideoToPathAsync(secondPath);

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        video.ExportCount.Should().Be(2);
    }

    // r137-remediation2: proves the external-modification guard fires through the REAL entry point
    // (OpenPathAsync captures the write time via PresentationFilePersistenceWorkflow.Open; a second
    // writer mutates the file on disk; session.SaveAsync fires the guard on its own) -- nothing in
    // this test passes expectedLastWriteTimeUtc directly, unlike PresentationFilePersistenceWorkflow
    // Tests's unit-level coverage of the Save check itself.
    [Fact]
    public async Task SaveAsync_ExternallyModifiedFile_DeclinedPromptDoesNotOverwrite()
    {
        var path = Path.Combine(TempDirectory, "Shared.pptx");
        PresentationFilePersistenceWorkflow.Save(path, TitledPresentation("Original"));
        var lifecycle = new FakeLifecyclePort();
        var presentation = TitledPresentation("My Edit");
        var session = CreateSession(
            () => presentation,
            _ => { },
            lifecycle,
            new FakePickerPort(),
            confirmExternallyModifiedOverwriteAsync: (_, _) => Task.FromResult(false));

        var opened = await session.OpenPathAsync(path);
        opened.Succeeded.Should().BeTrue();

        // Simulate a second writer (another FreeP instance, a sync client) touching the file on
        // disk after we opened it but before we save -- a real mtime change, not a fabricated one.
        PresentationFilePersistenceWorkflow.Save(path, TitledPresentation("Someone Else's Edit"));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        var saved = await session.SaveAsync();

        saved.Status.Should().Be(PresentationFileCommandStatus.Cancelled);
        PresentationFilePersistenceWorkflow.Open(path).Presentation.Properties.Title.Should().Be(
            "Someone Else's Edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [Fact]
    public async Task SaveAsync_ExternallyModifiedFile_ConfirmedPromptOverwrites()
    {
        var path = Path.Combine(TempDirectory, "Shared.pptx");
        PresentationFilePersistenceWorkflow.Save(path, TitledPresentation("Original"));
        var lifecycle = new FakeLifecyclePort();
        var presentation = TitledPresentation("My Edit");
        var confirmedPaths = new List<string>();
        var session = CreateSession(
            () => presentation,
            _ => { },
            lifecycle,
            new FakePickerPort(),
            confirmExternallyModifiedOverwriteAsync: (confirmedPath, _) =>
            {
                confirmedPaths.Add(confirmedPath);
                return Task.FromResult(true);
            });

        var opened = await session.OpenPathAsync(path);
        opened.Succeeded.Should().BeTrue();

        PresentationFilePersistenceWorkflow.Save(path, TitledPresentation("Someone Else's Edit"));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) + TimeSpan.FromMinutes(1));

        var saved = await session.SaveAsync();

        saved.Succeeded.Should().BeTrue();
        confirmedPaths.Should().Equal(path);
        PresentationFilePersistenceWorkflow.Open(path).Presentation.Properties.Title.Should().Be("My Edit");
    }

    // Save-As to a DIFFERENT path than the one that was opened must never fire the guard: the new
    // target has no prior observation to compare against, even though the ORIGINAL file was changed
    // externally in the meantime.
    [Fact]
    public async Task SaveAsAsync_ToDifferentPath_NeverFiresGuardEvenWhenOriginalWasModified()
    {
        var originalPath = Path.Combine(TempDirectory, "Original.pptx");
        PresentationFilePersistenceWorkflow.Save(originalPath, TitledPresentation("Original"));
        var differentPath = Path.Combine(TempDirectory, "SaveAsTarget.pptx");
        var lifecycle = new FakeLifecyclePort();
        var presentation = TitledPresentation("My Edit");
        var promptInvoked = false;
        var picker = new FakePickerPort { SaveResult = PresentationFilePickerResult.Selected(differentPath) };
        var session = CreateSession(
            () => presentation,
            _ => { },
            lifecycle,
            picker,
            confirmExternallyModifiedOverwriteAsync: (_, _) =>
            {
                promptInvoked = true;
                return Task.FromResult(false);
            });

        var opened = await session.OpenPathAsync(originalPath);
        opened.Succeeded.Should().BeTrue();

        PresentationFilePersistenceWorkflow.Save(originalPath, TitledPresentation("Someone Else's Edit"));
        File.SetLastWriteTimeUtc(originalPath, File.GetLastWriteTimeUtc(originalPath) + TimeSpan.FromMinutes(1));

        var savedAs = await session.SaveAsAsync();

        savedAs.Succeeded.Should().BeTrue();
        promptInvoked.Should().BeFalse("Save-As to a different path has no prior observation to compare");
        PresentationFilePersistenceWorkflow.Open(differentPath).Presentation.Properties.Title.Should().Be("My Edit");
    }

    private static Presentation TitledPresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
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

        nonLocal.Status.Should().Be(OperationStatus.ValidationFailed);
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
        IPresentationVideoPort? video = null,
        FakeFeedbackPort? feedback = null,
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
            videoPackageArtifactFactory = null,
        Func<string, CancellationToken, Task<bool>>? confirmExternallyModifiedOverwriteAsync = null) =>
        PresentationFileCommandSessionFactory.Create(
            new PresentationFileCommandSessionComposition(
                getPresentation,
                loadPresentation,
                lifecycle,
                picker,
                render ?? new FakeRenderPort(),
                print ?? new FakePrintPort(),
                video ?? new FakeVideoPort(),
                feedback,
                VideoPackageArtifactFactory: videoPackageArtifactFactory,
                ConfirmExternallyModifiedOverwriteAsync: confirmExternallyModifiedOverwriteAsync));

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

        public void MarkDirtyWithPath(string? path)
        {
            CurrentPath = path;
            MarkDirty();
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

        private readonly bool _reportUndecodableImage;

        public FakeRenderPort(bool reportUndecodableImage = false) =>
            _reportUndecodableImage = reportUndecodableImage;

        public int RenderCount { get; private set; }
        public Exception? PdfFailure { get; init; }
        public PresentationSlideImageRenderer RenderSlideToPng => Render;
        public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
            (presentation, slideIndex, widthPx, heightPx, _) => Render(presentation, slideIndex, widthPx, heightPx);
        public PresentationRasterPdfWriter WriteRasterPdf => _ => WritePdf();
        public PresentationPdfContentWriter WriteVectorPdf => _ => WritePdf();

        private byte[] WritePdf()
        {
            if (PdfFailure is not null)
                throw PdfFailure;
            return PdfBytes;
        }

        private byte[] Render(Presentation presentation, int slideIndex, int widthPx, int heightPx)
        {
            RenderCount++;
            // Mirrors what WpfPresentationSlideImageRenderer / SlideCanvas.RenderPicture report when an
            // embedded picture on the slide could not be decoded while compositing an otherwise
            // well-formed slide PNG (see SlideImageRenderDiagnostics).
            if (_reportUndecodableImage)
                SlideImageRenderDiagnostics.ReportUndecodableImage(1, "forced by test");
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

    /// <summary>
    /// Video port whose <see cref="ExportAsync"/> blocks until <see cref="Release"/> is called, so a
    /// test can deterministically observe a second Export Video call arriving while the first is still
    /// in flight instead of relying on timing.
    /// </summary>
    private sealed class BlockingVideoPort : IPresentationVideoPort
    {
        private readonly TaskCompletionSource _releaseSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PresentationVideoExportHandoffHostCapabilities Capabilities { get; } = new(
            "test blocking video host",
            CanEncodeMp4: true,
            CanCaptureNarration: false,
            CanCaptureCameraAndMedia: false,
            UnavailableReason: string.Empty);

        public TaskCompletionSource EnteredExportAsync { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExportCount { get; private set; }

        public async Task<PresentationNativeCommandResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
            CancellationToken cancellationToken)
        {
            ExportCount++;
            EnteredExportAsync.TrySetResult();
            await _releaseSignal.Task;
            return PresentationNativeCommandResult.Success("Exported video");
        }

        public void Release() => _releaseSignal.TrySetResult();
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
