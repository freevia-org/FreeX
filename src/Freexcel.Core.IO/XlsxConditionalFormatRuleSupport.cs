using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxConditionalFormatRuleSupport
{
    private static readonly string[] SupportedRuleTypes =
    [
        "cellIs",
        "expression",
        "colorScale",
        "dataBar",
        "iconSet",
        "aboveAverage",
        "top10",
        "uniqueValues",
        "duplicateValues",
        "containsText",
        "notContainsText",
        "beginsWith",
        "endsWith",
        "timePeriod",
        "containsBlanks",
        "notContainsBlanks",
        "containsErrors",
        "notContainsErrors",
    ];

    public static bool HasUnsupportedRuleInWorksheets(
        ZipArchive archive,
        bool allowBlankType,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetEntry))
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "cfRule", StringComparison.Ordinal) ||
                    !string.Equals(reader.NamespaceURI, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsSupportedRuleType(reader.GetAttribute("type"), allowBlankType, comparison))
                    return true;
            }
        }

        return false;
    }

    public static bool ConditionalFormattingHasUnsupportedRule(
        XElement block,
        XNamespace worksheetNs,
        bool allowBlankType,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase) =>
        block.Elements(worksheetNs + "cfRule")
            .Any(rule => !IsSupportedRuleType(rule.Attribute("type")?.Value, allowBlankType, comparison));

    public static bool IsSupportedRuleType(
        string? type,
        bool allowBlankType,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.IsNullOrWhiteSpace(type))
            return allowBlankType;

        return SupportedRuleTypes.Any(supported => string.Equals(type, supported, comparison));
    }

    public static bool IsWorksheetEntry(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
}
