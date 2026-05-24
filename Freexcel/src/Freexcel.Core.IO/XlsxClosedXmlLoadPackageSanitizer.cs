using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxClosedXmlLoadPackageSanitizer
{
    public static MemoryStream Create(MemoryStream sourcePackage)
    {
        var sanitized = new MemoryStream();
        if (sourcePackage.TryGetBuffer(out var sourceBuffer))
            sanitized.Write(sourceBuffer.Array!, sourceBuffer.Offset, sourceBuffer.Count);
        else
            sourcePackage.WriteTo(sanitized);
        sanitized.Position = 0;
        using (var archive = new ZipArchive(sanitized, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxPivotPackageCleaner.RemovePivotPackageMetadata(archive);
            RemoveUnsupportedConditionalFormattingBlocks(archive);
        }

        sanitized.Position = 0;
        return sanitized;
    }

    private static void RemoveUnsupportedConditionalFormattingBlocks(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var unsupportedBlocks = root
                .Elements(worksheetNs + "conditionalFormatting")
                .Where(block => ConditionalFormattingHasUnsupportedRule(block, worksheetNs))
                .ToList();
            if (unsupportedBlocks.Count == 0)
                continue;

            unsupportedBlocks.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool ConditionalFormattingHasUnsupportedRule(XElement block, XNamespace worksheetNs) =>
        block.Elements(worksheetNs + "cfRule")
            .Any(rule => !IsSupportedConditionalFormatRuleType(rule.Attribute("type")?.Value));

    private static bool IsSupportedConditionalFormatRuleType(string? type) =>
        string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "colorScale", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "dataBar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "iconSet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "aboveAverage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "top10", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "uniqueValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "duplicateValues", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsText", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "beginsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "endsWith", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "timePeriod", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsBlanks", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "containsErrors", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "notContainsErrors", StringComparison.OrdinalIgnoreCase);
}
