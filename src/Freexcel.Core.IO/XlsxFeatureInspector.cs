using System.IO.Compression;

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
                .SelectMany(XlsxPackageFeatureProbe.InspectEntry)
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
}
