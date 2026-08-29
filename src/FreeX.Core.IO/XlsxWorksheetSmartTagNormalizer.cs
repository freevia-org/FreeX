using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSmartTagNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var smartTagContainers = worksheetRoot.Elements(WorksheetNs + "smartTags").ToList();
        if (smartTagContainers.Count == 0)
            return false;

        foreach (var smartTags in smartTagContainers)
            smartTags.Remove();
        return true;
    }

}
