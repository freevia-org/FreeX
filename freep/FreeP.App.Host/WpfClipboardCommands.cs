using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class WpfClipboardCommands
{
    public static void Copy(EditingSession editor, OsClipboardService? osClipboard)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var request = osClipboard?.PrepareWrite(editor)
            ?? PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        PresentationClipboardWorkflow.CommitCopy(request);
        _ = osClipboard?.TryWrite(request);
    }

    public static void Cut(EditingSession editor, OsClipboardService? osClipboard)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var request = osClipboard?.PrepareWrite(editor)
            ?? PresentationClipboardWorkflow.PrepareInternalWrite(editor);
        PresentationClipboardWorkflow.CommitCut(
            request,
            osClipboard is null ? null : () => osClipboard.TryWrite(request));
    }
}
