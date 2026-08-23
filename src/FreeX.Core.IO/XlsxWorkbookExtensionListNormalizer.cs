using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookExtensionListNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs) =>
        XlsxExtensionListNormalizer.NormalizeRoot(workbookRoot, workbookNs);

    public static bool NormalizeExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.NormalizeElement(extensionList, WorkbookNs);

    public static bool ShouldRemoveExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.ShouldRemove(extensionList, WorkbookNs);

    public static bool NormalizeParent(XElement parent)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in parent.Elements(WorkbookNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeExtensionListElement(extensionList);
            if (ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }
}
