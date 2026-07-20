using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class WpfClipboardCommands
{
    public static void Copy(EditingSession editor, OsClipboardService? osClipboard)
    {
        editor.CopySelectedShapes();
        osClipboard?.PlaceSelectionOnOsClipboard(editor);
    }

    public static void Cut(EditingSession editor, OsClipboardService? osClipboard)
    {
        Copy(editor, osClipboard);
        editor.DeleteSelected();
    }
}
