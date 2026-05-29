using System.IO;

namespace FreeX.App.Host;

public static class WorkbookTitleFormatter
{
    public static string Format(string workbookName, bool isDirty, bool isGrouped)
    {
        var groupSuffix = isGrouped ? " [Group]" : "";
        var dirtySuffix = isDirty ? "*" : "";
        return $"{workbookName}{groupSuffix}{dirtySuffix} - FreeX";
    }

    public static string DisplayNameFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path);
}
