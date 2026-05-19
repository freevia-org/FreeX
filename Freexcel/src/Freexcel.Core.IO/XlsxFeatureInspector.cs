using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public enum XlsxUnsupportedFeatureKind
{
    Macros,
    Charts,
    EmbeddedObjects,
    CustomXmlParts,
    ConditionalFormats,
    DrawingObjects,
    PowerQuery,
    DataModel,
    LinkedDataTypes,
    ThreadedComments,
    TrackChanges,
    FormControls,
    DigitalSignatures,
    CustomRibbonUi,
    OfficeAddIns,
    LiveWebQueries,
    SensitivityLabels,
    SmartArtDiagrams,
    UnsupportedSheetTypes
}

public sealed record XlsxUnsupportedFeature(
    XlsxUnsupportedFeatureKind Kind,
    string PackagePart);

public sealed record XlsxFeatureReport(
    IReadOnlyList<XlsxUnsupportedFeature> Features)
{
    public bool HasUnsupportedFeatures => Features.Count > 0;
}

public static class XlsxFeatureInspector
{
    public static XlsxFeatureReport Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var features = archive.Entries
                .SelectMany(InspectEntry)
                .Distinct()
                .ToList();

            return new XlsxFeatureReport(features);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    private static IEnumerable<XlsxUnsupportedFeature> InspectEntry(ZipArchiveEntry entry)
    {
        var packagePart = entry.FullName;
        var normalized = packagePart.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        if (normalized is "xl/vbaproject.bin")
        {
            yield return Feature(XlsxUnsupportedFeatureKind.Macros);
            yield break;
        }

        if (normalized.StartsWith("xl/pivottables/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/pivotcache/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/queries/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/querytables/", StringComparison.Ordinal) ||
            normalized is "xl/connections.xml")
        {
            yield return Feature(XlsxUnsupportedFeatureKind.PowerQuery);
            yield break;
        }

        if (normalized.StartsWith("xl/model/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/datamodel/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/powerpivot/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.DataModel);
            yield break;
        }

        if (normalized.StartsWith("xl/richdata/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.LinkedDataTypes);
            yield break;
        }

        if (normalized.StartsWith("xl/threadedcomments/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/persons/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.ThreadedComments);
            yield break;
        }

        if (normalized.StartsWith("xl/revisionheaders/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/revisions/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.TrackChanges);
            yield break;
        }

        if (normalized.StartsWith("xl/activex/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/ctrlprops/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.FormControls);
            yield break;
        }

        if (normalized.StartsWith("_xmlsignatures/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.DigitalSignatures);
            yield break;
        }

        if (normalized.StartsWith("customui/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.CustomRibbonUi);
            yield break;
        }

        if (normalized.StartsWith("xl/webextensions/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.OfficeAddIns);
            yield break;
        }

        if (normalized is "xl/webpublishitems.xml")
        {
            yield return Feature(XlsxUnsupportedFeatureKind.LiveWebQueries);
            yield break;
        }

        if (normalized is "docprops/custom.xml" &&
            CustomPropertiesHaveSensitivityLabels(entry))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.SensitivityLabels);
            yield break;
        }

        if (normalized.StartsWith("xl/diagrams/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.SmartArtDiagrams);
            yield break;
        }

        if (normalized.StartsWith("xl/printersettings/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/chartsheets/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/dialogsheets/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/macrosheets/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
            yield break;
        }

        if (IsChartPart(normalized))
        {
            if (!IsSupportedChartPart(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.Charts);

            yield break;
        }

        if (normalized.StartsWith("xl/slicers/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/slicercaches/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/timelines/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/timelinecaches/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/externallinks/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/embeddings/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.EmbeddedObjects);
            yield break;
        }

        if (normalized.StartsWith("customxml/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
            normalized.EndsWith(".xml", StringComparison.Ordinal))
        {
            yield break;
        }

        XlsxUnsupportedFeature Feature(XlsxUnsupportedFeatureKind kind) => new(kind, packagePart);
    }

    private static bool IsChartPart(string normalizedPackagePart) =>
        normalizedPackagePart.StartsWith("xl/charts/", StringComparison.Ordinal) ||
        normalizedPackagePart.StartsWith("xl/drawings/charts/", StringComparison.Ordinal);

    private static bool IsSupportedChartPart(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var chartXml = XDocument.Load(stream);
            return XlsxChartPartReader.TryReadSupportedChart(chartXml, SheetId.New(), out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool WorksheetHasSparklines(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var worksheetXml = XDocument.Load(stream);
            return worksheetXml
                .Descendants()
                .Any(element => string.Equals(element.Name.LocalName, "sparklineGroups", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool CustomPropertiesHaveSensitivityLabels(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var propertiesXml = XDocument.Load(stream);
            XNamespace customPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";

            return propertiesXml
                .Descendants(customPropertiesNs + "property")
                .Select(property => property.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Any(name =>
                    name!.StartsWith("MSIP_Label_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Sensitivity", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
