using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxHeaderFooterPicturePackageReader
{
    public static XlsxHeaderFooterPictureSets Read(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace vmlNs = "urn:schemas-microsoft-com:vml";
        XNamespace officeNs = "urn:schemas-microsoft-com:office:office";

        var relId = worksheetXml.Root?
            .Element(worksheetNs + "legacyDrawingHF")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(relId))
            return XlsxHeaderFooterPictureSets.Empty;

        var worksheetRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return XlsxHeaderFooterPictureSets.Empty;

        var worksheetRelsXml = XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
        var vmlRelationship = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relId, StringComparison.Ordinal));
        var vmlTarget = vmlRelationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(vmlTarget))
            return XlsxHeaderFooterPictureSets.Empty;

        var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget);
        var vmlEntry = archive.GetEntry(vmlPath);
        if (vmlEntry is null)
            return XlsxHeaderFooterPictureSets.Empty;

        var vmlXml = XlsxPackageXmlEditor.LoadXml(vmlEntry);
        var vmlRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(vmlPath));
        if (vmlRelsEntry is null)
            return XlsxHeaderFooterPictureSets.Empty;

        var vmlRelsXml = XlsxPackageXmlEditor.LoadXml(vmlRelsEntry);
        var pictures = new Dictionary<(XlsxHeaderFooterPictureSetKind Kind, XlsxHeaderFooterPicturePosition Position), WorksheetHeaderFooterPicture>();

        foreach (var shape in vmlXml.Descendants(vmlNs + "shape"))
        {
            var id = shape.Attribute("id")?.Value;
            var slot = XlsxHeaderFooterPicturePackagePlanner.Slots.FirstOrDefault(candidate => string.Equals(candidate.ShapeId, id, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
                continue;

            var rel = shape.Element(vmlNs + "imagedata")?.Attribute(officeNs + "relid")?.Value;
            if (string.IsNullOrWhiteSpace(rel))
                continue;

            var imageTarget = vmlRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, rel, StringComparison.Ordinal))
                ?.Attribute("Target")
                ?.Value;
            if (string.IsNullOrWhiteSpace(imageTarget))
                continue;

            var imagePath = XlsxPackagePath.ResolveRelationshipTarget(vmlPath, imageTarget);
            var imageEntry = archive.GetEntry(imagePath);
            if (imageEntry is null)
                continue;

            using var imageStream = imageEntry.Open();
            using var memory = new MemoryStream();
            imageStream.CopyTo(memory);
            pictures[(slot.Kind, slot.Position)] = new WorksheetHeaderFooterPicture(
                memory.ToArray(),
                XlsxPackagePath.GetImageContentType(imagePath),
                Path.GetFileName(imagePath),
                XlsxHeaderFooterPicturePackagePlanner.ParseStyleDimension(shape.Attribute("style")?.Value, "width") ?? 96,
                XlsxHeaderFooterPicturePackagePlanner.ParseStyleDimension(shape.Attribute("style")?.Value, "height") ?? 48);
        }

        return new XlsxHeaderFooterPictureSets(
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.PageHeader),
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.PageFooter),
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.FirstPageHeader),
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.FirstPageFooter),
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.EvenPageHeader),
            XlsxHeaderFooterPicturePackagePlanner.ToSet(pictures, XlsxHeaderFooterPictureSetKind.EvenPageFooter));
    }
}
