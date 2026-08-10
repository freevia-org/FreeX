using Avalonia.Controls;
using Avalonia.Input.Platform;
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
        if (result == ExternalUriLaunchResult.Launched)
            return;

        await FreeWInfoDialog.ShowAsync(
            this,
            FreeWApplicationFrameTextCatalog.FormatExternalLinkFailure(title, url),
            title);
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
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null)
        {
            await ShowHelpMessageAsync(
                FreeWApplicationFrameTextCatalog.ClipboardUnavailableMessage,
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle);
            return;
        }

        try
        {
            await clipboard.SetTextAsync(diagnosticsText);
            await ShowHelpMessageAsync(
                FreeWApplicationFrameTextCatalog.DiagnosticsCopiedMessage,
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle);
        }
        catch (Exception ex)
        {
            await ShowHelpMessageAsync(
                FreeWApplicationFrameTextCatalog.FormatClipboardFailure(ex.Message),
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle);
        }
    }

    private async Task ShowHelpMessageAsync(string message, string title)
    {
        await FreeWInfoDialog.ShowAsync(this, message, title);
        _editor.Focus();
    }

    private static Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target) =>
        Task.FromResult(DesktopExternalUriLauncher.Open(target));
}
