using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal sealed record XlsxHeaderFooterPictureSets(
    WorksheetHeaderFooterPictureSet PageHeader,
    WorksheetHeaderFooterPictureSet PageFooter,
    WorksheetHeaderFooterPictureSet FirstPageHeader,
    WorksheetHeaderFooterPictureSet FirstPageFooter,
    WorksheetHeaderFooterPictureSet EvenPageHeader,
    WorksheetHeaderFooterPictureSet EvenPageFooter)
{
    public static XlsxHeaderFooterPictureSets Empty { get; } = new(
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty,
        WorksheetHeaderFooterPictureSet.Empty);
}

internal static class XlsxHeaderFooterPictureReaderWriter
{
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string VmlDrawingContentType =
        "application/vnd.openxmlformats-officedocument.vmlDrawing";

    private static readonly HeaderFooterPictureSlot[] Slots =
    [
        new("LH", HeaderFooterPictureSetKind.PageHeader, HeaderFooterPicturePosition.Left),
        new("CH", HeaderFooterPictureSetKind.PageHeader, HeaderFooterPicturePosition.Center),
        new("RH", HeaderFooterPictureSetKind.PageHeader, HeaderFooterPicturePosition.Right),
        new("LF", HeaderFooterPictureSetKind.PageFooter, HeaderFooterPicturePosition.Left),
        new("CF", HeaderFooterPictureSetKind.PageFooter, HeaderFooterPicturePosition.Center),
        new("RF", HeaderFooterPictureSetKind.PageFooter, HeaderFooterPicturePosition.Right),
        new("LFH", HeaderFooterPictureSetKind.FirstPageHeader, HeaderFooterPicturePosition.Left),
        new("CFH", HeaderFooterPictureSetKind.FirstPageHeader, HeaderFooterPicturePosition.Center),
        new("RFH", HeaderFooterPictureSetKind.FirstPageHeader, HeaderFooterPicturePosition.Right),
        new("LFF", HeaderFooterPictureSetKind.FirstPageFooter, HeaderFooterPicturePosition.Left),
        new("CFF", HeaderFooterPictureSetKind.FirstPageFooter, HeaderFooterPicturePosition.Center),
        new("RFF", HeaderFooterPictureSetKind.FirstPageFooter, HeaderFooterPicturePosition.Right),
        new("LEH", HeaderFooterPictureSetKind.EvenPageHeader, HeaderFooterPicturePosition.Left),
        new("CEH", HeaderFooterPictureSetKind.EvenPageHeader, HeaderFooterPicturePosition.Center),
        new("REH", HeaderFooterPictureSetKind.EvenPageHeader, HeaderFooterPicturePosition.Right),
        new("LEF", HeaderFooterPictureSetKind.EvenPageFooter, HeaderFooterPicturePosition.Left),
        new("CEF", HeaderFooterPictureSetKind.EvenPageFooter, HeaderFooterPicturePosition.Center),
        new("REF", HeaderFooterPictureSetKind.EvenPageFooter, HeaderFooterPicturePosition.Right)
    ];

    public static bool HasPictures(Sheet sheet) =>
        HasPictures(sheet.PageHeaderPictures) ||
        HasPictures(sheet.PageFooterPictures) ||
        HasPictures(sheet.FirstPageHeaderPictures) ||
        HasPictures(sheet.FirstPageFooterPictures) ||
        HasPictures(sheet.EvenPageHeaderPictures) ||
        HasPictures(sheet.EvenPageFooterPictures);

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
        var pictures = new Dictionary<(HeaderFooterPictureSetKind Kind, HeaderFooterPicturePosition Position), WorksheetHeaderFooterPicture>();

