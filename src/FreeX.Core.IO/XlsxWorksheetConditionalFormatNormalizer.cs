using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetConditionalFormatNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> ConditionalFormattingAttributes = ["sqref", "pivot"];
    private static readonly HashSet<string> ConditionalFormattingChildren = ["cfRule", "extLst"];
    private static readonly HashSet<string> CfRuleAttributes =
    [
        "aboveAverage",
        "bottom",
        "dxfId",
        "equalAverage",
        "operator",
        "percent",
        "priority",
        "rank",
        "stdDev",
        "stopIfTrue",
        "text",
        "timePeriod",
        "type"
    ];
    private static readonly HashSet<string> CfRuleChildren = ["formula", "colorScale", "dataBar", "iconSet", "extLst"];

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            if (!NormalizeWorksheet(worksheetXml))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    internal static bool NormalizeWorksheet(XDocument worksheetXml)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var changed = false;
        foreach (var conditionalFormatting in root.Elements(WorksheetNs + "conditionalFormatting").ToList())
            changed |= NormalizeConditionalFormatting(conditionalFormatting);

        return changed;
    }

    private static bool NormalizeConditionalFormatting(XElement conditionalFormatting)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(conditionalFormatting, ConditionalFormattingAttributes);
        changed |= RemoveUnexpectedChildren(conditionalFormatting, ConditionalFormattingChildren);
        changed |= RemoveDuplicateChildren(conditionalFormatting, "extLst");

        foreach (var rule in conditionalFormatting.Elements(WorksheetNs + "cfRule").ToList())
            changed |= NormalizeCfRule(rule);

        changed |= NormalizeChildOrder(conditionalFormatting, ConditionalFormattingChildOrder);
        return changed;
    }

    private static bool NormalizeCfRule(XElement rule)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(rule, CfRuleAttributes);
        changed |= RemoveUnexpectedChildren(rule, CfRuleChildren);
        changed |= RemoveDuplicateChildren(rule, "colorScale");
        changed |= RemoveDuplicateChildren(rule, "dataBar");
        changed |= RemoveDuplicateChildren(rule, "iconSet");
        changed |= RemoveDuplicateChildren(rule, "extLst");
        changed |= NormalizeChildOrder(rule, CfRuleChildOrder);
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
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

    private static bool RemoveDuplicateChildren(XElement element, string localName)
    {
        var changed = false;
        var seen = false;
        foreach (var child in element.Elements(WorksheetNs + localName).ToList())
        {
            if (!seen)
            {
                seen = true;
                continue;
            }

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

    private static int ConditionalFormattingChildOrder(XElement child) =>
        child.Name == WorksheetNs + "cfRule" ? 0 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static int CfRuleChildOrder(XElement child) =>
        child.Name == WorksheetNs + "formula" ? 0 :
        child.Name == WorksheetNs + "colorScale" ? 10 :
        child.Name == WorksheetNs + "dataBar" ? 20 :
        child.Name == WorksheetNs + "iconSet" ? 30 :
        child.Name == WorksheetNs + "extLst" ? 100 :
        90;

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = entry.FullName;
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
