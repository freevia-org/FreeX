using System.IO;
using System.Windows;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r139-remediation: the sweep78-1 fix (round 139) resolved FreeP's PDF-export deadlock by adding
/// ConfigureAwait(false) down the ExportPdfArtifactAsync chain (see
/// PresentationFileCommandSession.cs). That let <c>CompleteAsync</c>'s
/// <c>await ReportFeedbackAsync(result, cancellationToken)</c> resume on a thread-pool thread
/// instead of the UI thread. <see cref="WpfPresentationFileFeedbackPort"/>.ReportAsync then called
/// straight into WPF (SisterWpfFileCommandWorkflow -&gt; WpfUserMessageService -&gt;
/// MessageBox.Show(ownerWindow, ...)) from that thread-pool thread. Because <see cref="Window"/> is
/// a DispatcherObject, that threw InvalidOperationException ("a different thread owns it") --
/// turning a SUCCESSFUL export into a visible crash. Reproduced and fixed in
/// WpfPresentationFileCommandPorts.cs (WpfPresentationFileFeedbackPort now marshals via its
/// captured Dispatcher, using BeginInvoke -- not a blocking Invoke, which would deadlock the same
/// way RunFileCommand's <c>GetAwaiter().GetResult()</c> never pumps its own queue).
///
/// <para>
/// Both of <c>PresentationFileCommandSessionTests</c>'s sibling "DoesNotDeadlock" tests
/// (FreeP.App.Presentation.Tests) construct their session with a null/absent feedback port, so
/// neither exercises this: this test supplies the REAL production
/// <see cref="WpfPresentationFileFeedbackPort"/>, wrapping a real
/// <see cref="SisterWpfFileCommandWorkflow"/> bound to a real WPF <see cref="Window"/> via
/// <see cref="WpfUserMessageService"/> -- exactly how
/// <c>WpfPresentationFileCommandSessionFactory.Create</c> wires it in
/// FreeP.App.Host/WpfPresentationFileCommandPorts.cs -- and reproduces the exact never-pumped
/// UI-thread shape RunFileCommand's <c>command.GetAwaiter().GetResult()</c> creates in production
/// (see the <see cref="NeverPumpedSynchronizationContext"/> below, borrowed from the sibling
/// deadlock-repro test).
/// </para>
/// </summary>
public sealed class R139_ExportPdfFeedbackPortCrossThreadTests
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.R139FeedbackPortTests-");
    private string TempDirectory => _temporaryDirectory.Path;

    [StaFact]
    public void ExportPdfAsync_WithRealFeedbackPort_ReportsImageDiagnosticsWithoutCrossThreadCrash()
    {
        var previousMessageBoxHandler = HeadlessMessageBox.Handler;
        HeadlessMessageBox.Handler = static (_, _) => UserMessageResult.Ok;
        try
        {
            // Never shown, so there is nothing that needs an explicit Close() -- and importantly,
            // avoiding one sidesteps an unrelated hazard: this test's own body legitimately calls
            // ExportPdfAsync from a background thread, so anything past that point in THIS method must
            // not assume it is still running on the thread that constructed the window (see the sibling
            // ordinary-await test below, which discovered this the hard way).
            var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
            {
                // Real production wiring: the same SisterWpfFileCommandWorkflow + WpfUserMessageService
                // + Window combination WpfPresentationFileCommandSessionFactory.Create builds for a real
                // host -- not a test double that would skip the WPF-touching code the bug lives in.
                var workflow = new SisterWpfFileCommandWorkflow(
                    "FreeP",
                    maxRecentEntries: () => 10,
                    onChanged: () => { },
                    save: () => true,
                    messageService: new WpfUserMessageService(window));
                var feedback = new WpfPresentationFileFeedbackPort(workflow);

                var target = Path.Combine(TempDirectory, "export.pdf");
                var picker = new FakePickerPort { SaveResult = PresentationFilePickerResult.Selected(target) };
                // reportUndecodableImage: true forces the SUCCESSFUL-export-with-diagnostics shape the
                // gap called out -- BuildFileFeedback only touches WPF for a bare success when
                // ImageDiagnostics is non-empty (ShowExportImageWarnings); an empty-diagnostics success
                // never reaches WPF at all and would not exercise the regression.
                var render = new FakeRenderPort(reportUndecodableImage: true);
                var session = new PresentationFileCommandSession(
                    Presentation.CreateEmpty,
                    _ => { },
                    new FakeLifecyclePort(),
                    picker,
                    render,
                    new FakePrintPort(),
                    new FakeVideoPort(),
                    feedback: feedback,
                    atomicExportExecutor: CreateDeferredWriteAtomicExportExecutor());

                PresentationFileCommandResult? result = null;
                Exception? threadException = null;
                var uiThread = new Thread(() =>
                {
                    // Stands in for the WPF Dispatcher thread: continuations posted here queue up and
                    // are never run -- exactly like a real Dispatcher thread that is itself parked in
                    // GetResult() and so never pumps its own message queue. If the fix under test ever
                    // regresses back to running WPF-touching feedback synchronously on the calling
                    // thread, this is what reproduces the crash: this thread is deliberately NOT the
                    // thread that constructed WpfPresentationFileFeedbackPort (that was the test
                    // method's own STA thread), so it stands in for "some thread-pool thread" either way.
                    SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());
                    try
                    {
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
                var completedWithoutHanging = uiThread.Join(TimeSpan.FromSeconds(10));

                completedWithoutHanging.Should().BeTrue(
                    "ExportPdfAsync must complete promptly even though its feedback report is " +
                    "deferred to a Dispatcher nobody is pumping during this test");
                threadException.Should().BeNull(
                    "reporting successful-export image diagnostics through the real feedback port " +
                    "from a thread other than the one that constructed it must not throw a " +
                    "cross-thread WPF exception -- see r139-remediation");
                result.Should().NotBeNull();
                result!.Succeeded.Should().BeTrue();
                result.ImageDiagnostics.Should().ContainSingle();
                File.Exists(target).Should().BeTrue();
            }
        }
        finally
        {
            HeadlessMessageBox.Handler = previousMessageBoxHandler;
        }
    }

    /// <summary>
    /// Sibling no-regression check: the same real feedback port, called the ordinary way (an
    /// unblocked <c>await</c>, not the never-pumped GetResult() shape the test above forces) must
    /// still succeed and report the diagnostics -- the fix must not turn feedback reporting into a
    /// silent no-op for the everyday case just because it now has to be thread-safe for the
    /// GetResult() case too.
    ///
    /// <para>
    /// Deliberately does not assert -- or depend on -- which thread ReportOnUiThread ends up
    /// running on: PresentationFileCommandSession's ConfigureAwait(false) chain (sweep78-1) means
    /// even this ordinary `await` is not guaranteed to resume on the thread that started it. The
    /// test installs an explicit dispatcher synchronization context and pumps it only until the
    /// await completes, so it can assert that the queued warning is delivered without touching the
    /// WPF <see cref="Window"/> from an arbitrary continuation thread.
    /// </para>
    /// </summary>
    [StaFact]
    public void ExportPdfAsync_WithRealFeedbackPort_StillReportsOnTheOrdinaryAwaitPath()
    {
        string? reportedWarning = null;
        var previousMessageBoxHandler = HeadlessMessageBox.Handler;
        HeadlessMessageBox.Handler = (message, buttons) =>
        {
            buttons.Should().Be(UserMessageButtons.Ok);
            reportedWarning = message;
            return UserMessageResult.Ok;
        };

        try
        {
            var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
            var workflow = new SisterWpfFileCommandWorkflow(
                "FreeP",
                maxRecentEntries: () => 10,
                onChanged: () => { },
                save: () => true,
                messageService: new WpfUserMessageService(window));
            var feedback = new WpfPresentationFileFeedbackPort(workflow);

            var target = Path.Combine(TempDirectory, "export-ordinary-await.pdf");
            var picker = new FakePickerPort { SaveResult = PresentationFilePickerResult.Selected(target) };
            var render = new FakeRenderPort(reportUndecodableImage: true);
            var session = new PresentationFileCommandSession(
                Presentation.CreateEmpty,
                _ => { },
                new FakeLifecyclePort(),
                picker,
                render,
                new FakePrintPort(),
                new FakeVideoPort(),
                feedback: feedback);

            var dispatcher = Dispatcher.CurrentDispatcher;
            var previousSynchronizationContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            try
            {
                var exportTask = AwaitExportAsync(session);
                PumpDispatcherUntilCompleted(exportTask, dispatcher);
                var result = exportTask.GetAwaiter().GetResult();

                result.Succeeded.Should().BeTrue();
                result.ImageDiagnostics.Should().ContainSingle();
                reportedWarning.Should().Contain("image warning(s) occurred");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
            }
        }
        finally
        {
            HeadlessMessageBox.Handler = previousMessageBoxHandler;
        }
    }

    private static async Task<PresentationFileCommandResult> AwaitExportAsync(
        PresentationFileCommandSession session)
    {
        return await session.ExportPdfAsync();
    }

    private static void PumpDispatcherUntilCompleted(
        Task operation,
        Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        operation.ContinueWith(
            _ => dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
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
    /// Stands in for the WPF Dispatcher's SynchronizationContext for the sweep78-1 deadlock repro
    /// (mirrors PresentationFileCommandSessionTests's private copy): continuations posted to it are
    /// counted and dropped, never executed -- modeling a UI thread that is itself synchronously
    /// blocked in GetAwaiter().GetResult() and therefore never pumps its own message queue.
    /// </summary>
    private sealed class NeverPumpedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>
    /// <see cref="ITemporaryResourceFileSystem"/> whose write stream forces a genuine thread-pool
    /// hop (via a properly ConfigureAwait(false)'d Task.Run) before delegating to the real file
    /// write, mimicking real overlapped file I/O whose completion arrives on an IOCP thread-pool
    /// thread rather than synchronously. This isolates the PRODUCTION await chain under test -- not
    /// this helper's own timing -- as the thing that must tolerate a captured SynchronizationContext
    /// that never resumes.
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

    private sealed class FakeLifecyclePort : IPresentationFileLifecyclePort
    {
        public bool IsDirty { get; private set; }
        public int DirtyGeneration { get; private set; }
        public string? CurrentPath { get; private set; }
        public string? CurrentFileName => Path.GetFileName(CurrentPath);
        public string DisplayName => CurrentFileName ?? "Presentation";
        public IReadOnlyList<RecentFileEntry> RecentEntries { get; } = [];

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
            await loadNewPresentationAsync();
            MarkSavedWithoutPath();
            return true;
        }

        public async Task<bool> OpenAsync(
            string action,
            Func<Task<string?>> pickPathAsync,
            Func<string, Task<bool>> openPathAsync)
        {
            var path = await pickPathAsync();
            return path is not null && await openPathAsync(path);
        }

        public Task<bool> SaveAsync(
            Func<string, Task<bool>> saveToCurrentPathAsync,
            Func<Task<bool>> saveAsAsync) =>
            CurrentPath is { } path ? saveToCurrentPathAsync(path) : saveAsAsync();

        public Task<bool> ConfirmCloseAllowedAsync(string action) => Task.FromResult(true);
    }

    private sealed class FakePickerPort : IPresentationFilePickerPort
    {
        public PresentationFilePickerResult OpenResult { get; set; } = PresentationFilePickerResult.Cancelled;
        public PresentationFilePickerResult SaveResult { get; set; } = PresentationFilePickerResult.Cancelled;
        public PresentationFilePickerResult FolderResult { get; set; } = PresentationFilePickerResult.Cancelled;

        public Task<PresentationFilePickerResult> PickOpenFileAsync(
            PresentationFileOpenPickerRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(OpenResult);

        public Task<PresentationFilePickerResult> PickSaveFileAsync(
            PresentationFileSavePickerRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(SaveResult);

        public Task<PresentationFilePickerResult> PickFolderAsync(
            PresentationFolderPickerRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(FolderResult);
    }

    private sealed class FakeRenderPort : IPresentationFileRenderPort
    {
        private static readonly byte[] PdfBytes = "%PDF-r139-crossthread-test"u8.ToArray();
        private readonly bool _reportUndecodableImage;

        public FakeRenderPort(bool reportUndecodableImage = false) =>
            _reportUndecodableImage = reportUndecodableImage;

        public PresentationSlideImageRenderer RenderSlideToPng => Render;
        public PresentationSlideImageRendererWithPrintMarkup RenderSlideToPngWithPrintMarkup =>
            (presentation, slideIndex, widthPx, heightPx, _) => Render(presentation, slideIndex, widthPx, heightPx);
        public PresentationRasterPdfWriter WriteRasterPdf => _ => PdfBytes;
        public PresentationPdfContentWriter WriteVectorPdf => _ => PdfBytes;

        private byte[] Render(Presentation presentation, int slideIndex, int widthPx, int heightPx)
        {
            // Mirrors what WpfPresentationSlideImageRenderer / SlideCanvas.RenderPicture report when
            // an embedded picture on the slide could not be decoded while compositing an otherwise
            // well-formed slide PNG -- see SlideImageRenderDiagnostics. PresentationFilePdfExportExecutor
            // installs the ambient collector around this call, so this is what produces the
            // non-empty ImageDiagnostics on an otherwise-SUCCESSFUL export that the gap describes.
            if (_reportUndecodableImage)
                SlideImageRenderDiagnostics.ReportUndecodableImage(1, "forced by test");
            return [0x89, 0x50, 0x4E, 0x47];
        }
    }

    private sealed class FakePrintPort : IPresentationPrintPort
    {
        public PresentationNativePrintHandoffHostCapabilities Capabilities { get; } =
            PresentationNativePrintHandoffHostCapabilities.Available("test print host");

        public Task<PresentationNativePrintPortResult> PrintAsync(
            Presentation presentation,
            PresentationPrintRequest request,
            Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
            CancellationToken cancellationToken)
        {
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

        public Task<PresentationNativeCommandResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
            CancellationToken cancellationToken) =>
            Task.FromResult(PresentationNativeCommandResult.Success("Exported video"));
    }
}
