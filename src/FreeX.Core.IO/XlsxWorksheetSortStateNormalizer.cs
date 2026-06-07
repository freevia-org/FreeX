using System.Globalization;
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
        changed |= RemoveUnexpectedChildren(sortState, SortStateChildren);
        changed |= NormalizeExtensionLists(sortState);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "columnSort", NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "caseSensitive", NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(sortState, "sortMethod", value => NormalizeToken(value, ValidSortMethods));

        foreach (var condition in sortState.Elements(WorksheetNs + "sortCondition").ToList())
        {
            if (string.IsNullOrWhiteSpace(condition.Attribute("ref")?.Value))
            {
                condition.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "descending", NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "sortBy", value => NormalizeToken(value, ValidSortByValues));
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "dxfId", NormalizeUnsignedIntOrNull);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(condition, "iconId", NormalizeUnsignedIntOrNull);
            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(condition);
        }

        changed |= NormalizeChildOrder(sortState, SortStateChildOrder);
        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var sortState = worksheetRoot.Element(WorksheetNs + "sortState");
        return sortState is not null && NormalizeElement(sortState);
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
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

    private static bool NormalizeExtensionLists(XElement parent)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in parent.Elements(WorksheetNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeExtensionListElement(extensionList);
            if (XlsxWorksheetExtensionListNormalizer.ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    private static bool RemoveUnexpectedChildren(XElement element, IReadOnlySet<string> allowedLocalNames)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name.Namespace == WorksheetNs && allowedLocalNames.Contains(child.Name.LocalName))
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeChildOrder(XElement element, Func<XElement, int> orderSelector)
    {
        var children = element.Elements()
            .Select((child, index) => new { Child = child, Index = index })
            .OrderBy(item => orderSelector(item.Child))
            .ThenBy(item => item.Index)
            .Select(item => item.Child)
            .ToList();
        if (children.Count == 0 || element.Elements().SequenceEqual(children))
            return false;

        element.ReplaceNodes(children);
        return true;
    }

    private static int SortStateChildOrder(XElement child) =>
        child.Name == WorksheetNs + "sortCondition" ? 0 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
