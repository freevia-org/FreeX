using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

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
            if (requirements.HasChartExChartParts)
                RemoveChartExDrawingRelationships(archive);
            if (requirements.HasUnsupportedConditionalFormattingBlocks)
                RemoveUnsupportedConditionalFormattingBlocks(archive);
            if (requirements.HasWorksheetDynamicFilters)
                RemoveWorksheetDynamicFilters(archive);
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
                HasChartExChartParts(archive),
                scanUnsupportedConditionalFormatting && HasUnsupportedConditionalFormattingBlocks(archive),
                HasWorksheetDynamicFilters(archive));
        }
        catch
        {
            return new SanitizationRequirements(true, true, true, true);
        }
        finally
        {
            if (sourcePackage.CanSeek)
                sourcePackage.Position = 0;
        }
    }

    private readonly record struct SanitizationRequirements(
        bool HasPivotPackageMetadata,
        bool HasChartExChartParts,
        bool HasUnsupportedConditionalFormattingBlocks,
        bool HasWorksheetDynamicFilters)
    {
        public bool RequiresAny => HasPivotPackageMetadata || HasChartExChartParts || HasUnsupportedConditionalFormattingBlocks || HasWorksheetDynamicFilters;
    }

    private static bool HasPivotPackageMetadata(ZipArchive archive) =>
        archive.Entries.Any(entry =>
            entry.FullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase));

    private static bool HasChartExChartParts(ZipArchive archive) =>
        GetChartExPartNames(archive).Count > 0;

    private static HashSet<string> GetChartExPartNames(ZipArchive archive)
    {
        const string chartExContentType = "application/vnd.ms-office.chartex+xml";
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return [];

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        return contentTypesXml.Root?
            .Elements(contentTypesNs + "Override")
            .Where(element => string.Equals(element.Attribute("ContentType")?.Value, chartExContentType, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("PartName")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    private static void RemoveChartExDrawingRelationships(ZipArchive archive)
    {
        var chartExParts = GetChartExPartNames(archive);
        if (chartExParts.Count == 0)
            return;

        const string chartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        const string chartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var relsEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/drawings/_rels/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml.rels", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var drawingPath = "xl/drawings/" + relsEntry.FullName["xl/drawings/_rels/".Length..^".rels".Length];
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            var chartExRelationships = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(element =>
                    (string.Equals(element.Attribute("Type")?.Value, chartRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(element.Attribute("Type")?.Value, chartExRelationshipType, StringComparison.OrdinalIgnoreCase)) &&
                    element.Attribute("Target")?.Value is { Length: > 0 } target &&
                    chartExParts.Contains(XlsxPackagePath.ResolveRelationshipTarget(drawingPath, target)))
                .ToList()
                ?? [];

            if (chartExRelationships.Count == 0)
                continue;

            chartExRelationships.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, relsEntry.FullName, relsXml);
        }
    }

    private static bool HasUnsupportedConditionalFormattingBlocks(ZipArchive archive) =>
        XlsxConditionalFormatRuleSupport.HasUnsupportedRuleInWorksheets(archive, allowBlankType: false);

    private static bool HasWorksheetDynamicFilters(ZipArchive archive) =>
        archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Any(entry =>
            {
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                });

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        string.Equals(reader.LocalName, "dynamicFilter", StringComparison.Ordinal) &&
                        string.Equals(reader.NamespaceURI, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });

    private static void RemoveWorksheetDynamicFilters(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var dynamicFilters = root
                .Descendants(worksheetNs + "dynamicFilter")
                .ToList();
            if (dynamicFilters.Count == 0)
                continue;

            dynamicFilters.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
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
                .Where(block => XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(block, worksheetNs, allowBlankType: false))
                .ToList();
            if (unsupportedBlocks.Count == 0)
                continue;

            unsupportedBlocks.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

}
