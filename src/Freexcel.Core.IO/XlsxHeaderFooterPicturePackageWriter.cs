using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxHeaderFooterPicturePackageWriter
{
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string VmlDrawingContentType =
        "application/vnd.openxmlformats-officedocument.vmlDrawing";

    public static IReadOnlySet<string> FindSheetsWithUnchangedSourcePictures(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            packageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var unchanged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet) || !XlsxHeaderFooterPicturePackagePlanner.HasPictures(sheet))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var sourcePictures = XlsxHeaderFooterPicturePackageReader.Read(archive, worksheetPath, worksheetXml);
            if (XlsxHeaderFooterPicturePackagePlanner.PictureSetsEqual(sourcePictures, sheet))
                unchanged.Add(name);
        }

        return unchanged;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, IReadOnlySet<string>? sheetsToPreserve = null)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            packageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var index = 1;

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet) || !XlsxHeaderFooterPicturePackagePlanner.HasPictures(sheet))
                continue;
            if (sheetsToPreserve?.Contains(name) == true)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteSheetPictures(archive, worksheetPath, sheet, index++);
        }
    }

    public static void RemoveClearedPictures(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            packageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet) || XlsxHeaderFooterPicturePackagePlanner.HasPictures(sheet))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            RemoveSheetHeaderFooterDrawing(archive, worksheetPath, workbookNs, relNs, packageRelNs);
        }
    }

    private static void RemoveSheetHeaderFooterDrawing(
        ZipArchive archive,
        string worksheetPath,
        XNamespace worksheetNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        var relId = root?
            .Element(worksheetNs + "legacyDrawingHF")?
            .Attribute(relNs + "id")?
            .Value;
        if (root is null)
            return;

        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        if (worksheetRelsEntry is null)
        {
            root.Elements(worksheetNs + "legacyDrawingHF").Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
            return;
        }

        var worksheetRelsXml = XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
        var relationships = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(element =>
                !string.IsNullOrWhiteSpace(relId)
                    ? string.Equals(element.Attribute("Id")?.Value, relId, StringComparison.Ordinal)
                    : root.Element(worksheetNs + "legacyDrawing") is null &&
                      string.Equals(
                          element.Attribute("Type")?.Value,
                          VmlDrawingRelationshipType,
                          StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? [];
        foreach (var relationship in relationships)
        {
            var vmlTarget = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(vmlTarget))
                DeletePackagePartGraph(archive, XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, vmlTarget), packageRelNs);

            relationship.Remove();
        }
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);
        root.Elements(worksheetNs + "legacyDrawingHF").Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static void DeletePackagePartGraph(ZipArchive archive, string partPath, XNamespace packageRelNs)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(partPath);
        if (archive.GetEntry(relsPath) is { } relsEntry)
        {
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            foreach (var target in relsXml.Root?
                         .Elements(packageRelNs + "Relationship")
                         .Where(relationship => !string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                         .Select(relationship => relationship.Attribute("Target")?.Value)
                         .Where(target => !string.IsNullOrWhiteSpace(target))
                     ?? [])
            {
                DeletePackagePartGraph(archive, XlsxPackagePath.ResolveRelationshipTarget(partPath, target!), packageRelNs);
            }

            relsEntry.Delete();
        }

        archive.GetEntry(partPath)?.Delete();
        RemoveSpecificContentType(archive, partPath);
    }

    private static void RemoveSpecificContentType(ZipArchive archive, string partPath)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        contentTypesXml.Root?
            .Elements(contentTypeNs + "Override")
            .Where(element => string.Equals(
                element.Attribute("PartName")?.Value,
                $"/{partPath.TrimStart('/')}",
                StringComparison.OrdinalIgnoreCase))
            .Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void WriteSheetPictures(ZipArchive archive, string worksheetPath, Sheet sheet, int sheetIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace vmlNs = "urn:schemas-microsoft-com:vml";
        XNamespace officeNs = "urn:schemas-microsoft-com:office:office";
        XNamespace excelNs = "urn:schemas-microsoft-com:office:excel";

        var vmlPath = $"xl/drawings/freexcelHeaderFooter{sheetIndex}.vml";
        archive.GetEntry(vmlPath)?.Delete();
        archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(vmlPath))?.Delete();

        var vmlRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var shapes = new List<XElement>();
        var pictureIndex = 1;

        foreach (var slot in XlsxHeaderFooterPicturePackagePlanner.Slots)
        {
            var picture = XlsxHeaderFooterPicturePackagePlanner.GetPicture(sheet, slot.Kind, slot.Position);
            if (picture is null)
                continue;

            var extension = XlsxPackagePath.GetImageExtension(picture.ContentType);
            var imagePath = $"xl/media/{XlsxHeaderFooterPicturePackagePlanner.GetMediaFileName(picture.FileName, sheetIndex, pictureIndex, extension)}";
            archive.GetEntry(imagePath)?.Delete();
            var imageEntry = archive.CreateEntry(imagePath, CompressionLevel.Optimal);
            using (var imageStream = imageEntry.Open())
                imageStream.Write(picture.ImageBytes);

            XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension.TrimStart('.'), picture.ContentType);
            var imageRelId = XlsxPackageXmlEditor.NextRelationshipId(vmlRelsXml, packageRelNs);
            vmlRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", imageRelId),
                new XAttribute("Type", ImageRelationshipType),
                new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(vmlPath, imagePath))));

            shapes.Add(new XElement(
                vmlNs + "shape",
                new XAttribute("id", slot.ShapeId),
                new XAttribute("type", "#_x0000_t75"),
                new XAttribute("style", FormattableString.Invariant($"width:{picture.Width:0.##}px;height:{picture.Height:0.##}px")),
                new XElement(
                    vmlNs + "imagedata",
                    new XAttribute(officeNs + "relid", imageRelId),
                    new XAttribute(officeNs + "title", Path.GetFileNameWithoutExtension(picture.FileName ?? $"HeaderFooter{pictureIndex}")))));

            pictureIndex++;
        }

        var vmlXml = new XDocument(
            new XElement(
                "xml",
                new XAttribute(XNamespace.Xmlns + "v", vmlNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "o", officeNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "x", excelNs.NamespaceName),
                shapes));
        XlsxPackageXmlEditor.ReplaceXml(archive, vmlPath, vmlXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(vmlPath), vmlRelsXml);
        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, vmlPath, VmlDrawingContentType);

        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        var worksheetRelsXml = worksheetRelsEntry is null
            ? new XDocument(new XElement(packageRelNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
        worksheetRelsEntry?.Delete();
        var vmlRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            worksheetRelsXml,
            packageRelNs,
            worksheetPath,
            vmlPath,
            VmlDrawingRelationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "legacyDrawingHF").Remove();
        root.Add(new XElement(worksheetNs + "legacyDrawingHF", new XAttribute(relNs + "id", vmlRelId)));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

}
