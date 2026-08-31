using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private async Task CopySheetTabLinkToClipboardAsync(WorkbookSheetTab tab)
    {
        var write = await _platformClipboard.WriteAsync(new PlatformClipboardContent(
            Text: SheetTabLinkFormatter.BuildClipboardText(tab.Name)));
        if (write.Status == PlatformClipboardWriteStatus.Unavailable)
        {
            ShowEditIssue(UiText.Get("Clipboard_UnavailableOnPlatform"));
            return;
        }
        if (!write.IsSuccess)
        {
            ShowEditIssue(UiText.Format("SheetTabContext_LinkCopyFailed", write.ErrorMessage));
            return;
        }

        RefreshShell(UiText.Get("SheetTabContext_LinkCopied"));
    }
}
