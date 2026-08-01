using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

    /// <summary>
    /// Scans the SOURCE package for the "xl/drawings/freexHeaderFooterN.vml" index each sheet in
    /// <paramref name="sheetsToPreserve"/> is currently referenced by (via its worksheet's
    /// legacyDrawingHF relationship), so <see cref="Save"/> can steer its own sequential index
    /// allocator away from those numbers.
    /// </summary>
    /// <remarks>
    /// R112-io-hf-vml-path-collision: <see cref="Save"/> only ever numbers the sheets it is ABOUT
    /// TO rewrite (skipping every preserved sheet before incrementing its counter -- see the
    /// `continue` in that method), so the index it hands to <see cref="WriteSheetPictures"/> is a
    /// save-local sequence over the CHANGED sheets only, not a stable identity. A preserved sheet's
    /// existing legacyDrawingHF relationship still points at whatever "freexHeaderFooterN.vml" path
    /// it was assigned on an EARLIER save (via <see cref="XlsxWorksheetVmlReferencePreserver"/>,
    /// which copies that exact source part into the freshly generated package under the SAME path
    /// later in the save pipeline). If a changed sheet's freshly restarted counter lands on that
    /// same N, <see cref="WriteSheetPictures"/> unconditionally deletes/recreates that path (see its
    /// own doc comment) before the preserved sheet's copy is even written, so the preserved sheet's
    /// relationship silently ends up pointing at the OTHER sheet's picture. Reserving every
    /// preserved sheet's real on-disk index up front closes that hole without needing to touch the
    /// preservation pass itself.
    /// </remarks>
    public static IReadOnlySet<int> GetPreservedVmlIndices(
        Stream xlsxStream,
        Workbook workbook,
        IReadOnlySet<string> sheetsToPreserve)
    {
        var reserved = new HashSet<int>();
        if (sheetsToPreserve.Count == 0)
            return reserved;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relsEntry is null)
            return reserved;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relTargets = XlsxRelationshipReader.ReadTargets(
            relsXml,
            packageRelNs,
            XlsxPackagePath.NormalizeWorkbookTarget);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relId) || !sheetsToPreserve.Contains(name))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var vmlPath = TryResolveLegacyDrawingHfVmlPath(archive, worksheetPath, worksheetXml, workbookNs, relNs, packageRelNs);
            if (vmlPath is not null && ParseFreexHeaderFooterVmlIndex(vmlPath) is { } vmlIndex)
                reserved.Add(vmlIndex);
        }

        return reserved;
    }

    private static string? TryResolveLegacyDrawingHfVmlPath(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var relId = worksheetXml.Root?.Element(workbookNs + "legacyDrawingHF")?.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relId))
            return null;

        var worksheetRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (worksheetRelsEntry is null)
            return null;

        var worksheetRelsXml = XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
        var target = worksheetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value;

        return string.IsNullOrWhiteSpace(target) ? null : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static int? ParseFreexHeaderFooterVmlIndex(string vmlPath)
    {
        const string prefix = "xl/drawings/freexHeaderFooter";
        const string suffix = ".vml";
        if (vmlPath.Length <= prefix.Length + suffix.Length ||
            !vmlPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !vmlPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = vmlPath.Substring(prefix.Length, vmlPath.Length - prefix.Length - suffix.Length);
        return int.TryParse(digits, out var value) ? value : null;
    }

    public static void Save(
        Stream xlsxStream,
        Workbook workbook,
        IReadOnlySet<string>? sheetsToPreserve = null,
        IReadOnlySet<int>? reservedVmlIndices = null)
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

        // R112-io-hf-vml-path-collision: seed the allocator with every index a PRESERVED sheet's
        // legacyDrawingHF relationship already claims (see GetPreservedVmlIndices) so a sheet being
        // rewritten this save never restarts the counter onto a number an untouched sheet's marker
        // still points at. Candidates are also removed from availability as they're handed out, so
        // two sheets rewritten in the SAME call never collide with each other either.
        var usedIndices = reservedVmlIndices is { Count: > 0 } ? new HashSet<int>(reservedVmlIndices) : [];
        var nextCandidate = 1;

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

            while (usedIndices.Contains(nextCandidate))
                nextCandidate++;
            usedIndices.Add(nextCandidate);

            WriteSheetPictures(archive, worksheetPath, sheet, nextCandidate);
            nextCandidate++;
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

        var vmlPath = $"xl/drawings/freexHeaderFooter{sheetIndex}.vml";
        archive.GetEntry(vmlPath)?.Delete();
        archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(vmlPath))?.Delete();

        var vmlRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var shapes = new List<XElement>();
        var usedImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pictureIndex = 1;

        foreach (var slot in XlsxHeaderFooterPicturePackagePlanner.Slots)
        {
            var picture = XlsxHeaderFooterPicturePackagePlanner.GetPicture(sheet, slot.Kind, slot.Position);
            if (picture is null)
                continue;

            var extension = XlsxPackagePath.GetImageExtension(picture.ContentType);
            var imagePath = GetAvailableHeaderFooterImagePath(archive, usedImagePaths, picture.FileName, sheetIndex, pictureIndex, extension);
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
        InsertLegacyDrawingHeaderFooterInOrder(
            root,
            worksheetNs,
            new XElement(worksheetNs + "legacyDrawingHF", new XAttribute(relNs + "id", vmlRelId)));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static string GetAvailableHeaderFooterImagePath(
        ZipArchive archive,
        HashSet<string> usedImagePaths,
        string? fileName,
        int sheetIndex,
        int pictureIndex,
        string extension)
    {
        var mediaFileName = XlsxHeaderFooterPicturePackagePlanner.GetMediaFileName(fileName, sheetIndex, pictureIndex, extension);
        var candidatePath = $"xl/media/{mediaFileName}";
        if (archive.GetEntry(candidatePath) is null && usedImagePaths.Add(candidatePath))
            return candidatePath;

        var baseName = Path.GetFileNameWithoutExtension(mediaFileName);
        var candidateExtension = Path.GetExtension(mediaFileName);
        if (string.IsNullOrWhiteSpace(candidateExtension))
            candidateExtension = extension;

        for (var suffix = 1; ; suffix++)
        {
            candidatePath = $"xl/media/{baseName}_hf{sheetIndex}_{pictureIndex}_{suffix}{candidateExtension}";
            if (archive.GetEntry(candidatePath) is null && usedImagePaths.Add(candidatePath))
                return candidatePath;
        }
    }

    private static void InsertLegacyDrawingHeaderFooterInOrder(
        XElement worksheetRoot,
        XNamespace worksheetNs,
        XElement legacyDrawingHeaderFooter)
    {
        string[] laterWorksheetElements =
        [
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        XElement? insertionPoint = null;
        foreach (var element in worksheetRoot.Elements())
        {
            if (element.Name.Namespace != worksheetNs ||
                !laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                continue;
            }

            insertionPoint = element;
            break;
        }

        if (insertionPoint is null)
            worksheetRoot.Add(legacyDrawingHeaderFooter);
        else
            insertionPoint.AddBeforeSelf(legacyDrawingHeaderFooter);
    }

}
