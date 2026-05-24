using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;

namespace Freexcel.Core.IO;

internal static class XlsxClosedXmlLoadPackageSanitizer
{
    public static MemoryStream Create(MemoryStream sourcePackage) =>
        Create(sourcePackage, removeUnsupportedConditionalFormatting: false);

    public static MemoryStream Create(
        MemoryStream sourcePackage,
        bool removeUnsupportedConditionalFormatting = false)
    {
        sourcePackage.Position = 0;
        var requirements = GetSanitizationRequirements(sourcePackage, removeUnsupportedConditionalFormatting);
        if (!requirements.RequiresAny)
        {
            sourcePackage.Position = 0;
            return sourcePackage;
        }

        sourcePackage.Position = 0;
        var sanitized = new MemoryStream();
        if (sourcePackage.TryGetBuffer(out var sourceBuffer))
            sanitized.Write(sourceBuffer.Array!, sourceBuffer.Offset, sourceBuffer.Count);
        else
            sourcePackage.WriteTo(sanitized);
        sanitized.Position = 0;
        using (var archive = new ZipArchive(sanitized, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (requirements.HasPivotPackageMetadata)
                XlsxPivotPackageCleaner.RemovePivotPackageMetadata(archive);
            if (requirements.HasUnsupportedConditionalFormattingBlocks)
                RemoveUnsupportedConditionalFormattingBlocks(archive);
        }

        sanitized.Position = 0;
        return sanitized;
    }

    private static SanitizationRequirements GetSanitizationRequirements(
        Stream sourcePackage,
        bool scanUnsupportedConditionalFormatting = true)
    {
        try
        {
            using var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
            return new SanitizationRequirements(
                HasPivotPackageMetadata(archive),
                scanUnsupportedConditionalFormatting && HasUnsupportedConditionalFormattingBlocks(archive));
        }
        catch
        {
            return new SanitizationRequirements(true, true);
        }
        finally
        {
            if (sourcePackage.CanSeek)
                sourcePackage.Position = 0;
        }
    }

    private readonly record struct SanitizationRequirements(
        bool HasPivotPackageMetadata,
        bool HasUnsupportedConditionalFormattingBlocks)
    {
        public bool RequiresAny => HasPivotPackageMetadata || HasUnsupportedConditionalFormattingBlocks;
    }

    private static bool HasPivotPackageMetadata(ZipArchive archive) =>
        archive.Entries.Any(entry =>
            entry.FullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase));

    private static bool HasUnsupportedConditionalFormattingBlocks(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
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

                if (!IsSupportedConditionalFormatRuleType(reader.GetAttribute("type")))
                    return true;
            }
        }

        return false;
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
