using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxExternalLinkMetadataReader
{
    public static IReadOnlyList<ExternalLinkModel> Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive);
        }
        catch
        {
            return [];
        }
    }

    internal static IReadOnlyList<ExternalLinkModel> Load(ZipArchive archive)
    {
        try
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return [];

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = OpcRelationships.Namespace;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookRels = XlsxRelationshipReader.LoadTargets(
                archive,
                "xl/_rels/workbook.xml.rels",
                "xl/workbook.xml",
                packageRelNs);
            var result = new List<ExternalLinkModel>();
            var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var externalReference in workbookXml.Root?
                         .Element(workbookNs + "externalReferences")?
                         .Elements(workbookNs + "externalReference") ?? [])
            {
                var relId = externalReference.Attribute(relNs + "id")?.Value.Trim();
                if (string.IsNullOrWhiteSpace(relId) ||
                    !seenRelationshipIds.Add(relId) ||
                    !workbookRels.TryGetValue(relId, out var externalLinkPath))
                {
                    // Excel's '[n]' formula syntax addresses external references by their fixed
                    // ordinal position in workbook.xml's <externalReference> list, not by how many
                    // of them resolved. A blank/duplicated/unresolvable r:id still reserves its slot
                    // -- an empty placeholder -- so every later externalReference keeps the same
                    // '[n]' index the source file encoded, instead of silently shifting down.
                    result.Add(new ExternalLinkModel());
                    continue;
                }

                var model = new ExternalLinkModel { PackagePart = externalLinkPath };
                var externalLinkEntry = archive.GetEntry(externalLinkPath);
                if (externalLinkEntry is not null)
                {
                    var externalLinkXml = XlsxPackageXmlEditor.LoadXml(externalLinkEntry);
                    var externalBookElement = externalLinkXml.Root?.Element(workbookNs + "externalBook");
                    XlsxExternalLinkSchemaNormalizer.PopulateModelFromExternalBook(externalBookElement, model);
                }

                var externalLinkRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(externalLinkPath));
                if (externalLinkRelsEntry is not null)
                {
                    var externalLinkRelsXml = XlsxPackageXmlEditor.LoadXml(externalLinkRelsEntry);
                    var pathRelationship = externalLinkRelsXml.Root?
                        .Elements(packageRelNs + "Relationship")
                        .FirstOrDefault(relationship => (relationship.Attribute("Type")?.Value ?? "").EndsWith(
                            "/externalLinkPath",
                            StringComparison.OrdinalIgnoreCase));

                    model.TargetUri = pathRelationship?.Attribute("Target")?.Value;
                    model.TargetMode = pathRelationship?.Attribute("TargetMode")?.Value;
                }

                result.Add(model);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

}
