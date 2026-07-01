using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSingleXmlCellMapper
{
    private const string SingleCellTableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.tableSingleCells+xml";
    private const string SingleCellTableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableSingleCells";

    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = OpcRelationships.Namespace;

    public static WorksheetSingleXmlCellsModel? Read(
        ZipArchive archive,
        string worksheetPath,
        XElement? directSingleXmlCells)
    {
        return ReadPartBackedSingleXmlCells(archive, worksheetPath) ?? Read(directSingleXmlCells);
    }

    public static WorksheetSingleXmlCellsModel? Read(XElement? singleXmlCells)
    {
        if (singleXmlCells is null)
            return null;

        var model = new WorksheetSingleXmlCellsModel();
        foreach (var attribute in singleXmlCells.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var cellElement in singleXmlCells.Elements(WorksheetNs + "singleXmlCell"))
        {
            var xmlCellPr = cellElement.Element(WorksheetNs + "xmlCellPr");
            var cell = new WorksheetSingleXmlCellModel
            {
                Id = ReadOptionalInt(cellElement.Attribute("id")?.Value),
                Reference = XlsxWorksheetNativeMetadataHelpers.NullIfWhiteSpace(cellElement.Attribute("r")?.Value),
                XmlCellPropertyId =
                    ReadOptionalInt(xmlCellPr?.Attribute("id")?.Value) ??
                    ReadOptionalInt(cellElement.Attribute("xmlCellPrId")?.Value) ??
                    ReadOptionalInt(cellElement.Attribute("connectionId")?.Value)
            };
            XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(
                cellElement,
                cell.NativeAttributes,
                ["id", "r", "xmlCellPrId", "connectionId"]);

            if (cell.Id is not null ||
                cell.Reference is not null ||
                cell.XmlCellPropertyId is not null ||
                cell.NativeAttributes.Count > 0)
            {
                model.Cells.Add(cell);
            }
        }

        return model.NativeAttributes.Count == 0 && model.Cells.Count == 0
            ? null
            : model;
    }

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets)
        {
            var singleXmlCells = sheet.SingleXmlCells;
            if (singleXmlCells is null)
                continue;

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            SaveWorksheetSingleXmlCells(archive, worksheetPath, singleXmlCells);
        }
    }

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var partEntry in archive.Entries.Where(IsSingleCellTablePartEntry).ToList())
        {
            var partXml = XlsxPackageXmlEditor.LoadXml(partEntry);
            var root = partXml.Root;
            if (root is null || !NormalizePartRoot(root))
                continue;

            XlsxPackageXmlEditor.ReplaceXml(archive, partEntry.FullName, partXml);
        }
    }

    private static WorksheetSingleXmlCellsModel? ReadPartBackedSingleXmlCells(
        ZipArchive archive,
        string worksheetPath)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return null;

        try
        {
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            foreach (var relationship in relsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
            {
                if (!string.Equals(relationship.Attribute("Type")?.Value, SingleCellTableRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var partPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
                var partEntry = archive.GetEntry(partPath);
                if (partEntry is null)
                    continue;

                var model = Read(XlsxPackageXmlEditor.LoadXml(partEntry).Root);
                if (model is not null)
                    return model;
            }
        }
        catch
        {
            // Single-cell table parts are optional metadata; malformed parts should not block workbook load.
        }

        return null;
    }

    private static void SaveWorksheetSingleXmlCells(
        ZipArchive archive,
        string worksheetPath,
        WorksheetSingleXmlCellsModel singleXmlCells)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var worksheetChanged = RemoveDirectWorksheetSingleXmlCells(worksheetXml.Root);

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var existingRelsEntry = archive.GetEntry(relsPath);
        var relsXml = existingRelsEntry is null
            ? OpcRelationships.CreateDocument()
            : XlsxPackageXmlEditor.LoadXml(existingRelsEntry);

        var existingPartPaths = RemoveSingleCellTableRelationships(relsXml, worksheetPath);
        foreach (var partPath in existingPartPaths)
            archive.GetEntry(partPath)?.Delete();
        OpcMediaTypes.RemoveOverrideContentTypes(archive, existingPartPaths);

        var partXml = ToPartXml(singleXmlCells);
        if (partXml is not null)
        {
            var partPath = existingPartPaths.Count == 0 ? NextSingleCellTablePartPath(archive) : existingPartPaths[0];
            XlsxPackageXmlEditor.ReplaceXml(archive, partPath, partXml);
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, partPath, SingleCellTableContentType);
            XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                relsXml,
                PackageRelNs,
                worksheetPath,
                partPath,
                SingleCellTableRelationshipType);
        }

        if (existingPartPaths.Count > 0 || partXml is not null)
            XlsxPackageXmlEditor.ReplaceXml(archive, relsPath, relsXml);

        if (worksheetChanged)
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XDocument? ToPartXml(WorksheetSingleXmlCellsModel? model)
    {
        if (model is null)
            return null;

        var element = new XElement(WorksheetNs + "singleXmlCells");
        var fallbackIndex = 1;
        foreach (var cell in model.Cells)
        {
            element.Add(ToSingleXmlCellXml(cell, fallbackIndex));
            fallbackIndex++;
        }

        return element.HasElements ? new XDocument(element) : null;
    }

    private static bool NormalizePartRoot(XElement singleXmlCells)
    {
        var changed = false;
        foreach (var attribute in singleXmlCells.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static XElement ToSingleXmlCellXml(WorksheetSingleXmlCellModel cell, int fallbackIndex)
    {
        var id = PositiveIntOrFallback(cell.Id, fallbackIndex);
        var xmlCellPropertyId = PositiveIntOrFallback(cell.XmlCellPropertyId, id);
        var reference = NormalizeCellReference(cell.Reference, fallbackIndex);

        return new XElement(
            WorksheetNs + "singleXmlCell",
            new XAttribute("id", id.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("r", reference),
            new XAttribute("connectionId", xmlCellPropertyId.ToString(CultureInfo.InvariantCulture)),
            new XElement(
                WorksheetNs + "xmlCellPr",
                new XAttribute("id", "1"),
                new XAttribute("uniqueName", $"SingleXmlCell{id.ToString(CultureInfo.InvariantCulture)}"),
                new XElement(
                    WorksheetNs + "xmlPr",
                    new XAttribute("mapId", xmlCellPropertyId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("xpath", $"/freex/singleXmlCell{id.ToString(CultureInfo.InvariantCulture)}"),
                    new XAttribute("xmlDataType", "string"))));
    }

    private static bool RemoveDirectWorksheetSingleXmlCells(XElement? worksheetRoot)
    {
        if (worksheetRoot is null)
            return false;

        var existing = worksheetRoot.Elements(WorksheetNs + "singleXmlCells").ToList();
        if (existing.Count == 0)
            return false;

        foreach (var element in existing)
            element.Remove();
        return true;
    }

    private static List<string> RemoveSingleCellTableRelationships(XDocument relsXml, string worksheetPath)
    {
        var root = relsXml.Root;
        if (root is null)
        {
            root = OpcRelationships.CreateRoot();
            relsXml.Add(root);
        }

        var partPaths = new List<string>();
        foreach (var relationship in root.Elements(PackageRelNs + "Relationship")
                     .Where(relationship => string.Equals(
                         relationship.Attribute("Type")?.Value,
                         SingleCellTableRelationshipType,
                         StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(target) &&
                !string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                partPaths.Add(XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target));
            }

            relationship.Remove();
        }

        return partPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NextSingleCellTablePartPath(ZipArchive archive)
    {
        for (var index = 1; ; index++)
        {
            var path = $"xl/tables/tableSingleCells{index.ToString(CultureInfo.InvariantCulture)}.xml";
            if (archive.GetEntry(path) is null)
                return path;
        }
    }

    private static string NormalizeCellReference(string? reference, int fallbackIndex)
    {
        var candidate = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (candidate is not null &&
            CellAddress.TryParse(candidate, SheetId.New(), out _))
        {
            return candidate;
        }

        return $"A{fallbackIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int PositiveIntOrFallback(int? value, int fallback) =>
        value is > 0 ? value.Value : fallback;

    private static int? ReadOptionalInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static bool IsSingleCellTablePartEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeEntryPath(entry);
        return path.StartsWith("xl/tables/tableSingleCells", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
