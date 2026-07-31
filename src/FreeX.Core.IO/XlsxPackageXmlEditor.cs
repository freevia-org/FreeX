using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPackageXmlEditor
{
    public static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        // Defense in depth: a crafted package may contain multiple entries with the same name
        // (ZipArchive tolerates duplicates; GetEntry returns only the first).  Delete all of them
        // before creating the authoritative replacement so no stale duplicate can be read back.
        OpcXml.ReplaceXmlEntry(archive, entryName, document);
    }

    public static XDocument LoadXml(ZipArchiveEntry entry)
        => OpcXml.LoadXml(entry);

    public static XDocument LoadXml(Stream stream, long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
        => OpcXml.LoadXml(stream, maxCharactersInDocument);

    public static string NextRelationshipId(XDocument relsXml, XNamespace packageRelNs)
        => OpcRelationships.NextRelationshipId(relsXml, packageRelNs);

    public static void EnsureDefaultContentType(ZipArchive archive, string extension, string contentType) =>
        OpcMediaTypes.EnsureDefaultContentType(archive, extension, contentType);

    public static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType) =>
        OpcMediaTypes.EnsureOverrideContentType(archive, partName, contentType);

    public static string EnsureRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace packageRelNs,
        string sourcePart,
        string targetPart,
        string relationshipType,
        IReadOnlyCollection<string>? additionalReservedIdsForMinting = null)
        => OpcRelationships.EnsureRelationshipForPackagePart(
            relsXml,
            packageRelNs,
            sourcePart,
            targetPart,
            relationshipType,
            XlsxPackagePath.ResolveRelationshipTarget,
            XlsxPackagePath.GetRelationshipTarget,
            additionalReservedIdsForMinting);
}