        foreach (var shape in vmlXml.Descendants(vmlNs + "shape"))
        {
            var id = shape.Attribute("id")?.Value;
            var slot = Slots.FirstOrDefault(candidate => string.Equals(candidate.ShapeId, id, StringComparison.OrdinalIgnoreCase));
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
                ParseStyleDimension(shape.Attribute("style")?.Value, "width") ?? 96,
                ParseStyleDimension(shape.Attribute("style")?.Value, "height") ?? 48);
        }

        return new XlsxHeaderFooterPictureSets(
            ToSet(pictures, HeaderFooterPictureSetKind.PageHeader),
            ToSet(pictures, HeaderFooterPictureSetKind.PageFooter),
            ToSet(pictures, HeaderFooterPictureSetKind.FirstPageHeader),
            ToSet(pictures, HeaderFooterPictureSetKind.FirstPageFooter),
            ToSet(pictures, HeaderFooterPictureSetKind.EvenPageHeader),
            ToSet(pictures, HeaderFooterPictureSetKind.EvenPageFooter));
    }

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
            if (!sheetsByName.TryGetValue(name, out var sheet) || !HasPictures(sheet))
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var sourcePictures = Read(archive, worksheetPath, worksheetXml);
            if (PictureSetsEqual(sourcePictures, sheet))
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
            if (!sheetsByName.TryGetValue(name, out var sheet) || !HasPictures(sheet))
                continue;
            if (sheetsToPreserve?.Contains(name) == true)
                continue;
            if (!relTargets.TryGetValue(relId, out var worksheetPath))
                continue;

            WriteSheetPictures(archive, worksheetPath, sheet, index++);
        }
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

        foreach (var slot in Slots)
        {
            var picture = GetPicture(sheet, slot.Kind, slot.Position);
            if (picture is null)
                continue;

            var extension = XlsxPackagePath.GetImageExtension(picture.ContentType);
            var imagePath = $"xl/media/{GetMediaFileName(picture.FileName, sheetIndex, pictureIndex, extension)}";
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

    private static WorksheetHeaderFooterPictureSet ToSet(
        IReadOnlyDictionary<(HeaderFooterPictureSetKind Kind, HeaderFooterPicturePosition Position), WorksheetHeaderFooterPicture> pictures,
        HeaderFooterPictureSetKind kind) =>
        new(
            pictures.TryGetValue((kind, HeaderFooterPicturePosition.Left), out var left) ? left : null,
            pictures.TryGetValue((kind, HeaderFooterPicturePosition.Center), out var center) ? center : null,
            pictures.TryGetValue((kind, HeaderFooterPicturePosition.Right), out var right) ? right : null);

    private static WorksheetHeaderFooterPicture? GetPicture(
        Sheet sheet,
        HeaderFooterPictureSetKind kind,
        HeaderFooterPicturePosition position)
    {
        var set = kind switch
        {
            HeaderFooterPictureSetKind.PageHeader => sheet.PageHeaderPictures,
            HeaderFooterPictureSetKind.PageFooter => sheet.PageFooterPictures,
            HeaderFooterPictureSetKind.FirstPageHeader => sheet.FirstPageHeaderPictures,
            HeaderFooterPictureSetKind.FirstPageFooter => sheet.FirstPageFooterPictures,
            HeaderFooterPictureSetKind.EvenPageHeader => sheet.EvenPageHeaderPictures,
            HeaderFooterPictureSetKind.EvenPageFooter => sheet.EvenPageFooterPictures,
            _ => WorksheetHeaderFooterPictureSet.Empty
        };

        return position switch
        {
            HeaderFooterPicturePosition.Left => set.Left,
            HeaderFooterPicturePosition.Center => set.Center,
            HeaderFooterPicturePosition.Right => set.Right,
            _ => null
        };
    }

    private static bool HasPictures(WorksheetHeaderFooterPictureSet set) =>
        set.Left is not null || set.Center is not null || set.Right is not null;

    private static bool PictureSetsEqual(XlsxHeaderFooterPictureSets sourcePictures, Sheet sheet) =>
        PictureSetEqual(sourcePictures.PageHeader, sheet.PageHeaderPictures) &&
        PictureSetEqual(sourcePictures.PageFooter, sheet.PageFooterPictures) &&
        PictureSetEqual(sourcePictures.FirstPageHeader, sheet.FirstPageHeaderPictures) &&
        PictureSetEqual(sourcePictures.FirstPageFooter, sheet.FirstPageFooterPictures) &&
        PictureSetEqual(sourcePictures.EvenPageHeader, sheet.EvenPageHeaderPictures) &&
        PictureSetEqual(sourcePictures.EvenPageFooter, sheet.EvenPageFooterPictures);

    private static bool PictureSetEqual(WorksheetHeaderFooterPictureSet left, WorksheetHeaderFooterPictureSet right) =>
        PictureEqual(left.Left, right.Left) &&
        PictureEqual(left.Center, right.Center) &&
        PictureEqual(left.Right, right.Right);

    private static bool PictureEqual(WorksheetHeaderFooterPicture? left, WorksheetHeaderFooterPicture? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.ContentType, right.ContentType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase) &&
               left.Width.Equals(right.Width) &&
               left.Height.Equals(right.Height) &&
               left.ImageBytes.AsSpan().SequenceEqual(right.ImageBytes);
    }

    private static string GetMediaFileName(string? fileName, int sheetIndex, int pictureIndex, string extension)
    {
        var candidate = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"freexcelHeaderFooter{sheetIndex}_{pictureIndex}{extension}";
        }

        return Path.HasExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    private static double? ParseStyleDimension(string? style, string name)
    {
        if (string.IsNullOrWhiteSpace(style))
            return null;

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !string.Equals(pieces[0], name, StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = pieces[1].Trim();
            if (raw.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                raw = raw[..^2];
            else if (raw.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[..^2];
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
                    ? points * (96.0 / 72.0)
                    : null;
            }

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
                ? pixels
                : null;
        }

        return null;
    }

    private sealed record HeaderFooterPictureSlot(
        string ShapeId,
        HeaderFooterPictureSetKind Kind,
        HeaderFooterPicturePosition Position);

    private enum HeaderFooterPictureSetKind
    {
        PageHeader,
        PageFooter,
        FirstPageHeader,
        FirstPageFooter,
        EvenPageHeader,
        EvenPageFooter
    }

    private enum HeaderFooterPicturePosition
    {
        Left,
        Center,
        Right
    }
}
