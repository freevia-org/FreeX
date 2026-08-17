using Free.Shared.AppServices.Printing;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWOutputWorkflowTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new(nameof(FreeWOutputWorkflowTests));
    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Theory]
    [InlineData(FreeWExportFormat.Pdf, ".pdf", "application/pdf")]
    [InlineData(FreeWExportFormat.Xps, ".xps", "application/oxps")]
    public void CreatePlan_BuildsPortablePickerRequest(
        FreeWExportFormat format,
        string extension,
        string mimeType)
    {
        var plan = FreeWExportWorkflow.CreatePlan(format, "Quarterly Report.docx");

        plan.SuggestedFileName.Should().Be("Quarterly Report" + extension);
        plan.DefaultExtensionWithDot.Should().Be(extension);
        plan.FileType.Patterns.Should().Equal("*" + extension);
        plan.FileType.MimeTypes.Should().Contain(mimeType);
        plan.Filter.Should().EndWith($"|*{extension}");
    }

    [Fact]
    public async Task ExportExecution_AtomicallyReplacesTargetAndReturnsRendererDetails()
    {
        var target = Path.Combine(TempDirectory, "Document.pdf");
        await File.WriteAllTextAsync(target, "old");
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Document");
        Stream? renderStream = null;

        var result = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            target,
            async (stream, token) =>
            {
                renderStream = stream;
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("new"), token);
                return new FreeWExportArtifact(2, "Skia");
            });

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("2 pages").And.Contain("Skia").And.Contain("Document.pdf");
        (await File.ReadAllTextAsync(target)).Should().Be("new");
        renderStream.Should().NotBeNull();
        renderStream!.CanWrite.Should().BeFalse();
        Directory.GetFiles(TempDirectory).Should().Equal(target);
    }

    [Fact]
    public async Task ExportExecution_FailurePreservesExistingTargetAndCleansTemporaryFile()
    {
        var target = Path.Combine(TempDirectory, "Document.xps");
        await File.WriteAllTextAsync(target, "old");
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Xps, "Document");
        Stream? renderStream = null;

        var result = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            target,
            async (stream, token) =>
            {
                renderStream = stream;
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("partial"), token);
                throw new InvalidOperationException("render failed");
            });

        result.Outcome.Should().Be(FreeWExportExecutionOutcome.Failed);
        result.Message.Should().Contain("render failed");
        (await File.ReadAllTextAsync(target)).Should().Be("old");
        renderStream.Should().NotBeNull();
        renderStream!.CanWrite.Should().BeFalse();
        Directory.GetFiles(TempDirectory).Should().Equal(target);
    }

    [Fact]
    public async Task ExportExecution_CancellationPreservesExistingTargetAndCleansTemporaryFile()
    {
        var target = Path.Combine(TempDirectory, "Document.pdf");
        await File.WriteAllTextAsync(target, "old");
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Document");
        using var cancellation = new CancellationTokenSource();

        var result = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            target,
            async (stream, token) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("partial"), token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return new FreeWExportArtifact();
            },
            cancellation.Token);

        result.Outcome.Should().Be(FreeWExportExecutionOutcome.Canceled);
        (await File.ReadAllTextAsync(target)).Should().Be("old");
        Directory.GetFiles(TempDirectory).Should().Equal(target);
    }

    /// <summary>
    /// r139 sweep78-1: FreeW.App.Host invokes this whole chain with
    /// `FreeWExportWorkflow.ExecuteAsync(...).GetAwaiter().GetResult()` on the UI thread (see
    /// ExportToPdf/ExportToXps in freew/FreeW.App.Host/MainWindow.cs). AtomicExportExecutor opens
    /// the temporary export file with real async I/O (FileOptions.Asynchronous); if that write
    /// genuinely completes on a thread-pool/IOCP thread rather than synchronously, an
    /// un-configured await in ExecuteAsync's own chain would try to resume by posting its
    /// continuation back to the SynchronizationContext captured on the (now permanently blocked)
    /// UI thread -- which never happens, hanging the app forever. This test reproduces exactly
    /// that shape: a dedicated thread installs a SynchronizationContext that queues posted
    /// continuations but never pumps them (standing in for a WPF Dispatcher thread parked in
    /// GetResult()), then blocks on ExecuteAsync the same way MainWindow.cs does. The render
    /// callback forces a genuine thread-pool hop via a properly-ConfigureAwait(false)'d Task.Run
    /// before writing, so the only await left that could need the blocked context is
    /// FreeWOutputWorkflow.ExecuteAsync's own. Before the sweep78-1 fix (missing
    /// .ConfigureAwait(false) on the await of AtomicExportExecutor.ExecuteAsync), this test times
    /// out (thread.Join returns false). After the fix, the export completes promptly even though
    /// the pump context is never serviced.
    /// </summary>
    [Fact]
    public void ExportExecution_DoesNotDeadlockUiThreadWhenTemporaryFileWriteCompletesOnThreadPool()
    {
        var target = Path.Combine(TempDirectory, "deadlock-repro.pdf");
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Document");

        FreeWExportExecutionResult? result = null;
        Exception? threadException = null;
        var uiThread = new Thread(() =>
        {
            // Stands in for the WPF Dispatcher: continuations posted here are queued, never run --
            // exactly like a real Dispatcher thread that is itself parked in GetResult() and so
            // never pumps its own message queue.
            SynchronizationContext.SetSynchronizationContext(new NeverPumpedSynchronizationContext());
            try
            {
                // Mirrors ExportToPdf/ExportToXps: `FreeWExportWorkflow.ExecuteAsync(...)
                // .GetAwaiter().GetResult()`.
                result = FreeWExportWorkflow.ExecuteAsync(
                    plan,
                    target,
                    async (stream, token) =>
                    {
                        // Genuine, properly-configured asynchrony: forces the write below onto a
                        // thread-pool thread (never via SynchronizationContext), exactly like a
                        // real overlapped write's IOCP completion callback.
                        await Task.Run(() => { }, token).ConfigureAwait(false);
                        await stream.WriteAsync(
                            System.Text.Encoding.UTF8.GetBytes("payload"),
                            token).ConfigureAwait(false);
                        return new FreeWExportArtifact(1, "Test");
                    }).GetAwaiter().GetResult();
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
            "FreeWExportWorkflow.ExecuteAsync must not require the blocked UI thread's " +
            "SynchronizationContext to resume once the temporary file write completes " +
            "off-thread -- see sweep78-1");
        threadException.Should().BeNull();
        result.Should().NotBeNull();
        result!.Succeeded.Should().BeTrue();
        File.Exists(target).Should().BeTrue();
    }

    /// <summary>
    /// Sibling of <see cref="ExportExecution_DoesNotDeadlockUiThreadWhenTemporaryFileWriteCompletesOnThreadPool"/>:
    /// proves the sweep78-1 fix did not stop ExecuteAsync from working the ordinary way (no
    /// blocked UI thread) -- the neighbouring behaviour the happy-path tests above already
    /// exercise.
    /// </summary>
    [Fact]
    public async Task ExportExecution_StillSucceedsOnOrdinaryCall()
    {
        var target = Path.Combine(TempDirectory, "ordinary.pdf");
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, "Document");

        var result = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            target,
            async (stream, token) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("payload"), token);
                return new FreeWExportArtifact(1, "Test");
            });

        result.Succeeded.Should().BeTrue();
        File.Exists(target).Should().BeTrue();
    }

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

    [Fact]
    public void PrintRequestPlanner_OwnsGeometryAndBoundedPageRanges()
    {
        var page = new PageSettings { WidthPt = 612, HeightPt = 792 };

        var plan = FreeWPrintRequestPlanner.Create("FreeW Document", page, totalPages: 5);
        var range = FreeWPrintRequestPlanner.ResolvePageRange(
            PrintPageRange.Between(4, 20),
            plan.TotalPages);

        plan.PageWidthDip.Should().BeApproximately(PageLayout.PointsToDip(612), 0.001);
        plan.PageHeightDip.Should().BeApproximately(PageLayout.PointsToDip(792), 0.001);
        range.Should().Be((4, 5));
        FreeWPrintRequestPlanner.FromOneBasedRange(8, 10, 5)
            .Should().Be(PrintPageRange.Single(5));
    }

    [Fact]
    public void PreviewSession_OwnsPageOptionsSummaryAndPrimaryAction()
    {
        var capability = BackstageDirectPrintCapability.Deferred("CUPS unavailable.");
        var session = new FreeWPrintPreviewSession(
            "Report",
            new PageSettings(),
            capability,
            canCreatePdf: true,
            canDirectPrint: false);

        var state = session.SetPageCount(4);
        state = session.GoToPage(99);
        state = session.ApplyOptions(new PrintSelection(
            Copies: 3,
            PageRange: PrintPageRange.Between(2, 10),
            Orientation: PrintOrientation.Landscape,
            Collate: false));

        state.Title.Should().Be("Print Preview - Report");
        state.PageCountText.Should().Be("4 pages");
        state.CurrentPage.Should().Be(4);
        state.Options.Copies.Should().Be(3);
        state.Options.EffectivePageRange.Should().Be(PrintPageRange.Between(2, 4));
        state.PrimaryAction.Action.Should().Be(FreeWPrintPreviewPrimaryAction.CreatePdf);
        state.PrimaryAction.IsEnabled.Should().BeTrue();
        state.Fields.Should().NotBeEmpty();
    }

    [Fact]
    public void PrintCapabilityPlanner_MapsCupsStateToBackstageCapability()
    {
        FreeWPrintMessagePlanner.PlanCapability(true, AvailableDiscovery())
            .IsAvailable.Should().BeTrue();
        FreeWPrintMessagePlanner.PlanCapability(false, null)
            .ActionDescription.Should().Contain("no supported native printer service");
    }

    [Fact]
    public void DocumentSnapshot_ProducesIndependentModel()
    {
        var source = TextDocument.CreateEmpty();
        source.Blocks.Clear();
        source.Blocks.Add(new Paragraph("before"));

        var clone = FreeWDocumentSnapshot.Clone(source);
        ((Paragraph)clone.Blocks[0]).Runs[0].Text = "after";

        source.PlainText.Trim().Should().Be("before");
        clone.PlainText.Trim().Should().Be("after");
    }

    private static PrinterDiscoveryResult AvailableDiscovery() =>
        new(
            PrinterDiscoveryStatus.Available,
            [new PrinterInfo("Office", IsDefault: true)],
            "Office");

}
