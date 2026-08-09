using Free.Shared.AppServices.Printing;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWOutputWorkflowTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        nameof(FreeWOutputWorkflowTests),
        Guid.NewGuid().ToString("N"));

    public FreeWOutputWorkflowTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { /* best effort */ }
    }

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
        var target = Path.Combine(_tempDirectory, "Document.pdf");
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
        Directory.GetFiles(_tempDirectory).Should().Equal(target);
    }

    [Fact]
    public async Task ExportExecution_FailurePreservesExistingTargetAndCleansTemporaryFile()
    {
        var target = Path.Combine(_tempDirectory, "Document.xps");
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
        Directory.GetFiles(_tempDirectory).Should().Equal(target);
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
    public async Task PortablePrintWorkflow_OwnsDiscoveryRenderSubmissionAndCleanup()
    {
        var service = new RecordingPrintService
        {
            Discovery = AvailableDiscovery(),
            Submission = new PrintSubmissionResult(PrintSubmissionStatus.Submitted, "Office"),
        };
        var workflow = new FreeWPortablePrintWorkflow(service);
        Stream? renderedStream = null;

        var result = await workflow.ExecuteAsync(
            (discovery, _) => Task.FromResult<PrintSelection?>(new("Office", Copies: 2)),
            async (stream, _, token) =>
            {
                renderedStream = stream;
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("pdf"), token);
            });

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Be("Sent to printer Office.");
        renderedStream.Should().NotBeNull();
        renderedStream!.CanWrite.Should().BeFalse();
        service.SubmittedPath.Should().NotBeNull();
        service.SubmittedFileExisted.Should().BeTrue();
        File.Exists(service.SubmittedPath!).Should().BeFalse();
    }

    [Fact]
    public async Task PortablePrintWorkflow_UnavailableStopsBeforeSelectionAndRendering()
    {
        var service = new RecordingPrintService
        {
            Discovery = new(
                PrinterDiscoveryStatus.NoPrinters,
                [],
                null),
        };
        var workflow = new FreeWPortablePrintWorkflow(service);
        var selected = false;
        var rendered = false;

        var result = await workflow.ExecuteAsync(
            (_, _) =>
            {
                selected = true;
                return Task.FromResult<PrintSelection?>(null);
            },
            (_, _, _) =>
            {
                rendered = true;
                return ValueTask.CompletedTask;
            });

        result.Outcome.Should().Be(FreeWPrintExecutionOutcome.Unavailable);
        result.Message.Should().Contain("No printers");
        selected.Should().BeFalse();
        rendered.Should().BeFalse();
    }

    [Fact]
    public async Task PortablePrintWorkflow_DiscoveryFailureReturnsReusableResult()
    {
        var workflow = new FreeWPortablePrintWorkflow(new ThrowingDiscoveryPrintService());

        var discovery = await workflow.DiscoverAsync();

        discovery.Status.Should().Be(PrinterDiscoveryStatus.Failed);
        discovery.Message.Should().Contain("backend unavailable");
        FreeWPrintMessagePlanner.FormatDiscovery(discovery)
            .Should().Contain("Create PDF");
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

    private sealed class RecordingPrintService : IPlatformPrintService
    {
        public bool IsSupported => true;

        public PrinterDiscoveryResult Discovery { get; init; } = AvailableDiscovery();

        public PrintSubmissionResult Submission { get; init; } =
            new(PrintSubmissionStatus.Submitted, "Office");

        public string? SubmittedPath { get; private set; }

        public bool SubmittedFileExisted { get; private set; }

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Discovery);

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default)
        {
            SubmittedPath = pdfPath;
            SubmittedFileExisted = File.Exists(pdfPath);
            return Task.FromResult(Submission);
        }
    }

    private sealed class ThrowingDiscoveryPrintService : IPlatformPrintService
    {
        public bool IsSupported => true;

        public Task<PrinterDiscoveryResult> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("backend unavailable");

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
