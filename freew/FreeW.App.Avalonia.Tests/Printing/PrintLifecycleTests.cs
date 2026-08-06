using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

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
            callbacks.ExportXps.Should().NotBeNull("Avalonia uses the portable XPS writer");
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

    [Fact]
    public async Task CtrlP_uses_direct_print_selection_instead_of_opening_preview()
    {
        var dialogCalls = 0;
        await Session.Dispatch(() =>
        {
            var window = CreateWindow(
                new FakePrintService(isSupported: true),
                showPrintSelectionDialog: (_, _, _) =>
                {
                    dialogCalls++;
                    return Task.FromResult<PrintSelection?>(null);
                });

            var args = new KeyEventArgs
            {
                Key = Key.P,
                KeyModifiers = KeyModifiers.Control,
            };
            window.RaiseKeyDownForTest(args);
            args.Handled.Should().BeTrue();
        }, CancellationToken.None);

        dialogCalls.Should().Be(1,
            "Ctrl+P must enter the direct printer-selection workflow; Print Preview is a separate command");
    }

    [Fact]
    public async Task MainWindow_CupsAvailabilityDoesNotClaimNativeSystemPrintDialog()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow(
                new FakePrintService(isSupported: true),
                showPrintSelectionDialog: (_, _, _) => Task.FromResult<PrintSelection?>(null));

            await window.PrintAsync();

            var capability = window.BuildBackstageCallbacks().DirectPrintCapability;
            capability!.IsAvailable.Should().BeTrue();
            capability.FieldValue.Should().Contain("platform printer submission");
            capability.FieldValue.Should().NotContain("operating-system printer dialog");
            capability.ActionDescription.Should().Contain("platform printer service");
            capability.ActionDescription.Should().NotContain("native");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindow_NoPrintersDisablesDirectPrintAndReportsPdfFallback()
    {
        var restoreCalls = 0;
        await Session.Dispatch(async () =>
        {
            var window = CreateWindow(
                new FakePrintService(isSupported: true, discoveryStatus: PrinterDiscoveryStatus.NoPrinters),
                restorePrintOwnerFocus: _ => restoreCalls++);

            await window.PrintAsync();

            var callbacks = window.BuildBackstageCallbacks();
            callbacks.DirectPrintCapability!.IsAvailable.Should().BeFalse();
            callbacks.Print.Should().BeNull();
            window.PrintStatusForTests.Should().Contain("No printers");
            window.PrintStatusForTests.Should().Contain("Create PDF");
        }, CancellationToken.None);

        restoreCalls.Should().Be(1);
    }

    [Fact]
    public async Task FinishMergePrinter_prints_selected_record_without_replacing_preview_or_session()
    {
        string? exportedText = null;
        var printService = new FakePrintService(isSupported: true);

        await Session.Dispatch(async () =>
        {
            var window = CreateWindow(
                printService,
                showPrintSelectionDialog: (_, _, _) =>
                    Task.FromResult<PrintSelection?>(new PrintSelection("Office")),
                savePrintPdf: (view, stream) =>
                {
                    exportedText = view.Document.PlainText;
                    stream.Write(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 test"));
                });
            var template = TextDocument.CreateEmpty();
            template.Blocks.Clear();
            template.Blocks.Add(new Paragraph(
                $"Dear {MailMerge.FieldOpen}FirstName{MailMerge.FieldClose}"));
            window.Editor.LoadDocument(template);

            var engine = window.MailMergeForTests;
            engine.LoadRecipientsCsv("FirstName\nAda\nGrace");
            engine.TogglePreview();
            engine.NextRecord();
            var visiblePreview = window.Editor.Document;
            var stashedTemplate = engine.Session.Template;
            var recipients = engine.Session.Data;
            var mapping = engine.Session.Mapping;
            var plan = MailMergeFinishPlanner.Plan(
                MailMergeFinishDestination.Printer,
                MailMergeRecipientScope.CurrentRecord,
                recordCount: 2,
                currentIndex: 1,
                fromRecordText: null,
                toRecordText: null);

            await window.ExecuteFinishMergePlanForTests(plan);

            exportedText.Should().Contain("Dear Grace");
            exportedText.Should().NotContain("Dear Ada");
            window.Editor.Document.Should().BeSameAs(visiblePreview);
            engine.Session.Template.Should().BeSameAs(stashedTemplate);
            engine.Session.Data.Should().BeSameAs(recipients);
            engine.Session.Mapping.Should().BeSameAs(mapping);
            engine.Session.CurrentIndex.Should().Be(1);
            engine.Session.IsPreviewing.Should().BeTrue();
        }, CancellationToken.None);

        printService.SubmittedFileExisted.Should().BeTrue();
        printService.SubmittedPdfPath.Should().NotBeNull();
        File.Exists(printService.SubmittedPdfPath!).Should().BeFalse("PrintAsync cleans its temporary merged PDF");
    }

    private static MainWindow CreateWindow(
        IPlatformPrintService printService,
        Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>>? showPrintSelectionDialog = null,
        Action<IInputElement?>? restorePrintOwnerFocus = null,
        Action<DocumentView, Stream>? savePrintPdf = null)
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
            restorePrintOwnerFocus: restorePrintOwnerFocus,
            savePrintPdf: savePrintPdf);
    }

    private sealed class FakePrintService(
        bool isSupported,
        PrinterDiscoveryStatus discoveryStatus = PrinterDiscoveryStatus.Available) : IPlatformPrintService
    {
        public bool IsSupported { get; } = isSupported;
        public string? SubmittedPdfPath { get; private set; }
        public bool SubmittedFileExisted { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            SubmittedPdfPath = pdfPath;
            SubmittedFileExisted = File.Exists(pdfPath);
            return Task.FromResult(new PrintSubmissionResult(PrintSubmissionStatus.Submitted, selection.PrinterName));
        }
    }
}
