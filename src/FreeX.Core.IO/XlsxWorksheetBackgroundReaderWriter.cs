using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads/writes worksheet background images. <see cref="Save"/> runs before
/// PreserveSourcePackageParts merges the source package's own xl/media/* entries into the
/// generated archive, so writing a background under the user's raw filename could otherwise
/// collide with (a) a source media part of the same name (shadowing an existing drawing's
/// picture with the background image once CopyUnknownPackageParts skips the now-duplicate
/// source entry) or (b) another sheet's background written earlier in the same save pass
/// (the second WriteBackground call would delete and overwrite the first sheet's media entry).
/// <see cref="Save"/> guards against both by reserving the source package's media names up
/// front and tracking every path it writes across the whole call.
/// </summary>
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
        var relationship = FindRelationshipById(relsXml, packageRelNs, relId);
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

    public static void Save(Stream xlsxStream, Workbook workbook, IReadOnlySet<string>? reservedMediaEntryNames = null)
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

        // Names already claimed for this save pass: the source package's own xl/media/* entries
        // (not yet copied into `archive` at this point — that happens later, in
        // PreserveSourcePackageParts/CopyUnknownPackageParts) plus every background media path
        // written earlier in this same loop, so two sheets whose background files share a name
        // (e.g. both "background.png") get distinct package paths instead of the second call
        // deleting and overwriting the first sheet's media entry.
        var claimedMediaEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reservedMediaEntryNames is not null)
            claimedMediaEntryNames.UnionWith(reservedMediaEntryNames);

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

            WriteBackground(archive, worksheetPath, sheet.BackgroundImage, backgroundIndex++, claimedMediaEntryNames);
        }
    }

    private static void WriteBackground(
        ZipArchive archive,
        string worksheetPath,
        WorksheetBackgroundImage background,
        int backgroundIndex,
        HashSet<string> claimedMediaEntryNames)
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

        // Never reuse a media path already claimed by the source package or by an earlier
        // background written in this same pass — that would delete/overwrite the other part
        // (see class remarks for the two corruption scenarios this guards against).
        if (!claimedMediaEntryNames.Add(imagePath))
        {
            var fallbackIndex = backgroundIndex;
            string fallbackPath;
            do
            {
                var fallbackFileName = $"freexBackground{fallbackIndex}{extension}";
                fallbackPath = $"xl/media/{fallbackFileName}";
                fallbackIndex++;
            }
            while (!claimedMediaEntryNames.Add(fallbackPath));
            imagePath = fallbackPath;
        }

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
        XlsxWorksheetElementOrder.Insert(root, new XElement(worksheetNs + "picture", new XAttribute(relNs + "id", relId)));

        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XElement? FindRelationshipById(XDocument relsXml, XNamespace packageRelNs, string relId)
    {
        var root = relsXml.Root;
        if (root is null)
            return null;

        foreach (var relationship in root.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(relationship.Attribute("Id")?.Value, relId, StringComparison.Ordinal))
                return relationship;
        }

        return null;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }
}
