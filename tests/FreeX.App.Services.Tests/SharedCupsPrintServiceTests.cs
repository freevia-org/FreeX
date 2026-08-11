using FluentAssertions;
using Free.Shared.AppServices.Printing;
using SharedCupsPrintCommandPlanner = Free.Shared.AppServices.Printing.CupsPrintCommandPlanner;

namespace FreeX.App.Services.Tests;

public sealed class SharedCupsPrintServiceTests
{
    [Fact]
    public void SelectionPlanner_PrefersRequestedPrinterAndCarriesSelections()
    {
        var discovery = new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            [new PrinterInfo("Office", true), new PrinterInfo("PDF")],
            "Office");

        var plan = PrintSelectionPlanner.Build(
            discovery,
            new PrintSelection(
                "PDF",
                3,
                PrintPageRange.Between(2, 4),
                PrintOrientation.Landscape,
                Collate: false));

        plan.Status.Should().Be(PrintCapabilityStatus.Ready);
        plan.SelectedPrinter.Should().Be("PDF");
        plan.Copies.Should().Be(3);
        plan.PageRange.Should().Be(PrintPageRange.Between(2, 4));
        plan.Orientation.Should().Be(PrintOrientation.Landscape);
        plan.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void SelectionPlanner_DeduplicatesPrintersAndUsesDefault()
    {
        var plan = PrintSelectionPlanner.Build(new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            [new PrinterInfo("PDF"), new PrinterInfo("office"), new PrinterInfo("Office", true)],
            "Office"));

