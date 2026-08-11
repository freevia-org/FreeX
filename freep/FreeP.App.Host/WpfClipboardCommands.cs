using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class WpfClipboardCommands
{
    /// <param name="onWriteFailed">
    /// Invoked with <see cref="OsClipboardService.LastWriteFailureMessage"/> when the OS-clipboard
    /// write fails, so callers can surface it (e.g. to the status bar) instead of the failure
    /// vanishing silently while the user believes the copy succeeded.
    /// </param>
    public static void Copy(EditingSession editor, OsClipboardService? osClipboard, Action<string>? onWriteFailed = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var request = osClipboard?.PrepareWrite(editor)
            ?? PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        PresentationClipboardWorkflow.CommitCopy(request);
        if (osClipboard is not null &&
            !osClipboard.TryWrite(request)
            && osClipboard.LastWriteFailureMessage is { } error)
        {
            onWriteFailed?.Invoke(error);
        }
    }

    public static void Cut(EditingSession editor, OsClipboardService? osClipboard, Action<string>? onWriteFailed = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var request = osClipboard?.PrepareWrite(editor)
            ?? PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        PresentationClipboardWorkflow.CommitCut(
            request,
            osClipboard is null
                ? null
                : () =>
                {
                    var written = osClipboard.TryWrite(request);
                    if (!written && osClipboard.LastWriteFailureMessage is { } error)
                        onWriteFailed?.Invoke(error);
                });
    }
}
