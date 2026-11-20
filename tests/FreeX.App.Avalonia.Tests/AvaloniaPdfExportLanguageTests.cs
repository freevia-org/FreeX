using System.Reflection;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaPdfExportLanguageTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AcceptedPdfRequestPersistsLanguageForTheNextExportDialog()
    {
        await Session.Dispatch(async () =>
        {
            var storedOptions = new AppOptions { PdfExportLanguage = "de-de" };
            var saveCount = 0;
            var optionsSession = new FreeXOptionsRuntimeSession(
                load: () => storedOptions.Clone(),
                save: options =>
                {
                    storedOptions = options.Clone();
                    saveCount++;
                    return true;
                });
            using var workbookSession = new WorkbookSessionFactory().CreateNew(viewportHeight: 720, viewportWidth: 1120);
            var window = new MainWindow([], workbookSession, optionsSession);
            try
            {
                window.Show();
                var (pdfDialog, pdfCompletion) = await OpenOptionsAsync(window, ExportFormat.Pdf);
                var language = GetLanguageBox(pdfDialog);
                language.Text.Should().Be("de-DE");
                language.Text = " fr-ca ";
                ClickButton(pdfDialog, button => button.IsDefault);
                var accepted = await pdfCompletion;
                accepted.Should().NotBeNull();
                accepted!.PdfLanguage.Should().Be("fr-CA");
                saveCount.Should().Be(0);

                var request = WorkbookExportInteractionPlanner.CreateRequestPlan(
                    "workbook.pdf", ExportFormat.Pdf, accepted!, _ => false);
                PersistLanguage(window, request);
                storedOptions.PdfExportLanguage.Should().Be("fr-CA");
                saveCount.Should().Be(1);

                var (nextPdfDialog, nextPdfCompletion) = await OpenOptionsAsync(window, ExportFormat.Pdf);
                var nextLanguage = GetLanguageBox(nextPdfDialog);
                nextLanguage.Text.Should().Be("fr-CA");
                nextLanguage.Text = "it-IT";
                ClickButton(nextPdfDialog, button => button.IsCancel);
                (await nextPdfCompletion).Should().BeNull();

                var (xpsDialog, xpsCompletion) = await OpenOptionsAsync(window, ExportFormat.Xps);
                var xpsLanguage = GetLanguageBox(xpsDialog);
                xpsLanguage.IsEnabled.Should().BeFalse();
                xpsLanguage.Text.Should().Be("fr-CA");
                ClickButton(xpsDialog, button => button.IsDefault);
                var xpsOptions = await xpsCompletion;
                xpsOptions.Should().NotBeNull();
                var xpsRequest = WorkbookExportInteractionPlanner.CreateRequestPlan(
                    "workbook.xps", ExportFormat.Xps, xpsOptions!, _ => false);
                PersistLanguage(window, xpsRequest);
                storedOptions.PdfExportLanguage.Should().Be("fr-CA");
                saveCount.Should().Be(1);
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                    owned.Close();
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task<(Window Dialog, Task<ExportOptions?> Completion)> OpenOptionsAsync(
        MainWindow window,
        ExportFormat format)
    {
        var method = typeof(MainWindow).GetMethod("ShowExportOptionsDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var completion = (Task<ExportOptions?>)method.Invoke(window, [ExportContentScope.ActiveSheet, format])!;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var dialog = window.OwnedWindows.Single(owned =>
            AutomationProperties.GetAutomationId(owned) == ExportOptionsDialogSurfacePlanner.DialogAutomationId);
        return (dialog, completion);
    }

    private static TextBox GetLanguageBox(Window dialog) =>
        dialog.GetLogicalDescendants().OfType<TextBox>()
            .Single(box => AutomationProperties.GetAutomationId(box) == "PdfExportLanguageBox");

    private static void ClickButton(Window dialog, Func<Button, bool> predicate) =>
        dialog.GetLogicalDescendants().OfType<Button>().Single(predicate)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void PersistLanguage(MainWindow window, WorkbookExportRequestPlan requestPlan) =>
        typeof(MainWindow).GetMethod("PersistPdfExportLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [requestPlan]);
}
