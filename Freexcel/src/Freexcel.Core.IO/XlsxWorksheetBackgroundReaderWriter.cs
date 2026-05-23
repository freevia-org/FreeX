using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetBackgroundReaderWriter
{
    public static WorksheetBackgroundImage? Read(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relId = worksheetXml.Root?
            .Element(worksheetNs + "picture")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(relId))
            return null;

        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = LoadXml(relsEntry);
        var relationship = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(e => string.Equals(e.Attribute("Id")?.Value, relId, StringComparison.Ordinal));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var imagePath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
        var imageEntry = archive.GetEntry(imagePath);
        if (imageEntry is null)
            return null;

        using var imageStream = imageEntry.Open();
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        return new WorksheetBackgroundImage(
            ms.ToArray(),
            XlsxPackagePath.GetImageContentType(imagePath),
            Path.GetFileName(imagePath));
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return;

        var workbookXml = LoadXml(workbookEntry);
        var relsXml = LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(e => e.Attribute("Id") is not null && e.Attribute("Target") is not null)
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => XlsxPackagePath.NormalizeWorkbookTarget(e.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);
        var backgroundIndex = 1;
        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                continue;
            if (!sheetsByName.TryGetValue(name, out var sheet) || sheet.BackgroundImage is null)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteBackground(archive, worksheetPath, sheet.BackgroundImage, backgroundIndex++);
        }
    }

    private static void WriteBackground(
        ZipArchive archive,
        string worksheetPath,
        WorksheetBackgroundImage background,
        int backgroundIndex)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var extension = XlsxPackagePath.GetImageExtension(background.ContentType);
        var mediaFileName = XlsxPackagePath.GetWorksheetBackgroundMediaFileName(background.FileName, backgroundIndex, extension);
        var imagePath = $"xl/media/{mediaFileName}";
        archive.GetEntry(imagePath)?.Delete();
        var imageEntry = archive.CreateEntry(imagePath);
        using (var imageStream = imageEntry.Open())
            imageStream.Write(background.ImageBytes);

        XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension.TrimStart('.'), background.ContentType);

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        XDocument relsXml;
        if (relsEntry is null)
        {
            relsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        }
        else
        {
            relsXml = LoadXml(relsEntry);
            relsEntry.Delete();
        }

        var relId = XlsxPackageXmlEditor.NextRelationshipId(relsXml, packageRelNs);
        relsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", relId),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, imagePath))));

        var updatedRelsEntry = archive.CreateEntry(relsPath);
        using (var relsStream = updatedRelsEntry.Open())
            relsXml.Save(relsStream);

        var worksheetXml = LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        root.Elements(worksheetNs + "picture").Remove();
        root.Add(new XElement(worksheetNs + "picture", new XAttribute(relNs + "id", relId)));

        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
