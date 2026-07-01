using Avalonia.Controls;
using Avalonia.Input.Platform;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task CopyDiagnosticsToClipboardAsync()
    {
        var context = CreateIssueReportContext();
        var diagnosticsText = AppIssueReporter.CreateDiagnosticsText(context);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            ShowHelpIssue("Clipboard unavailable on this platform.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(diagnosticsText);
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
