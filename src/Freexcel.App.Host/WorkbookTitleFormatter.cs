using System.IO;

namespace Freexcel.App.Host;

public static class WorkbookTitleFormatter
{
    public static string Format(string workbookName, bool isDirty, bool isGrouped)
    {
        var groupSuffix = isGrouped ? " [Group]" : "";
        var dirtySuffix = isDirty ? "*" : "";
        return $"{workbookName}{groupSuffix}{dirtySuffix} - Freexcel";
    }

    public static string DisplayNameFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path);
}
