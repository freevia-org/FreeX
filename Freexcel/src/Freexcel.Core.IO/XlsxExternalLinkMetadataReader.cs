using Freexcel.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxExternalLinkMetadataReader
{
    public static IReadOnlyList<ExternalLinkModel> Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return [];

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookXml = LoadXml(workbookEntry);
            var workbookRels = LoadRelationshipTargets(
                archive,
                "xl/_rels/workbook.xml.rels",
                "xl/workbook.xml",
                packageRelNs);
            var result = new List<ExternalLinkModel>();
            foreach (var externalReference in workbookXml.Root?
                         .Element(workbookNs + "externalReferences")?
                         .Elements(workbookNs + "externalReference") ?? [])
            {
                var relId = externalReference.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(relId) ||
                    !workbookRels.TryGetValue(relId, out var externalLinkPath))
                {
                    continue;
                }

                var model = new ExternalLinkModel { PackagePart = externalLinkPath };
                var externalLinkRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(externalLinkPath));
                if (externalLinkRelsEntry is not null)
                {
                    var externalLinkRelsXml = LoadXml(externalLinkRelsEntry);
                    var pathRelationship = externalLinkRelsXml.Root?
                        .Elements(packageRelNs + "Relationship")
                        .FirstOrDefault(relationship =>
                            (relationship.Attribute("Type")?.Value ?? "").EndsWith("/externalLinkPath", StringComparison.OrdinalIgnoreCase));
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

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relsXml = LoadXml(relsEntry);
        return relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.ResolveRelationshipTarget(sourcePart, e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
