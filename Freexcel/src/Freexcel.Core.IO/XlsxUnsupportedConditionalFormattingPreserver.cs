using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxUnsupportedConditionalFormattingPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var sourceWorksheetEntry in sourceArchive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var targetWorksheetEntry = targetArchive.GetEntry(sourceWorksheetEntry.FullName);
            if (targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            var unsupportedBlocks = sourceWorksheetXml.Root?
                .Elements(worksheetNs + "conditionalFormatting")
                .Where(block => ConditionalFormattingHasUnsupportedRule(block, worksheetNs))
                .ToList()
                ?? [];
            if (unsupportedBlocks.Count == 0)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var existing = targetRoot
                .Elements(worksheetNs + "conditionalFormatting")
                .Select(element => element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var block in unsupportedBlocks)
            {
                var raw = block.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                if (!existing.Contains(raw))
                    targetRoot.Add(new XElement(block));
            }

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, sourceWorksheetEntry.FullName, targetWorksheetXml);
        }
    }

    private static bool ConditionalFormattingHasUnsupportedRule(XElement block, XNamespace worksheetNs) =>
        block.Elements(worksheetNs + "cfRule")
            .Select(rule => rule.Attribute("type")?.Value)
            .Any(type => !IsSupportedConditionalFormatRuleType(type));

    private static bool IsSupportedConditionalFormatRuleType(string? type) =>
        string.IsNullOrWhiteSpace(type) ||
        type is "cellIs" or "expression" or "colorScale" or "dataBar" or "iconSet" or
            "aboveAverage" or "top10" or "uniqueValues" or "duplicateValues" or
            "containsText" or "notContainsText" or "beginsWith" or "endsWith" or
            "timePeriod" or "containsBlanks" or "notContainsBlanks" or
            "containsErrors" or "notContainsErrors";
}
