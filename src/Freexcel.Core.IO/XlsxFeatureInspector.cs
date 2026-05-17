using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public enum XlsxUnsupportedFeatureKind
{
    Macros,
    PivotTables,
    Charts,
    Slicers,
    Timelines,
    ExternalLinks,
    EmbeddedObjects,
    CustomXmlParts,
    ConditionalFormats,
    DrawingObjects,
    Sparklines,
    PowerQuery,
    DataModel,
    LinkedDataTypes,
    ThreadedComments,
    TrackChanges,
    FormControls
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
            yield return Feature(XlsxUnsupportedFeatureKind.PivotTables);
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

        if (IsChartPart(normalized))
        {
            if (!IsSupportedChartPart(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.Charts);

            yield break;
        }

        if (normalized.StartsWith("xl/slicers/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/slicercaches/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.Slicers);
            yield break;
        }

        if (normalized.StartsWith("xl/timelines/", StringComparison.Ordinal) ||
            normalized.StartsWith("xl/timelinecaches/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.Timelines);
            yield break;
        }

        if (normalized.StartsWith("xl/externallinks/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.ExternalLinks);
            yield break;
        }

        if (normalized.StartsWith("xl/embeddings/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.EmbeddedObjects);
            yield break;
        }

        if (normalized.StartsWith("customxml/", StringComparison.Ordinal))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.CustomXmlParts);
            yield break;
        }

        if (normalized.StartsWith("xl/worksheets/", StringComparison.Ordinal) &&
            normalized.EndsWith(".xml", StringComparison.Ordinal))
        {
            if (WorksheetHasUnsupportedConditionalFormats(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.ConditionalFormats);

            if (WorksheetHasSparklines(entry))
                yield return Feature(XlsxUnsupportedFeatureKind.Sparklines);

            yield break;
        }

        if (normalized.StartsWith("xl/drawings/", StringComparison.Ordinal) &&
            normalized.EndsWith(".xml", StringComparison.Ordinal) &&
            DrawingHasUnsupportedObjects(entry))
        {
            yield return Feature(XlsxUnsupportedFeatureKind.DrawingObjects);
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

    private static bool WorksheetHasUnsupportedConditionalFormats(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var worksheetXml = XDocument.Load(stream);
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            return worksheetXml
                .Descendants(worksheetNs + "cfRule")
                .Any(rule =>
                {
                    var type = rule.Attribute("type")?.Value;
                    return !string.Equals(type, "cellIs", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase);
                });
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

    private static bool DrawingHasUnsupportedObjects(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var drawingXml = XDocument.Load(stream);
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            return drawingXml
                .Descendants()
                .Any(element =>
                    element.Name.Namespace == spreadsheetDrawingNs &&
                    element.Name.LocalName is "sp" or "pic" or "cxnSp" or "grpSp");
        }
        catch
        {
            return false;
        }
    }
}
