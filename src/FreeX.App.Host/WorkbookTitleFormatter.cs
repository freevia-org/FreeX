using System.IO;

namespace FreeX.App.Host;

public static class WorkbookTitleFormatter
{
    private const string ApplicationTitle = "FreeX";
    private const string GroupSuffix = " [Group]";
    private const string DirtySuffix = "*";

    public static string Format(string workbookName, bool isDirty, bool isGrouped)
    {
        var groupSuffix = isGrouped ? GroupSuffix : "";
        var dirtySuffix = isDirty ? DirtySuffix : "";
        return $"{workbookName}{groupSuffix}{dirtySuffix} - {ApplicationTitle}";
    }

    public static string DisplayNameFromPath(string path) =>
        Path.GetFileNameWithoutExtension(path);
}
