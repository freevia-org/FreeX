using Avalonia.Controls;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task OpenExternalHelpLinkAsync(string url, string title)
    {
        var result = await OpenExternalUriAsync(url);
        if (FreeWSupportCommandFeedbackPlanner.PlanExternalUriLaunch(result, title, url) is not { } feedback)
            return;

        await FreeWInfoDialog.ShowAsync(
            this,
            feedback.Message,
            feedback.Title);
        _editor.Focus();
    }

    private async Task CopyDiagnosticsAsync()
    {
        var diagnosticsDirectory = AppStoragePathPlanner.GetDiagnosticsDirectory(
            PlatformAppDiagnosticsPathProvider.Instance);
        var optionsPath = AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(
            PlatformApplicationDataPathProvider.LocalInstance);
        var diagnosticsText = FreeWProductInfo.CreateDiagnosticsText(
            typeof(MainWindow).Assembly,
            diagnosticsDirectory,
            optionsPath);
        var write = await _platformClipboard.WriteAsync(
            new PlatformClipboardContent(Text: diagnosticsText));
        var feedback = FreeWSupportCommandFeedbackPlanner.PlanDiagnosticsCopy(write);
        await ShowHelpMessageAsync(feedback.Message, feedback.Title);
    }

    private Task TestCrashReportingAsync() =>
        ShowHelpMessageAsync(
            AppCrashAnalyticsRuntime.UserMessage(AppCrashAnalyticsRuntime.SendTestReport()),
            UiText.Get("Help_TestCrashReporting_Title"));

    private async Task ShowHelpMessageAsync(string message, string title)
    {
        await FreeWInfoDialog.ShowAsync(this, message, title);
        _editor.Focus();
    }

    private static Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target) =>
        Task.FromResult(DesktopExternalUriLauncher.Open(target));
}
