using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    // Source package snapshot and native package-part preservation for loaded workbook saves.
    private static void PreserveSourcePackageParts(Workbook workbook, Stream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return;

        using var sourceStream = sourcePackage.OpenRead();
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, generatedArchive);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, generatedArchive);
        PreserveSourceChartExParts(sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, generatedArchive);
        if (HasAnySourcePackagePart(sourceArchive, "xl/pivotCache/", "xl/pivotTables/"))
            XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (HasSourcePackagePart(sourceArchive, "xl/tables/"))
            XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (HasSourcePackagePart(sourceArchive, "xl/externalLinks/"))
            XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (HasUnsupportedSheetPackagePart(sourceArchive))
            XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (HasSourcePackagePart(sourceArchive, "xl/drawings/"))
        {
            var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, generatedArchive, context);
            XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive, context, drawingPaths);
        }
        if (HasSourcePackagePart(sourceArchive, "xl/printerSettings/"))
            XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook, context);
        XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        if (HasSourcePackagePart(sourceArchive, "xl/sharedStrings.xml"))
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        if (HasUnsupportedConditionalFormatting(sourceArchive))
            XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);
    }

    private static bool HasSourcePackagePart(ZipArchive archive, string prefix) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static void PreserveSourceChartExParts(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge)
    {
        foreach (var chartExPartPath in GetChartExPartPaths(sourceArchive))
        {
            if (generatedEntriesBeforeMerge.Contains(chartExPartPath))
                continue;

            var sourceEntry = sourceArchive.GetEntry(chartExPartPath);
            if (sourceEntry is null)
                continue;

            generatedArchive.GetEntry(chartExPartPath)?.Delete();
            XlsxPackageMetadataMerger.CopyEntry(sourceEntry, generatedArchive);
        }
    }

    private static IEnumerable<string> GetChartExPartPaths(ZipArchive archive)
    {
        const string chartExContentType = "application/vnd.ms-office.chartex+xml";
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            yield break;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        foreach (var partName in contentTypesXml.Root?
                     .Elements(contentTypesNs + "Override")
                     .Where(element => string.Equals(element.Attribute("ContentType")?.Value, chartExContentType, StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("PartName")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                 ?? [])
        {
            yield return partName!.TrimStart('/');
        }

        foreach (var chartEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var chartXml = XlsxPackageXmlEditor.LoadXml(chartEntry);
            if (chartXml.Root?.Name.NamespaceName == "http://schemas.microsoft.com/office/drawing/2014/chartex")
                yield return chartEntry.FullName;
        }
    }

    private static bool HasAnySourcePackagePart(ZipArchive archive, params string[] prefixes) =>
        archive.Entries.Any(entry => prefixes.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    private static bool HasUnsupportedSheetPackagePart(ZipArchive archive) =>
        archive.Entries.Any(entry =>
            entry.FullName.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase));

    private static bool HasUnsupportedConditionalFormatting(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
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

    private static bool IsSupportedConditionalFormatRuleType(string? type) =>
        string.IsNullOrWhiteSpace(type) ||
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

    private sealed record XlsxSourcePackage(byte[] Buffer, int Offset, int Count)
    {
        public static XlsxSourcePackage Capture(MemoryStream stream)
        {
            if (stream.TryGetBuffer(out var buffer))
                return new XlsxSourcePackage(buffer.Array!, buffer.Offset, buffer.Count);

            var bytes = new byte[stream.Length];
            var previousPosition = stream.Position;
            stream.Position = 0;
            stream.ReadExactly(bytes);
            stream.Position = previousPosition;
            return new XlsxSourcePackage(bytes, 0, bytes.Length);
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);
    }
}
