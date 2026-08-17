using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsPrinterSelectionTests
{
    [Fact]
    public void AvaloniaPrintPaneExposesWindowsPrinterSelectorBackedBySharedDiscovery()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var presentation = Read(
            "freep",
            "FreeP.App.Presentation",
            "PresentationPrintOutputPackageExecutor.cs");

        source.Should().Contain("surface.PrinterPickerAutomationId")
            .And.Contain("surface.NativeDialogAutomationId")
            .And.NotContain("\"FreePWindowsPrinterPicker\"")
            .And.NotContain("\"FreePWindowsPrinterDialog\"")
            .And.Contain("_printService.DiscoverAsync()")
            .And.Contain("_latestPrinterDiscovery.Printers")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
        presentation.Should().Contain("PrinterPickerAutomationId: \"FreePWindowsPrinterPicker\"")
            .And.Contain("NativeDialogAutomationId: \"FreePWindowsPrinterDialog\"");
    }

    [Fact]
    public void UnknownPrinterIsRejectedAgainstTheSharedDiscoverySnapshot()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("_latestPrinterDiscovery?.Printers.FirstOrDefault")
            .And.Contain("PresentationShellTextCatalog.WindowsPrinterQueueUnavailableStatus(normalized)")
            .And.NotContain("Windows printer queue '{normalized}' is no longer available.")
            .And.NotContain("WindowsNativePrintOutput.ForPrinter(");
    }

    // sweep78-2 (round 139): AddWindowsPrinterSelector used to call
    // `_printService.DiscoverAsync().GetAwaiter().GetResult()` directly while building the print
    // backstage pane, blocking the UI thread for however long printer discovery took -- an unreachable
    // network printer or a wedged spooler had no bound at all.
    //
    // This is a coarse source-contract tripwire, kept as a cheap regression signal alongside the real
    // behavioral proof below (ShowPrintOptionsPaneDoesNotBlockOnASlowPrinterDiscoveryProbe...). It used
    // to be the *only* signal available: the entire Windows-native printer/camera surface in
    // MainWindow.cs sits behind `#if FREEP_WINDOWS_CAPTURE`, and that constant was undefined in every
    // build because of an MSBuild PropertyGroup-ordering bug in FreeP.App.Avalonia.csproj (a
    // `Condition="'$(TargetPlatformIdentifier)' == 'Windows'"` PropertyGroup sitting in the project body
    // is evaluated before Sdk.targets -- imported only after the whole body -- has derived
    // $(TargetPlatformIdentifier) from $(TargetFramework), so the condition always saw it empty). Round
    // 139 remediation (a) fixed that PropertyGroup to key off the already-reliable $(FreePWindowsBuild)
    // signal instead, (b) fixed the CS0246 (AvaloniaOleInPlaceHost.cs was missing
    // `using FreeP.App.Rendering.Avalonia;` for AvaloniaInlineOleHostRequest) and the CS0019
    // (`printers.Count` on a string[] -- Array has no LINQ-extension-shadowed `Count` property, only
    // `Length`) that fixing (a) exposed, and (c) added the behavioral test below that actually
    // constructs a MainWindow and drives DiscoverAsync through the real code path now that it compiles.
    [Fact]
    public void AddWindowsPrinterSelectorNoLongerBlocksTheUiThreadOnPrinterDiscovery()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().NotContain(
                "_latestPrinterDiscovery = _printService.DiscoverAsync().GetAwaiter().GetResult();",
                "the print pane must never block synchronously on printer discovery -- a slow " +
                "spooler/unreachable network printer would freeze the whole window with no bound")
            .And.Contain(
                "Task.Run(() => _printService.DiscoverAsync())",
                "printer discovery must run on a background thread so a slow spooler cannot block " +
                "the caller even transiently")
            .And.Contain("StartPrinterDiscovery()")
            .And.Contain(
                "_printOptionsPaneHost?.IsVisible == true",
                "once background discovery lands, the pane must re-render so a slow probe still " +
                "reaches the user instead of being silently dropped");
    }

    // Sibling to the test above: proves the fix did not disturb the rest of the printer-selector wiring
    // this same file already contract-tests (shared discovery snapshot, automation ids, native dialog).
    [Fact]
    public void PrinterSelectorStillExposesTheSameSharedDiscoverySurface()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("surface.PrinterPickerAutomationId")
            .And.Contain("_latestPrinterDiscovery.Printers")
            .And.Contain("_latestPrinterDiscovery.DefaultPrinter")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog");
    }

    [Fact]
    public void SharedPlatformServiceOwnsDiscoveryAndSubmissionOnEveryPlatform()
    {
        var source = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("IPlatformPrintService")
            .And.Contain("new CupsPrintService()")
            .And.Contain("new WindowsPrintService(")
            .And.Contain("CupsPrintDialog.ShowAsync")
            .And.Contain("WindowsNativePrintOutput.TryShowPrinterSelectionDialog")
            .And.NotContain("_portablePrintWorkflowEnabled")
            .And.NotContain("CreateNativePrintAdapter")
            .And.NotContain("ILinuxNativePrintHandoffAdapter");
    }

    // ── Behavioral proof (round 139 remediation) ──────────────────────────────────
    //
    // The tests above are source-text tripwires; none of them constructs a MainWindow or calls
    // DiscoverAsync, so none of them could have caught the original bug at runtime -- only a diff to
    // the exact string that was fixed. These two actually run the production code path a user reaches
    // by opening File > Print: MainWindow(printService: <fake>).ShowPrintOptionsPane() ->
    // RenderPrintOptionsPane -> AddWindowsPrinterSelector -> StartPrinterDiscovery ->
    // RefreshPrinterDiscoveryAsync -> Task.Run(() => _printService.DiscoverAsync()). The fake
    // IPlatformPrintService is the same kind of seam MainWindow's own constructor already exposes for
    // production DI (swapping the OS-specific spooler bridge, not the MainWindow logic under test), and
    // its DiscoverAsync blocks synchronously on a gate the test controls -- exactly how the real
    // WindowsPrintService.DiscoverAsync behaves today (see RefreshPrinterDiscoveryAsync's own comment:
    // "currently completes its spooler probe synchronously, wrapped in Task.FromResult").
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static WindowsPrinterSelectionTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task ShowPrintOptionsPaneDoesNotBlockOnASlowPrinterDiscoveryProbeAndPopulatesResultsWhenItCompletes()
    {
        var expected = new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            new[] { new PrinterInfo("Contract-Test-Queue", IsDefault: true) },
            "Contract-Test-Queue");
        var fakeService = new GatedDiscoveryPrintService(expected);
        MainWindow? window = null;

        // Everything below runs inside ONE dispatched async delegate rather than a chain of separate
        // Dispatch calls: RefreshPrinterDiscoveryAsync's `await Task.Run(...)` continuation resumes via
        // the Avalonia dispatcher's SynchronizationContext, and that context is only actually drained
        // while an awaited async Dispatch delegate is in flight on this harness -- a bare `Action`
        // dispatch, or a loop of separate ones calling Dispatcher.UIThread.RunJobs(), does not pick the
        // continuation up. This mirrors MainWindowHeadlessTests' OnUiThreadAsync helper, which awaits
        // real production async calls (e.g. ExecutePrintForTests) the same way. The lambda needs an
        // explicit `return` (not just a trailing `await ...;`) so the compiler binds it to a
        // Task-returning Dispatch overload instead of silently treating it as async void -- the exact
        // trap the freex-dispatch-async-void-program fix guarded against elsewhere in this codebase.
        //
        // A watchdog around the whole dispatch (not inside it) still catches a real regression: if
        // AddWindowsPrinterSelector goes back to blocking synchronously on DiscoverAsync, ShowPrintOptionsPane
        // never returns while the gate below is held, and the delegate never reaches the point where it
        // opens the gate -- so the outer Task.WhenAny times this test out instead of hanging forever.
        long openElapsedMs = -1;
        var dispatchTask = Session.Dispatch(
            async () =>
            {
                window = new MainWindow(
                    Array.Empty<string>(),
                    loadRecentFilesStore: null,
                    printService: fakeService);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                window.ShowPrintOptionsPane();
                openElapsedMs = sw.ElapsedMilliseconds;

                // Give the thread-pool worker Task.Run handed DiscoverAsync to a bounded moment to
                // actually start running before checking it was invoked at all.
                var callDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (fakeService.CallCount == 0 && DateTime.UtcNow < callDeadline)
                    await Task.Delay(10);

                var stillGated = window.LatestPrinterDiscoveryForTests;

                fakeService.Release();

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (window.LatestPrinterDiscoveryForTests is null && DateTime.UtcNow < deadline)
                    await Task.Delay(25);

                return (CallCount: fakeService.CallCount, StillGated: stillGated, window.LatestPrinterDiscoveryForTests);
            },
            CancellationToken.None);

        var completedInTime =
            await Task.WhenAny(dispatchTask, Task.Delay(TimeSpan.FromSeconds(20))) == dispatchTask;
        completedInTime.Should().BeTrue(
            "opening the print pane and observing the background discovery result must both finish " +
            "within a bounded time -- if AddWindowsPrinterSelector regresses back to " +
            "DiscoverAsync().GetAwaiter().GetResult() this whole dispatch hangs forever instead");

        var (callCount, stillGated, observed) = await dispatchTask;

        openElapsedMs.Should().BeLessThan(
            2000,
            "opening the print pane must return almost immediately even while printer discovery is " +
            "still probing the spooler -- it must not wait for the gated DiscoverAsync call to finish");
        callCount.Should().Be(1, "DiscoverAsync must actually be invoked when the print pane opens");
        stillGated.Should().BeNull(
            "discovery is still gated behind the fake service at the point the pane finished opening " +
            "-- a result here would mean the pane somehow got an answer before the probe was allowed " +
            "to complete");
        observed.Should().NotBeNull(
            "once the gated probe is released, the background discovery must complete and populate " +
            "the cached result -- this is the part of the fix (Task.Run + await) that a source-text " +
            "match cannot verify");
        observed!.Status.Should().Be(PrinterDiscoveryStatus.Available);
        observed.Printers.Should().ContainSingle(printer => printer.Name == "Contract-Test-Queue");
        observed.DefaultPrinter.Should().Be("Contract-Test-Queue");
    }

    [Fact]
    public async Task PrinterDiscoveryRunsOffTheDispatcherThread()
    {
        var fakeService = new GatedDiscoveryPrintService(new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            new[] { new PrinterInfo("Contract-Test-Queue") },
            "Contract-Test-Queue"));
        fakeService.Release();
        MainWindow? window = null;
        int openingThreadId = -1;

        // Compare against the actual OS thread that ran ShowPrintOptionsPane() itself (captured in the
        // same dispatch, right here), rather than Dispatcher.UIThread.CheckAccess(): this test assembly's
        // whole Avalonia headless session -- and its single simulated UI thread -- is shared with every
        // other parallel-running test class (MainWindowHeadlessTests included, via the same
        // GetOrStartForAssembly call), so a dispatcher-identity flag can be momentarily ambiguous under
        // heavy cross-class parallel load. A directly captured thread id has no such dependency.
        await Session.Dispatch(
            () =>
            {
                openingThreadId = Environment.CurrentManagedThreadId;
                window = new MainWindow(
                    Array.Empty<string>(),
                    loadRecentFilesStore: null,
                    printService: fakeService);
                window.ShowPrintOptionsPane();
            },
            CancellationToken.None);

        // CallerThreadId is written directly by the thread-pool worker Task.Run hands DiscoverAsync to,
        // so observing it needs no dispatcher pumping -- just a bounded wait for that worker to run.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (fakeService.CallerThreadId is null && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        fakeService.CallerThreadId.Should().NotBeNull("DiscoverAsync must have been called at all");
        fakeService.CallerThreadId.Should().NotBe(
            openingThreadId,
            "printer discovery must run on a background thread (Task.Run), not the thread that opened " +
            "the print pane, so a slow spooler cannot block that caller even transiently");
    }

    /// <summary>
    /// Blocks synchronously inside DiscoverAsync until <see cref="Release"/> is called, the same way
    /// the real WindowsPrintService's spooler probe currently behaves (synchronous work wrapped in
    /// Task.FromResult). Lets tests prove the caller does not stall waiting for it.
    /// </summary>
    private sealed class GatedDiscoveryPrintService(PrinterDiscoveryResult result) : IPlatformPrintService
    {
        private readonly ManualResetEventSlim _gate = new(initialState: false);
        private int _callCount;

        public int CallCount => _callCount;

        public int? CallerThreadId { get; private set; }

        public bool IsSupported => true;

        public void Release() => _gate.Set();

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            CallerThreadId = Environment.CurrentManagedThreadId;
            _gate.Wait(cancellationToken);
            return Task.FromResult(result);
        }

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintSubmissionResult(PrintSubmissionStatus.Failed, selection.PrinterName));
    }

    private static string Read(params string[] pathParts) =>
        TestWorkspaceFileLocator.ReadAllText(pathParts);
}
