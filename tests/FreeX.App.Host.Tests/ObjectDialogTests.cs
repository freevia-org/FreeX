namespace FreeX.App.Host.Tests;

public sealed partial class ObjectDialogTests
{
    private static string ReadObjectDialogSources() =>
        DialogSourceTestSupport.ReadHostSources(
            "HyperlinkDialog.cs",
            "TextEntryDialogs.cs",
            "ThreadedCommentDialog.cs",
            "ObjectSizingDialogs.cs");

    private static string ReadClassSource(string fileName, string startMarker, string endMarker)
        => DialogSourceTestSupport.ReadClassSource(fileName, startMarker, endMarker);

    private static T GetField<T>(object instance, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(instance, name);
}
