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
            .Where(feature => feature.Kind != XlsxUnsupportedFeatureKind.LinkedDataTypes)
            .Distinct()
            .ToList();

        // Rich-data is a container format, not a synonym for a Microsoft linked data type.  Excel
        // uses it for formula-created local values too: notably dynamic-array results and images
        // produced or propagated by a formula.  Those package graphs are preserved by the source
        // patch writer, so warning just because xl/richData exists is both noisy and misleading.
        // A service-linked entity is explicitly identified by its rich-value structure type.
        if (HasLinkedDataTypes(archive))
            features.Add(new XlsxUnsupportedFeature(XlsxUnsupportedFeatureKind.LinkedDataTypes, "xl/richData"));

        return new XlsxFeatureReport(features.Distinct().ToList());
    }

    private static bool HasLinkedDataTypes(ZipArchive archive)
    {
        var richDataEntries = archive.Entries
            .Where(entry => XlsxPackagePath.NormalizeEntryPath(entry)
                .StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (richDataEntries.Count == 0)
        {
            // A dangling rich-data relationship still describes a linked-data package feature and
            // remains worth disclosing.  The normal complete package path below is deliberately
            // more precise because it can inspect the rich-value type.
            return archive.Entries
                .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                .SelectMany(InspectRelationships)
                .Contains(XlsxUnsupportedFeatureKind.LinkedDataTypes);
        }

        var structureEntries = richDataEntries
            .Where(entry => string.Equals(
                Path.GetFileName(XlsxPackagePath.NormalizeEntryPath(entry)),
                "rdrichvaluestructure.xml",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A rich-value payload without its type table is not something this inspector can safely
        // classify as a formula-created value, so retain the conservative disclosure.
        if (structureEntries.Count == 0)
            return true;

        foreach (var structureEntry in structureEntries)
        {
            try
            {
                var document = XlsxPackageXmlEditor.LoadXml(structureEntry);
                var structures = document
                    .Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "s", StringComparison.Ordinal));

                foreach (var structure in structures)
                {
                    var type = structure.Attribute("t")?.Value;
                    if (string.IsNullOrWhiteSpace(type) || !IsFormulaCreatedRichValueType(type))
                        return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFormulaCreatedRichValueType(string type) =>
        string.Equals(type, "_array", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "_error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "_localImage", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "_webImage", StringComparison.OrdinalIgnoreCase);

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

        if (normalized.StartsWith("xl/queries/", StringComparison.Ordinal))
        {
            // xl/queries/ parts hold the actual Power Query M-code — a genuine PQ signal.
            yield return Feature(XlsxUnsupportedFeatureKind.PowerQuery);
            yield break;
        }

        if (normalized.StartsWith("xl/querytables/", StringComparison.Ordinal))
        {
            // R76-io-external-data-4-2: a queryTable part by itself is the generic OOXML mechanism
            // for ANY external data range (classic Text/Database/ODBC/classic-Web queries included)
            // and round-trips byte-for-byte. It is not, on its own, evidence of Power Query — the
            // real PQ signal (if any) lives in the paired xl/connections.xml (Mashup provider) or
            // xl/queries/ (M-code), both handled by their own branches. Do not flag it here.
            yield break;
        }

        if (normalized is "xl/connections.xml")
        {
            if (ConnectionsHaveLiveWebQuery(entry))
            {
                yield return Feature(XlsxUnsupportedFeatureKind.LiveWebQueries);
            }
            else if (ConnectionsHavePowerQuerySignal(entry))
            {
                yield return Feature(XlsxUnsupportedFeatureKind.PowerQuery);
            }

            // A classic Text/Database/ODBC connection (no webPr, no Mashup provider signal) is a
            // preserved, round-tripped external data connection — not Power Query — so it is not
            // reported as an excluded/unsupported feature at all.
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

        if (normalized.StartsWith("xl/activex/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.FormControls);
            yield break;
        }

        // Legacy form controls (ctrlProps) are now modeled on load and round-trip preserved, so the
        // ctrlProp parts are a SUPPORTED feature and must not be reported as unsupported.
        if (normalized.StartsWith("xl/ctrlprops/", StringComparison.Ordinal))
        {
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

        if (normalized.StartsWith("xl/chartsheets/", StringComparison.Ordinal))
        {
            // Chartsheets (full-page chart-only sheets) are now loaded and modeled as Sheets with
            // Kind = Chartsheet, so they are no longer flagged as an unsupported sheet type.
            yield break;
        }

        if (normalized.StartsWith("xl/dialogsheets/", StringComparison.Ordinal) ||
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
            // Legacy form-control shapes in VML/drawings are now supported (modeled + preserved);
            // only embedded OLE objects in drawings remain unsupported.
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
            // Worksheet <controls>/<control> form controls are now supported (modeled + preserved);
            // only embedded OLE objects remain unsupported.
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

        // ActiveX controls remain unsupported. Legacy form-control relationships (/control, /ctrlProp)
        // are now modeled + round-trip preserved, so they are no longer reported as unsupported.
        if (normalizedType.EndsWith("/activexcontrol", StringComparison.OrdinalIgnoreCase) ||
            normalizedType.EndsWith("/activexcontrolbinary", StringComparison.OrdinalIgnoreCase))
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

        // Chartsheets are loaded and modeled (Kind = Chartsheet), so only dialog/macro sheets
        // remain unsupported sheet types.
        if (normalizedType.EndsWith("/dialogsheet", StringComparison.OrdinalIgnoreCase) ||
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

    /// <summary>
    /// R76-io-external-data-4-2: detects the genuine Power Query signal in a connections.xml part —
    /// a <c>dbPr</c> (or <c>olapPr</c>) connection/command string that references the
    /// "Microsoft.Mashup" OLE DB provider, which is how Excel marks a connection as backed by a
    /// Power Query (as opposed to a classic Text/Database/ODBC external data connection).
    /// </summary>
    private static bool ConnectionsHavePowerQuerySignal(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
        {
            if (!string.Equals(reader.LocalName, "dbPr", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reader.LocalName, "olapPr", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return HasMashupSignal(reader.GetAttribute("connection")) ||
                   HasMashupSignal(reader.GetAttribute("command"));
        });

    private static bool HasMashupSignal(string? value) =>
        !string.IsNullOrEmpty(value) && value.Contains("Mashup", StringComparison.OrdinalIgnoreCase);

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

    private static bool WorksheetHasEmbeddedObjects(ZipArchiveEntry entry) =>
        XmlHasElement(entry, reader =>
            string.Equals(reader.LocalName, "oleObject", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "oleObjects", StringComparison.OrdinalIgnoreCase));

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
