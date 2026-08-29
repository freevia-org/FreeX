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
        // r169 follow-up: report the file this window ACTUALLY loads and saves. The old path-planner
        // label named %LOCALAPPDATA%\FreeW\options.json -- wrong twice over, since FreeW keeps its
        // options in settings.json under %APPDATA% (the planner resolves FreeX's file name). Support
        // reports were pointing at a path no FreeW install has ever had.
        var optionsPath = _optionsStore.StorePath;
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
            FreeWUiTextCatalog.TestCrashReportingTitle);

    private async Task ShowHelpMessageAsync(string message, string title)
    {
        await FreeWInfoDialog.ShowAsync(this, message, title);
        _editor.Focus();
    }

    private static Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target) =>
        Task.FromResult(DesktopExternalUriLauncher.Open(target));
}
