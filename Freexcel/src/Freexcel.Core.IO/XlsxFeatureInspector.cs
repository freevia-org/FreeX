using System.IO.Compression;

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
    CustomXmlParts
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
                .Select(entry => InspectEntry(entry.FullName))
                .Where(feature => feature is not null)
                .Cast<XlsxUnsupportedFeature>()
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

    private static XlsxUnsupportedFeature? InspectEntry(string packagePart)
    {
        var normalized = packagePart.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        return normalized switch
        {
            "xl/vbaproject.bin" => Feature(XlsxUnsupportedFeatureKind.Macros),
            _ when normalized.StartsWith("xl/pivottables/", StringComparison.Ordinal)
                || normalized.StartsWith("xl/pivotcache/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.PivotTables),
            _ when normalized.StartsWith("xl/charts/", StringComparison.Ordinal)
                || normalized.StartsWith("xl/drawings/charts/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.Charts),
            _ when normalized.StartsWith("xl/slicers/", StringComparison.Ordinal)
                || normalized.StartsWith("xl/slicercaches/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.Slicers),
            _ when normalized.StartsWith("xl/timelines/", StringComparison.Ordinal)
                || normalized.StartsWith("xl/timelinecaches/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.Timelines),
            _ when normalized.StartsWith("xl/externallinks/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.ExternalLinks),
            _ when normalized.StartsWith("xl/embeddings/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.EmbeddedObjects),
            _ when normalized.StartsWith("customxml/", StringComparison.Ordinal)
                => Feature(XlsxUnsupportedFeatureKind.CustomXmlParts),
            _ => null
        };

        XlsxUnsupportedFeature Feature(XlsxUnsupportedFeatureKind kind) => new(kind, packagePart);
    }
}
