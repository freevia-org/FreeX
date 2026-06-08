using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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
    private static readonly XmlReaderSettings ScanXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };

    public static XlsxFeatureReport Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return Inspect(archive);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }
    }

    public static XlsxFeatureReport Inspect(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var features = archive.Entries
            .SelectMany(InspectEntry)
            .Distinct()
            .ToList();

        return new XlsxFeatureReport(features);
    }

    private static IEnumerable<XlsxUnsupportedFeature> InspectEntry(ZipArchiveEntry entry)
    {
        var packagePart = entry.FullName;
        var normalized = XlsxPackagePath.NormalizeEntryPath(entry).ToLowerInvariant();

        if (normalized is "xl/vbaproject.bin")
        {
            yield return Feature(XlsxUnsupportedFeatureKind.Macros);
            yield break;
        }

        if (normalized.EndsWith(".rels", StringComparison.Ordinal))
        {
            foreach (var featureKind in InspectRelationships(entry))
                yield return Feature(featureKind);

            yield break;
        }

        if (normalized.StartsWith("xl/pivottables/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/pivotcache/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (normalized.StartsWith("xl/queries/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/querytables/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.PowerQuery);
            yield break;
        }

        if (normalized is "xl/connections.xml")
        {
            yield return Feature(ConnectionsHaveLiveWebQuery(entry)
                ? XlsxUnsupportedFeatureKind.LiveWebQueries
                : XlsxUnsupportedFeatureKind.PowerQuery);
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

        if (normalized is "docmetadata/labelinfo.xml")
        {
            yield return Feature(XlsxUnsupportedFeatureKind.SensitivityLabels);
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

        if (normalized.StartsWith("xl/drawings/", StringComparison.Ordinal) &&
            (normalized.EndsWith(".xml", StringComparison.Ordinal) ||
             normalized.EndsWith(".vml", StringComparison.Ordinal)))
        {
            if (DrawingHasFormControls(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.FormControls);
            if (DrawingHasEmbeddedObjects(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.EmbeddedObjects);

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
            if (WorksheetHasFormControls(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.FormControls);
            if (WorksheetHasEmbeddedObjects(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.EmbeddedObjects);

            yield break;
        }

        XlsxUnsupportedFeature Feature(XlsxUnsupportedFeatureKind kind) => new(kind, packagePart);
    }

    private static bool IsChartPart(string normalizedPackagePart) =>
        IsNumberedChartPart(normalizedPackagePart, "xl/charts/") ||
        IsNumberedChartPart(normalizedPackagePart, "xl/drawings/charts/");

    private static bool IsNumberedChartPart(string normalizedPackagePart, string prefix)
    {
        if (!normalizedPackagePart.StartsWith(prefix, StringComparison.Ordinal) ||
            !normalizedPackagePart.EndsWith(".xml", StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = normalizedPackagePart[prefix.Length..];
        return fileName.StartsWith("chart", StringComparison.Ordinal) &&
               fileName.Length > "chart".Length &&
               char.IsDigit(fileName["chart".Length]);
    }

    private static IReadOnlyList<XlsxUnsupportedFeatureKind> InspectRelationships(ZipArchiveEntry entry)
    {
        const string relationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        List<XlsxUnsupportedFeatureKind>? result = null;

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, ScanXmlSettings);
            var relationshipsDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (relationshipsDepth < 0)
                {
                    if (reader.LocalName == "Relationships" &&
                        reader.NamespaceURI == relationshipsNs)
                    {
                        relationshipsDepth = reader.Depth;
                    }

                    continue;
                }

                if (reader.Depth != relationshipsDepth + 1 ||
                    reader.LocalName != "Relationship" ||
                    reader.NamespaceURI != relationshipsNs)
                {
                    continue;
                }

                AddRelationshipFeatures(reader.GetAttribute("Type"), reader.GetAttribute("Target"), ref result);
            }
        }
        catch
        {
            return [];
        }

        return result ?? [];
    }

    private static void AddRelationshipFeatures(
        string? type,
        string? target,
        ref List<XlsxUnsupportedFeatureKind>? result)
    {
        if (string.IsNullOrWhiteSpace(type))
            return;

        var normalizedType = type.Trim();
        var normalizedTarget = NormalizeRelationshipTarget(target);

        if (normalizedType.EndsWith("/vbaproject", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.Macros);
            return;
        }

        if (normalizedType.Contains("/digital-signature/", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.DigitalSignatures);
            return;
        }

        if (normalizedType.EndsWith("/querytable", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/connections", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.PowerQuery);
            return;
        }

        if (normalizedType.EndsWith("/webpublishitems", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.LiveWebQueries);
            return;
        }

        if (normalizedType.EndsWith("/rdrichvalue", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/rdrichvaluestructure", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/rdrichvaluetypes", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/richvaluerel", StringComparison.OrdinalIgnoreCase) ||
            normalizedTarget.Contains("richdata/", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.LinkedDataTypes);
            return;
        }

        if (normalizedType.EndsWith("/model", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.DataModel);
            return;
        }

        if (normalizedType.EndsWith("/threadedcomment", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/person", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (normalizedType.EndsWith("/revisionheaders", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/revisionlog", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.TrackChanges);
            return;
        }

        if (normalizedType.EndsWith("/control", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/activexcontrol", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/activexcontrolbinary", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/ctrlprop", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.FormControls);
            return;
        }

        if (normalizedType.EndsWith("/oleobject", StringComparison.OrdinalIgnoreCase) ||
            (normalizedType.EndsWith("/package", StringComparison.OrdinalIgnoreCase) &&
             normalizedTarget.Contains("embeddings/", StringComparison.OrdinalIgnoreCase)))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.EmbeddedObjects);
            return;
        }

        if (normalizedType.EndsWith("/customui", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.Contains("/ui/extensibility", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.CustomRibbonUi);
            return;
        }

        if (normalizedType.EndsWith("/diagramdata", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/diagramlayout", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/diagramquickstyle", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/diagramcolors", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.SmartArtDiagrams);
            return;
        }

        if (normalizedType.EndsWith("/chartsheet", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/dialogsheet", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/xlmacrosheet", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
            return;
        }

        if (normalizedType.EndsWith("/webextension", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/webextensiontaskpanes", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/taskpane", StringComparison.OrdinalIgnoreCase))
        {
            AddFeature(ref result, XlsxUnsupportedFeatureKind.OfficeAddIns);
        }
    }

    private static void AddFeature(
        ref List<XlsxUnsupportedFeatureKind>? result,
        XlsxUnsupportedFeatureKind kind)
        => (result ??= []).Add(kind);

    private static string NormalizeRelationshipTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;

        var trimmed = target.Trim();
        return trimmed.Contains('\\', StringComparison.Ordinal)
            ? trimmed.Replace('\\', '/')
            : trimmed;
    }

    private static bool IsSupportedChartPart(ZipArchiveEntry entry)
    {
        try
        {
            var chartXml = XlsxPackageXmlEditor.LoadXml(entry);
            return XlsxChartPartReader.TryReadSupportedChart(chartXml, SheetId.New(), out _);
        }
        catch
        {
            return false;
        }
    }

    private static bool ConnectionsHaveLiveWebQuery(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "webPr", StringComparison.OrdinalIgnoreCase));

    private static bool WorksheetHasSparklines(ZipArchiveEntry entry)
    {
        try
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            return worksheetXml
                .Descendants()
                .Any(element => string.Equals(element.Name.LocalName, "sparklineGroups", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool WorksheetHasFormControls(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "control", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "controls", StringComparison.OrdinalIgnoreCase));

    private static bool WorksheetHasEmbeddedObjects(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "oleObject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "oleObjects", StringComparison.OrdinalIgnoreCase));

    private static bool DrawingHasFormControls(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "control", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "ClientData", StringComparison.OrdinalIgnoreCase) &&
            IsFormControlObjectType(reader.GetAttribute("ObjectType")));

    private static bool DrawingHasEmbeddedObjects(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "oleObj", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "oleObject", StringComparison.OrdinalIgnoreCase));

    private static bool XmlHasElement(ZipArchiveEntry entry, Func<XmlReader, bool> predicate)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, ScanXmlSettings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && predicate(reader))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool XmlHasDescendant(ZipArchiveEntry entry, Func<XElement, bool> predicate)
    {
        try
        {
            var xml = XlsxPackageXmlEditor.LoadXml(entry);
            return xml.Descendants().Any(predicate);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFormControlObjectType(string? objectType) =>
        objectType is not null &&
        (objectType.Equals("Button", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Drop", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("GBox", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Label", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("List", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Option", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Radio", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Scroll", StringComparison.OrdinalIgnoreCase) ||
         objectType.Equals("Spin", StringComparison.OrdinalIgnoreCase));

    private static bool CustomPropertiesHaveSensitivityLabels(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, ScanXmlSettings);
            const string customPropertiesNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "property", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(reader.NamespaceURI, customPropertiesNs, StringComparison.Ordinal))
                {
                    continue;
                }

                var name = reader.GetAttribute("name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name.StartsWith("MSIP_Label_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Sensitivity", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
