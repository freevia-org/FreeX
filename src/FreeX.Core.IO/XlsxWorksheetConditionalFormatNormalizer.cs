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
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            if (!NormalizeWorksheet(worksheetXml))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        foreach (var conditionalFormatting in worksheetRoot.Elements(WorksheetNs + "conditionalFormatting").ToList())
            changed |= NormalizeConditionalFormatting(conditionalFormatting);

        return changed;
    }

    internal static bool NormalizeWorksheet(XDocument worksheetXml)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        return NormalizeWorksheetRoot(root);
    }

    private static bool NormalizeConditionalFormatting(XElement conditionalFormatting)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(conditionalFormatting, ConditionalFormattingAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(
            conditionalFormatting,
            WorksheetNs,
            ConditionalFormattingChildren);
        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(conditionalFormatting);

        foreach (var rule in conditionalFormatting.Elements(WorksheetNs + "cfRule").ToList())
            changed |= NormalizeCfRule(rule);

        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(conditionalFormatting, ConditionalFormattingChildOrder);
        return changed;
    }

    private static bool NormalizeCfRule(XElement rule)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(rule, CfRuleAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(rule, WorksheetNs, CfRuleChildren);
        changed |= XlsxWorksheetExtensionListNormalizer.RemoveDuplicateChildren(rule, "colorScale");
        changed |= XlsxWorksheetExtensionListNormalizer.RemoveDuplicateChildren(rule, "dataBar");
        changed |= XlsxWorksheetExtensionListNormalizer.RemoveDuplicateChildren(rule, "iconSet");
        changed |= RemovePayloadExtensionLists(rule);
        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChildren(rule);
        changed |= XlsxXmlNormalizationHelpers.NormalizeChildOrder(rule, CfRuleChildOrder);
        return changed;
    }

    private static bool RemovePayloadExtensionLists(XElement rule)
    {
        var changed = false;
        foreach (var payload in rule.Elements(WorksheetNs + "colorScale")
                     .Concat(rule.Elements(WorksheetNs + "dataBar"))
                     .Concat(rule.Elements(WorksheetNs + "iconSet")))
        {
            changed |= RemoveExtensionLists(payload);
        }

        return changed;
    }

    private static bool RemoveExtensionLists(XElement parent)
    {
        var extensionLists = parent.Elements(WorksheetNs + "extLst").ToList();
        if (extensionLists.Count == 0)
            return false;

        foreach (var extensionList in extensionLists)
            extensionList.Remove();

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

}
