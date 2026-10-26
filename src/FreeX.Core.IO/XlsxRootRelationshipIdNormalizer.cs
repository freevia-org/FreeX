using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// r313: makes the package-root relationship ids deterministic, so saving the same workbook twice
/// produces the same file.
///
/// <para>The OOXML packaging layer allocates the root <c>officeDocument</c> relationship a RANDOM id
/// (<c>Rb8ce4c41530e4534</c>, then <c>R7ea447fa93bb4cbf</c>, ...) while its siblings get the ordinary
/// <c>rId1</c>/<c>rId2</c>. Two saves of an unchanged workbook therefore differ, which costs the user
/// wherever a file is compared rather than opened: version control shows a change that is not one,
/// backup and sync tools re-upload an identical file, and content-hash caches miss.</para>
///
/// <para>Only <c>_rels/.rels</c> is touched, and only ids that are not already <c>rIdN</c>. A root
/// relationship is located by Type, never by Id -- unlike a part-level relationship, whose id appears
/// in <c>r:id</c> attributes elsewhere -- so renaming one here cannot break a reference. Ids that are
/// already well-formed keep their spelling, so this cannot renumber a package Excel wrote.</para>
/// </summary>
internal static class XlsxRootRelationshipIdNormalizer
{
    private const string RootRelationshipsPath = "_rels/.rels";
    private static readonly XNamespace RelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Regex WellFormedId = new(@"^rId\d+$", RegexOptions.Compiled);

    internal static void Normalize(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry(RootRelationshipsPath);
        if (entry is null)
            return;

        var document = XlsxPackageXmlEditor.LoadXml(entry);
        if (document.Root is not { } root || root.Name != RelationshipsNs + "Relationships")
            return;

        var relationships = root.Elements(RelationshipsNs + "Relationship").ToList();
        var used = relationships
            .Select(relationship => relationship.Attribute("Id")?.Value)
            .Where(id => id is not null && WellFormedId.IsMatch(id))
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        var next = 1;
        foreach (var relationship in relationships)
        {
            var id = relationship.Attribute("Id")?.Value;
            if (id is not null && WellFormedId.IsMatch(id))
                continue;

            // Only the officeDocument relationship. The first version of this renamed every
            // ill-formed root id and broke three customXml tests: a customXml root relationship's id
            // IS referenced -- by the item's property sidecar binding -- so renaming it unbinds the
            // part. officeDocument is the one relationship a reader finds by Type alone, and the only
            // one the packaging layer randomises.
            if (relationship.Attribute("Type")?.Value is not { } type
                || !type.EndsWith("/officeDocument", StringComparison.Ordinal))
            {
                continue;
            }

            while (!used.Add($"rId{next}"))
                next++;

            relationship.SetAttributeValue("Id", $"rId{next}");
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, RootRelationshipsPath, document);
    }
}
