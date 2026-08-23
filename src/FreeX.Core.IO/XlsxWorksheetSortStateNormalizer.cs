using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSortStateNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidSortMethods = ["stroke", "pinYin"];

    private static readonly HashSet<string> ValidSortByValues = ["value", "cellColor", "fontColor", "icon"];
    private static readonly HashSet<string> SortStateChildren = ["sortCondition", "extLst"];

    public static bool NormalizeElement(XElement sortState)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(sortState, WorksheetNs, SortStateChildren);
        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(sortState);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "columnSort", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "caseSensitive", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "sortMethod", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidSortMethods));

        foreach (var condition in sortState.Elements(WorksheetNs + "sortCondition").ToList())
        {
            if (string.IsNullOrWhiteSpace(condition.Attribute("ref")?.Value))
            {
                condition.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "descending", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "sortBy", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidSortByValues));
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "dxfId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "iconId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(condition);
        }

        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(sortState, SortStateChildOrder);
        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var sortState = worksheetRoot.Element(WorksheetNs + "sortState");
        return sortState is not null && NormalizeElement(sortState);
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is not null &&
                NormalizeWorksheetRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    private static int SortStateChildOrder(XElement child) =>
        child.Name == WorksheetNs + "sortCondition" ? 0 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

}
