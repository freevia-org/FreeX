using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDigitalSignaturePackagePolicy
{
    public const string DigitalSignatureOriginRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin";

    public static IReadOnlySet<string> GetEditedSaveExclusions(ZipArchive sourceArchive)
    {
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sourceArchive.Entries)
        {
            var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
            if (IsDigitalSignaturePackagePath(path) ||
                IsVbaProjectSignaturePackagePath(path))
            {
                exclusions.Add(path);
            }
        }

        return exclusions;
    }

    public static bool IsDigitalSignaturePackagePath(string path) =>
        path.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase);

    public static bool IsVbaProjectSignaturePackagePath(string path) =>
        string.Equals(path, "xl/vbaProjectSignature.bin", StringComparison.OrdinalIgnoreCase);

    public static bool HasDigitalSignatureRelationship(ZipArchiveEntry relationshipEntry)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument relationshipsXml;
        try
        {
            relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        }
        catch
        {
            return false;
        }

        return relationshipsXml.Root?
            .Elements(relationshipNs + "Relationship")
            .Any(relationship => IsDigitalSignatureRelationshipType(relationship.Attribute("Type")?.Value)) == true;
    }

    private static bool IsDigitalSignatureRelationshipType(string? relationshipType) =>
        string.Equals(
            relationshipType?.Trim(),
            DigitalSignatureOriginRelationshipType,
            StringComparison.OrdinalIgnoreCase);
}
