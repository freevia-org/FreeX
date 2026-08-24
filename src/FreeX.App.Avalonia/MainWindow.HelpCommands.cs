using Avalonia.Controls;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task CopyDiagnosticsToClipboardAsync()
    {
        var context = CreateIssueReportContext();
        var diagnosticsText = AppIssueReporter.CreateDiagnosticsText(context);
        try
        {
            var write = await _platformClipboard.WriteAsync(
                new PlatformClipboardContent(Text: diagnosticsText));
            if (write.Status == PlatformClipboardWriteStatus.Unavailable)
            {
                ShowHelpIssue("Clipboard unavailable on this platform.");
                return;
            }
            if (!write.IsSuccess)
                throw new InvalidOperationException(write.ErrorMessage);
            App.Diagnostics?.RecordEvent("diagnostics_copied", new Dictionary<string, string?>
            {
                ["source"] = "help"
            });
            RefreshShell(UiText.Get("MainWindowMessage_DiagnosticsCopied"));
        }
        catch (Exception ex)
        {
            ShowHelpIssue(UiText.Format("MainWindowMessage_DiagnosticsCopyFailed", ex.Message));
        }
    }

    private Task SendCrashAnalyticsTestReportAsync()
    {
        var result = AppCrashAnalyticsRuntime.SendTestReport();
        var message = AppCrashAnalyticsRuntime.UserMessage(result);
        if (result == CrashAnalyticsTestReportResult.Sent)
            RefreshShell(message);
        else
            ShowHelpIssue(message);
        return Task.CompletedTask;
    }

    private AppIssueReportContext CreateIssueReportContext()
    {
        var metadata = App.Diagnostics?.Metadata
            ?? AppDiagnosticsMetadata.Create(AppHelpInfo.GetVersionText(typeof(MainWindow).Assembly));
        return AppIssueReporter.CreateContext(
            AppHelpInfo.FeedbackUrl,
            metadata,
            App.Diagnostics?.IsEnabled == true,
            typeof(MainWindow).Assembly);
    }
}
