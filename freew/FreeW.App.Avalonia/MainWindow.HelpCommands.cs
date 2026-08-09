using Avalonia.Controls;
using Avalonia.Input.Platform;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

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
            $"FreeW could not open {title}. The link is:\n\n{url}",
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
                "FreeW could not access the clipboard.",
                "Copy Diagnostics");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(diagnosticsText);
            await ShowHelpMessageAsync(
                "FreeW diagnostics were copied to the clipboard.",
                "Copy Diagnostics");
        }
        catch (Exception ex)
        {
            await ShowHelpMessageAsync(
                $"FreeW could not access the clipboard: {ex.Message}",
                "Copy Diagnostics");
        }
    }

    private async Task ShowHelpMessageAsync(string message, string title)
    {
        await FreeWInfoDialog.ShowAsync(this, message, title);
        _editor.Focus();
    }

    private async Task<ExternalUriLaunchResult> OpenExternalUriAsync(string target)
    {
        return await AvaloniaExternalUriLauncher.OpenAsync(this, target);
    }
}
