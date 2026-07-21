using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDigitalSignaturePackagePolicy
{
    public const string DigitalSignatureOriginRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin";

    // R60-io-vba-macro-6-2: the overall document digital signature (_xmlsignatures/*) always
    // becomes stale on ANY edited save -- it signs the whole package graph, which the save just
    // regenerated -- so it is always excluded. The VBA project's own signature
    // (xl/vbaProjectSignature.bin) is different: MS-OVBA computes it purely over the
    // _VBA_PROJECT stream inside xl/vbaProject.bin, and FreeX has no VBA editor -- every edited
    // save copies vbaProject.bin through byte-for-byte unchanged (CopyUnknownPackageParts).
    // Stripping a still-valid signature over unchanged macro bytes is unforced data loss (Excel
    // reports the macros as unsigned and a "digitally signed macros only" policy silently
    // disables them). Only exclude the VBA signature when the caller can positively confirm the
    // VBA project itself changed; today nothing in FreeX ever does, so it is always preserved.
    public static IReadOnlySet<string> GetEditedSaveExclusions(
        ZipArchive sourceArchive,
        bool vbaProjectChanged = false)
    {
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sourceArchive.Entries)
        {
            var path = XlsxPackagePath.NormalizeEntryPath(entry);
            if (IsDigitalSignaturePackagePath(path) ||
                (vbaProjectChanged && IsVbaProjectSignaturePackagePath(path)))
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