        plan.Printers.Select(printer => printer.Name).Should().Equal("PDF", "office");
        plan.SelectedPrinter.Should().Be("office");
    }

    [Fact]
    public void SelectionPlanner_NoPrintersDisablesSubmitWithTruthfulMessage()
    {
        var plan = PrintSelectionPlanner.Build(
            new PrinterDiscoveryResult(PrinterDiscoveryStatus.NoPrinters, [], null));

        plan.Status.Should().Be(PrintCapabilityStatus.NoPrinters);
        plan.SelectedPrinter.Should().BeNull();
        plan.CanSubmit.Should().BeFalse();
        plan.Message.Should().Contain("No printers");
    }

    [Fact]
    public void DialogSession_InitializesRendererStateFromTheSharedPlan()
    {
        var session = PrintDialogSession.Start(
            new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Available,
                [new PrinterInfo("Office", true), new PrinterInfo("PDF")],
                "Office"),
            new PrintSelection(
                "PDF",
                3,
                PrintPageRange.Between(2, 4),
                PrintOrientation.Landscape,
                Collate: false));

        session.State.PrinterNames.Should().Equal("Office", "PDF");
        session.State.SelectedPrinterIndex.Should().Be(1);
        session.State.CopiesText.Should().Be("3");
        session.State.PageRangeIndex.Should().Be((int)PrintPageRangeKind.Range);
        session.State.FirstPageText.Should().Be("2");
        session.State.LastPageText.Should().Be("4");
        session.State.OrientationIndex.Should().Be((int)PrintOrientation.Landscape);
        session.State.Collate.Should().BeFalse();
        session.State.CanSubmit.Should().BeTrue();
        session.State.StatusMessage(PrintDialogText.DefaultEnglish)
            .Should().Be("Choose the printer and print settings.");
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void DialogSession_ProjectsPageRangeVisibility(
        int selectedIndex,
        bool showFirstPage,
        bool showLastPage)
    {
        var visibility = PrintDialogSession.RangeVisibility(selectedIndex);

        visibility.ShowFirstPage.Should().Be(showFirstPage);
        visibility.ShowLastPage.Should().Be(showLastPage);
    }

    [Theory]
    [InlineData("0", 0, "1", "1", PrintDialogValidationIssue.CopiesOutOfRange)]
    [InlineData("1000", 0, "1", "1", PrintDialogValidationIssue.CopiesOutOfRange)]
    [InlineData("1", 1, "0", "1", PrintDialogValidationIssue.FirstPageInvalid)]
    [InlineData("1", 2, "3", "2", PrintDialogValidationIssue.LastPageBeforeFirstPage)]
    public void DialogSession_ClassifiesInvalidRendererInput(
        string copies,
        int pageRangeIndex,
        string firstPage,
        string lastPage,
        PrintDialogValidationIssue expectedIssue)
    {
        var session = ReadyDialogSession();

        var submission = session.Submit(
            "Office",
            copies,
            pageRangeIndex,
            firstPage,
            lastPage,
            (int)PrintOrientation.Document,
            collate: true);

        submission.Succeeded.Should().BeFalse();
        submission.Selection.Should().BeNull();
        submission.ValidationIssue.Should().Be(expectedIssue);
        PrintDialogText.DefaultEnglish.ValidationMessage(expectedIssue).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0, "9", "3", PrintPageRangeKind.All)]
    [InlineData(1, "9", "3", PrintPageRangeKind.Single)]
    [InlineData(2, "3", "9", PrintPageRangeKind.Range)]
    public void DialogSession_CreatesSelectionForEveryPageRangeMode(
        int pageRangeIndex,
        string firstPage,
        string lastPage,
        PrintPageRangeKind expectedKind)
    {
        var submission = ReadyDialogSession().Submit(
            "Office",
            "2",
            pageRangeIndex,
            firstPage,
            lastPage,
            (int)PrintOrientation.Landscape,
            collate: false);

        submission.Succeeded.Should().BeTrue();
        submission.ValidationIssue.Should().Be(PrintDialogValidationIssue.None);
        submission.Selection.Should().BeEquivalentTo(new PrintSelection(
            "Office",
            2,
            expectedKind switch
            {
                PrintPageRangeKind.All => PrintPageRange.All,
                PrintPageRangeKind.Single => PrintPageRange.Single(9),
                _ => PrintPageRange.Between(3, 9),
            },
            PrintOrientation.Landscape,
            Collate: false));
    }

    [Fact]
    public void DialogSession_PreservesDiscoveryStatusMessage()
    {
        var session = PrintDialogSession.Start(new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Unavailable,
            [],
            null,
            "CUPS is unavailable."));

        session.State.CanSubmit.Should().BeFalse();
        session.State.StatusMessage(PrintDialogText.DefaultEnglish).Should().Be("CUPS is unavailable.");
    }

    [Fact]
    public void PrintSelection_RejectsInvalidCopiesAndRanges()
    {
        var copies = () => new PrintSelection(Copies: 0).Validate();
        var range = () => PrintPageRange.Between(4, 2).Validate();

        copies.Should().Throw<ArgumentOutOfRangeException>();
        range.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Service_OwnershipIsSharedAndPlatformSupportIsReported()
    {
        IPlatformPrintService service = new CupsPrintService(new FakeProcessRunner());

        service.GetType().Assembly.GetName().Name.Should().Be("Free.Shared.AppServices");
        service.IsSupported.Should().Be(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
    }

    [Fact]
    public void CommandPlanner_SingleCopyOmitsCountAndCarriesTrimmedJobTitle()
    {
        var invocation = SharedCupsPrintCommandPlanner.Submit(
            "/tmp/job.pdf",
            new PrintSelection(JobTitle: "  Budget  "),
            "Office");

        invocation.Arguments.Should().NotContain("-n");
        invocation.Arguments.Should().ContainInOrder("-t", "Budget", "/tmp/job.pdf");
    }

    [Fact]
    public async Task DiscoverAsync_DefaultModePreservesPrinterStatusBehavior()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, "printer Office is idle.\nprinter PDF is idle.\n", ""),
            new ProcessResult(0, "system default destination: Office\n", ""));

        var result = await new CupsPrintService(runner, isSupportedOverride: true).DiscoverAsync();

        result.Printers.Should().Equal(new PrinterInfo("Office", true), new PrinterInfo("PDF"));
        runner.Invocations[0].Arguments.Should().Equal("-p");
    }

    [Fact]
    public async Task DiscoverAsync_ParsesPrintersAndDefault()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, "PDF\nOffice\n", ""),
            new ProcessResult(0, "system default destination: Office\n", ""));

        var result = await FreeXCups(runner).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Available);
        result.DefaultPrinter.Should().Be("Office");
        result.Printers.Should().Equal(
            new PrinterInfo("Office", true),
            new PrinterInfo("PDF"));
        runner.Invocations.Select(invocation => invocation.FileName).Should().Equal("lpstat", "lpstat");
        runner.Invocations.Select(invocation => invocation.Arguments.Single()).Should().Equal("-e", "-d");
    }

    [Fact]
    public async Task DiscoverAsync_DefaultMissingFromListIsAppendedFirst()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(0, "Office\n", ""),
            new ProcessResult(0, "system default destination: Hidden_Default\n", ""));

        var result = await FreeXCups(runner).DiscoverAsync();

        result.Printers.Should().Equal(
            new PrinterInfo("Hidden_Default", true),
            new PrinterInfo("Office"));
    }

    [Fact]
    public async Task SubmitAsync_BuildsSupersetCupsArguments()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = SuccessfulSubmissionRunner();
            var result = await FreeXCups(runner).SubmitAsync(
                pdfPath,
                new PrintSelection(
                    Copies: 2,
                    PageRange: PrintPageRange.Between(2, 4),
                    Orientation: PrintOrientation.Landscape,
                    Collate: false,
                    JobTitle: "Budget"));

            result.Status.Should().Be(PrintSubmissionStatus.Submitted);
            runner.Invocations.Last().Should().BeEquivalentTo(
                new ProcessInvocation(
                    "lp",
                    [
                        "-d", "Office", "-n", "2", "-P", "2-4", "-o", "collate=false",
                        "-o", "orientation-requested=4", "-t", "Budget", pdfPath,
                    ]));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_NoPrintersReturnsWithoutCallingLp()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new FakeProcessRunner(
                new ProcessResult(0, "", ""),
                new ProcessResult(0, "no system default destination\n", ""));

            var result = await FreeXCups(runner)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.NoPrinters);
            runner.Invocations.Should().HaveCount(2);
            runner.Invocations[0].Should().BeEquivalentTo(SharedCupsPrintCommandPlanner.ListPrinters(
                CupsPrinterDiscoveryMode.DestinationNames));
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task DiscoverAsync_CancellationIsReported()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await FreeXCups(new FakeProcessRunner())
            .DiscoverAsync(cancellation.Token);

        result.Status.Should().Be(PrinterDiscoveryStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task DiscoverAsync_UnsupportedBackendDoesNotInvokeCommands()
    {
        var runner = new FakeProcessRunner();

        var result = await new CupsPrintService(runner, isSupportedOverride: false).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Unavailable);
        result.Printers.Should().BeEmpty();
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_CommandFailureIsReportedAsUnavailable()
    {
        var runner = new FakeProcessRunner(new ProcessResult(1, "", "lpstat: not found"));

        var result = await FreeXCups(runner).DiscoverAsync();

        result.Status.Should().Be(PrinterDiscoveryStatus.Unavailable);
        result.Message.Should().Contain("lpstat");
    }

    [Fact]
    public async Task SubmitAsync_LpFailureIsReportedWithoutClaimingSuccess()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new FakeProcessRunner(
                new ProcessResult(0, "Office\n", ""),
                new ProcessResult(0, "system default destination: Office\n", ""),
                new ProcessResult(1, "", "lp: backend rejected job"));

            var result = await FreeXCups(runner)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Succeeded.Should().BeFalse();
            result.Message.Should().Contain("backend rejected");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CancellationDuringLpIsReported()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new CancellingSubmitProcessRunner();

            var result = await FreeXCups(runner)
                .SubmitAsync(pdfPath, new PrintSelection());

            result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
            runner.Invocations.Select(invocation => invocation.FileName)
                .Should().Equal("lpstat", "lpstat", "lp");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CancellationBeforeSpoolingIsReported()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new CupsPrintService(new FakeProcessRunner())
            .SubmitAsync("unused.pdf", new PrintSelection(), cancellation.Token);

        result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
    }

    [Fact]
    public async Task SubmitAsync_CommandTimeoutIsReportedAndCancelsRunner()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new BlockingSubmitProcessRunner();
            var result = await new CupsPrintService(
                    runner,
                    isSupportedOverride: true,
                    commandTimeout: TimeSpan.FromMilliseconds(50),
                    discoveryMode: CupsPrinterDiscoveryMode.DestinationNames)
                .SubmitAsync(pdfPath, new PrintSelection(JobTitle: "Quarterly report"));

            result.Status.Should().Be(PrintSubmissionStatus.Failed);
            result.Message.Should().Be("Printing failed: the CUPS command timed out.");
            runner.CancellationObserved.Should().BeTrue();
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task SubmitAsync_CallerCancellationIsNotReportedAsTimeout()
    {
        var pdfPath = await CreatePdfStubAsync();
        try
        {
            var runner = new BlockingSubmitProcessRunner();
            using var cancellation = new CancellationTokenSource();
            var submission = new CupsPrintService(
                    runner,
                    isSupportedOverride: true,
                    commandTimeout: TimeSpan.FromSeconds(10),
                    discoveryMode: CupsPrinterDiscoveryMode.DestinationNames)
                .SubmitAsync(pdfPath, new PrintSelection(), cancellation.Token);
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            cancellation.Cancel();
            var result = await submission;

            result.Status.Should().Be(PrintSubmissionStatus.Cancelled);
            result.Message.Should().NotContain("timed out");
            runner.CancellationObserved.Should().BeTrue();
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    private static FakeProcessRunner SuccessfulSubmissionRunner() => new(
        new ProcessResult(0, "Office\n", ""),
        new ProcessResult(0, "system default destination: Office\n", ""),
        new ProcessResult(0, "request id is Office-17 (1 file(s))\n", ""));

    private static CupsPrintService FreeXCups(IProcessRunner runner) =>
        new(
            runner,
            isSupportedOverride: true,
            discoveryMode: CupsPrinterDiscoveryMode.DestinationNames);

    private static PrintDialogSession ReadyDialogSession() => PrintDialogSession.Start(
        new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            [new PrinterInfo("Office", true)],
            "Office"));

    private static async Task<string> CreatePdfStubAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"free-shared-print-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46]);
        return path;
    }

    private sealed class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingSubmitProcessRunner : IProcessRunner
    {
        public List<ProcessInvocation> Invocations { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            if (Invocations.Count == 3)
                throw new OperationCanceledException(cancellationToken);

            return Task.FromResult(Invocations.Count == 1
                ? new ProcessResult(0, "Office\n", "")
                : new ProcessResult(0, "system default destination: Office\n", ""));
        }
    }

    private sealed class BlockingSubmitProcessRunner : IProcessRunner
    {
        private int _invocationCount;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            _invocationCount++;
            if (_invocationCount == 1)
                return new ProcessResult(0, "Office\n", "");
            if (_invocationCount == 2)
                return new ProcessResult(0, "system default destination: Office\n", "");

            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking runner unexpectedly completed.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
