using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class PrintLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task MainWindow_GatesBackstagePrintByInjectedPlatformCapability()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateWindow(new FakePrintService(isSupported: false));
            var callbacks = window.BuildBackstageCallbacks();

            callbacks.DirectPrintCapability.Should().NotBeNull();
            callbacks.DirectPrintCapability!.IsAvailable.Should().BeFalse();
            callbacks.Print.Should().BeNull();
            callbacks.ExportXps.Should().BeNull("XPS is WPF-only");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_CancellationRestoresOwnerFocusThroughInjectedLifecycleHook()
    {
        var restoreCalls = 0;
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow(
                new FakePrintService(isSupported: true, discoveryStatus: PrinterDiscoveryStatus.Cancelled),
                restorePrintOwnerFocus: _ => restoreCalls++);

            await window.PrintAsync();
        }, CancellationToken.None);

        restoreCalls.Should().Be(1);
    }

    [Fact]
    public async Task MainWindow_DialogCancellationRestoresOwnerFocusWithoutRenderingOrSpooling()
    {
        var restoreCalls = 0;
        var dialogCalls = 0;
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow(
                new FakePrintService(isSupported: true),
                showPrintSelectionDialog: (_, _, _) =>
                {
                    dialogCalls++;
                    return Task.FromResult<PrintSelection?>(null);
                },
                restorePrintOwnerFocus: _ => restoreCalls++);

            await window.PrintAsync();
        }, CancellationToken.None);

        dialogCalls.Should().Be(1);
        restoreCalls.Should().Be(1);
    }

    private static MainWindow CreateWindow(
        IPlatformPrintService printService,
        Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>>? showPrintSelectionDialog = null,
        Action<IInputElement?>? restorePrintOwnerFocus = null)
    {
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            "FreeW.PrintLifecycleTests",
            Guid.NewGuid().ToString("N"),
            "settings.json");
        return new MainWindow(
            [],
            new FreeWOptions(),
            ApplicationOptionsStore<FreeWOptions>.ForPath(settingsPath),
            printService: printService,
            showPrintSelectionDialog: showPrintSelectionDialog,
            restorePrintOwnerFocus: restorePrintOwnerFocus);
    }

    private sealed class FakePrintService(
        bool isSupported,
        PrinterDiscoveryStatus discoveryStatus = PrinterDiscoveryStatus.Available) : IPlatformPrintService
    {
        public bool IsSupported { get; } = isSupported;

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new PrinterDiscoveryResult(
                    discoveryStatus,
                    discoveryStatus == PrinterDiscoveryStatus.Available
                        ? [new PrinterInfo("Office", IsDefault: true)]
                        : [],
                    discoveryStatus == PrinterDiscoveryStatus.Available ? "Office" : null,
                    discoveryStatus == PrinterDiscoveryStatus.Cancelled ? "Printer discovery was cancelled." : null));

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintSubmissionResult(PrintSubmissionStatus.Submitted, selection.PrinterName));
    }
}
