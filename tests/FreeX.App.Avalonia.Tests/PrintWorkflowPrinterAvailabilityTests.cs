using System.Reflection;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell.Avalonia;

using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The macOS print workflow talks to a CUPS queue through <see cref="IPlatformPrintService"/>. These
/// tests drive the real entry points (File &gt; Print backstage pane, Print Preview, the Print dialog)
/// with every printer-availability shape a Mac can be in -- a spooler that is unsupported, no queue
/// installed, one installed queue, a spooler that cancels, and discovery that throws -- and assert the
/// window survives each, the printer lists reflect the discovery result, and a faulting service is
/// contained instead of escaping an async-void handler (which would terminate the process).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class PrintWorkflowPrinterAvailabilityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private const string QueueName = "FreeX_Test_PDF";
    private const string SecondQueueName = "FreeX_Office";

    public static TheoryData<string> Shapes => new()
    {
        "unsupported",
        "no-printers",
        "one-printer",
        "cancelled",
        "throws",
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task BackstagePrintPane_BuildsAndFillsPrinterListPerDiscoveryResult(string shape) =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService(shape);
            await WithWindowAsync(service, async window =>
            {
                window.ShowBackstageOverlayForTest();
                typeof(MainWindow).GetMethod("TryActivateBackstagePane", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [FreeXBackstagePaneId.Print]);
                await SettleAsync();

                var overlay = (AvaloniaBackstageFrame)typeof(MainWindow)
                    .GetField("_backstageOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                var pane = overlay.CurrentPaneContent!;
                Find<Button>(pane, "BackstagePrintNowButton").Should().NotBeNull();

                AssertPrinterListMatches(pane, shape);
            });
            return true;
        }, CancellationToken.None);

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task PrintPreview_OpensAndFillsPrinterListPerDiscoveryResult(string shape) =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService(shape);
            await WithWindowAsync(service, async window =>
            {
                var dialog = await OpenModalAsync(window, "ShowPrintPreviewDialogAsync", [null, null, null]);

                AssertPrinterListMatches(dialog, shape);
            });
            return true;
        }, CancellationToken.None);

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task PrintDialog_OpensForEveryPrinterShapeWithoutThrowing(string shape) =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService(shape);
            await WithWindowAsync(service, async window =>
            {
                var method = typeof(MainWindow).GetMethod("ShowPrintDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var task = (Task)method.Invoke(window, null)!;
                await SettleAsync();

                if (shape == "throws")
                {
                    // File > Print awaits discovery before it builds the dialog: a throwing service
                    // surfaces on the returned task (the callers route it through RunGuarded), it must
                    // not take the process down and must not leave a half-built dialog behind.
                    await ((Func<Task>)(() => task)).Should().ThrowAsync<InvalidOperationException>();
                    window.OwnedWindows.Should().BeEmpty();
                    return;
                }

                window.OwnedWindows.Should().ContainSingle("File > Print opens one modal dialog");
                var dialog = window.OwnedWindows.Single();
                AutomationProperties.GetAutomationId(dialog).Should().NotBeNull();
                if (shape == "one-printer")
                    PrinterComboItems(dialog).Should().Contain(QueueName);
                else
                    PrinterComboItems(dialog).Should().NotContain(QueueName);

                dialog.Close();
                await SettleAsync();
            });
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task PrintPreview_SubmitsToolbarSettingsAndKeepsTheRailInSync() =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService("two-printers");
            await WithWindowAsync(service, async window =>
            {
                var dialog = await OpenModalAsync(window, "ShowPrintPreviewDialogAsync", [null, null, null]);
                var toolbarPrinter = Find<ComboBox>(dialog, "PrintPreviewToolbarPrinter");
                var railPrinter = Find<ComboBox>(dialog, "PrintPreviewSettingsPrinter");
                var collated = Find<CheckBox>(dialog, "PrintPreviewToolbarCollated");
                var railCollation = Find<ComboBox>(dialog, "PrintPreviewSettingsCollation");

                railPrinter.SelectedItem = SecondQueueName;
                toolbarPrinter.SelectedItem.Should().Be(SecondQueueName);
                railCollation.SelectedIndex = 1;
                collated.IsChecked.Should().BeFalse();

                toolbarPrinter.SelectedItem = QueueName;
                railPrinter.SelectedItem.Should().Be(QueueName);
                collated.IsChecked = true;
                railCollation.SelectedIndex.Should().Be(0);

                // Submit the non-default printer with the toolbar's copy, collation and duplex settings.
                toolbarPrinter.SelectedItem = SecondQueueName;
                Find<TextBox>(dialog, "PrintPreviewToolbarCopies").SetCurrentValue(TextBox.TextProperty, "3");
                Find<ComboBox>(dialog, "PrintPreviewToolbarSides").SetCurrentValue(ComboBox.SelectedIndexProperty, 2);
                collated.IsChecked = false;
                await SettleAsync();

                var printButton = Find<Button>(dialog, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId);
                printButton.IsEnabled.Should().BeTrue();
                printButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await SettleAsync();

                service.Submission.Should().Match<PrintSelection>(selection =>
                    selection.PrinterName == SecondQueueName && selection.Copies == 3 &&
                    selection.Sides == PrintSides.TwoSidedShortEdge && !selection.Collate);
            });
            return true;
        }, CancellationToken.None);

    [Theory]
    [InlineData(PrintPreviewPageRangeMode.AllPages, "2", "2", 1, 3)]
    [InlineData(PrintPreviewPageRangeMode.CurrentPage, "1", "1", 2, 2)]
    [InlineData(PrintPreviewPageRangeMode.Pages, "2", "3", 2, 3)]
    [InlineData(PrintPreviewPageRangeMode.Pages, "2", "", 2, 3)]
    [InlineData(PrintPreviewPageRangeMode.Pages, "", "2", 1, 2)]
    public Task PrintPreview_SubmitsChosenToolbarPageRange(
        PrintPreviewPageRangeMode mode, string fromPage, string toPage, int expectedFrom, int expectedTo) =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService("one-printer");
            await WithWindowAsync(service, async window =>
            {
                ConfigureThreePrintPages(window.Session.ActiveSheet);
                var dialog = await OpenModalAsync(window, "ShowPrintPreviewDialogAsync", [null, null, null]);
                var rangeMode = Find<ComboBox>(dialog, "PrintPreviewToolbarPageRange");
                rangeMode.SelectedIndex.Should().Be((int)PrintPreviewPageRangeMode.AllPages);

                Find<Button>(dialog, PrintPreviewDialogPlanner.NextPageButtonAutomationId)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Find<TextBox>(dialog, PrintPreviewDialogPlanner.PageNumberBoxAutomationId).Text.Should().Be("2");

                Find<TextBox>(dialog, "PrintPreviewSettingsFromPage").Text = fromPage;
                Find<TextBox>(dialog, "PrintPreviewSettingsToPage").Text = toPage;
                await SettleAsync();
                rangeMode.SelectedIndex.Should().Be((int)PrintPreviewPageRangeMode.Pages);

                rangeMode.SelectedIndex = (int)mode;
                Find<Button>(dialog, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await SettleAsync();

                service.Submission.Should().NotBeNull();
                service.Submission!.EffectivePageRange.FirstPage.Should().Be(expectedFrom);
                service.Submission.EffectivePageRange.LastPage.Should().Be(expectedTo);
            });

            return true;
        }, CancellationToken.None);

    [Theory]
    [InlineData("wrong", "2", "Export_PageRangeWholeNumbersError")]
    [InlineData("3", "2", "Export_PageRangeFromLessThanToError")]
    [InlineData("2", "4", "Export_PageRangeEndsAfterLastPage")]
    public Task PrintPreview_RejectsInvalidRailRangeWithoutClosingOrSubmitting(
        string fromPage, string toPage, string messageKey) =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService("one-printer");
            await WithWindowAsync(service, async window =>
            {
                ConfigureThreePrintPages(window.Session.ActiveSheet);
                var dialog = await OpenModalAsync(window, "ShowPrintPreviewDialogAsync", [null, null, null]);
                Find<TextBox>(dialog, "PrintPreviewSettingsFromPage").Text = fromPage;
                Find<TextBox>(dialog, "PrintPreviewSettingsToPage").Text = toPage;
                await SettleAsync();

                Find<Button>(dialog, PrintPreviewDialogPlanner.ExportPdfButtonAutomationId)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await SettleAsync();

                service.Submission.Should().BeNull();
                dialog.IsVisible.Should().BeTrue();
                var warning = dialog.OwnedWindows.Should().ContainSingle().Which;
                warning.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)
                    .Should().Contain(messageKey == "Export_PageRangeEndsAfterLastPage"
                        ? UiText.Format(messageKey, 3)
                        : UiText.Get(messageKey));
                warning.Close();
            });

            return true;
        }, CancellationToken.None);

    private static void ConfigureThreePrintPages(Sheet sheet)
    {
        sheet.PrintArea = GridRange.Parse("A1:A3", sheet.Id);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("second page"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("third page"));
        sheet.RowPageBreaks.Add(2);
        sheet.RowPageBreaks.Add(3);
    }

    [Fact]
    public Task FaultingDiscovery_FromPrintPreview_IsContainedAndTheWindowStaysUsable() =>
        Session.Dispatch(async () =>
        {
            var service = new FakePrintService("throws");
            await WithWindowAsync(service, async window =>
            {
                var dialog = await OpenModalAsync(window, "ShowPrintPreviewDialogAsync", [null, null, null]);
                await SettleAsync();

                service.DiscoverCalls.Should().BeGreaterThan(0, "the preview asks the spooler for printers when it opens");
                dialog.IsVisible.Should().BeTrue("a failing printer lookup must not close or crash the preview");
            });
            return true;
        }, CancellationToken.None);

    private static void AssertPrinterListMatches(Control root, string shape)
    {
        var items = PrinterComboItems(root);
        if (shape == "one-printer")
            items.Should().Contain(QueueName, "the discovered CUPS queue is offered as a destination");
        else
            items.Should().NotContain(QueueName, $"'{shape}' discovery must not invent a destination");
    }

    private static IReadOnlyList<string> PrinterComboItems(Control root) =>
        root.GetVisualDescendants().OfType<ComboBox>()
            .SelectMany(box => (box.ItemsSource ?? box.Items)?.OfType<string>() ?? [])
            .ToArray();

    private static T Find<T>(Control root, string automationId)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>()
            .First(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static async Task<Window> OpenModalAsync(MainWindow window, string method, object?[] args)
    {
        var show = typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = (Task)show.Invoke(window, args)!;
        await SettleAsync();
        window.OwnedWindows.Should().ContainSingle();
        return window.OwnedWindows.Single();
    }

    private static async Task WithWindowAsync(FakePrintService service, Func<MainWindow, Task> body)
    {
        var originalProduct = AppProduct.Current;
        var productDirectory = "FreeXPrintAvailabilityTest_" + Guid.NewGuid().ToString("N");
        AppProduct.Current = new AppProductIdentity(productDirectory, "PRINT_AVAILABILITY_TEST", productDirectory);
        var window = new MainWindow(
            [],
            WorkbookShareSheetServiceFactory.Create("macOS Share Sheet"),
            WorkbookFileAccessServiceFactory.Create(App.Diagnostics),
            service);
        try
        {
            window.Show();
            var sheet = window.Session.ActiveSheet;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("printable"));
            await SettleAsync();
            await body(window);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToList())
                owned.Close();

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
            AppProduct.Current = originalProduct;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                productDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 4; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(25);
        }
    }

    private sealed class FakePrintService(string shape) : IPlatformPrintService
    {
        public int DiscoverCalls { get; private set; }
        public PrintSelection? Submission { get; private set; }

        public bool IsSupported => shape != "unsupported";

        public Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            return shape switch
            {
                "no-printers" => Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.NoPrinters, [], null, "No printers are installed or available.")),
                "one-printer" => Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Available, [new PrinterInfo(QueueName, IsDefault: true)], QueueName)),
                "two-printers" => Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Available,
                    [new PrinterInfo(QueueName, IsDefault: true), new PrinterInfo(SecondQueueName)],
                    QueueName)),
                "cancelled" => Task.FromResult(new PrinterDiscoveryResult(
                    PrinterDiscoveryStatus.Cancelled, [], null, "Printer discovery was cancelled.")),
                "throws" => throw new InvalidOperationException("spooler exploded"),
                _ => Task.FromResult(new PrinterDiscoveryResult(PrinterDiscoveryStatus.Unavailable, [], null)),
            };
        }

        public Task<PrintSubmissionResult> SubmitAsync(
            string pdfPath,
            PrintSelection selection,
            CancellationToken cancellationToken = default)
        {
            Submission = selection;
            return Task.FromResult(new PrintSubmissionResult(PrintSubmissionStatus.Submitted, selection.PrinterName));
        }
    }
}
