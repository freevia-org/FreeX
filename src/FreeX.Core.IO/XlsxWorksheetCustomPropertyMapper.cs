using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCustomPropertyMapper
{
    public static IReadOnlyList<WorksheetCustomProperty> Read(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var properties = new List<WorksheetCustomProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var customProperty in worksheetXml.Root?
                     .Element(worksheetNs + "customProperties")?
                     .Elements(worksheetNs + "customPr") ?? [])
        {
            var name = customProperty.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                !int.TryParse(customProperty.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                id <= 0 ||
                !seen.Add(name))
            {
                continue;
            }

            properties.Add(new WorksheetCustomProperty(name, id, ReadMetadata(customProperty)));
        }

        return properties;
    }

    public static void Save(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in workbook.Sheets)
        {
            var properties = sheet.CustomProperties
                .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(property => property.Id)
                .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (properties.Count == 0 || !sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "customProperties")?.Remove();
            InsertCustomPropertiesInOrder(root, workbookNs, new XElement(
                workbookNs + "customProperties",
                properties.Select(property => ToXml(property, workbookNs))));
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static HashSet<string> GetModeledNames(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return sheet.CustomProperties
            .Where(property => !string.IsNullOrWhiteSpace(property.Name) && property.Id > 0)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void InsertCustomPropertiesInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement customProperties)
    {
        string[] laterWorksheetElements =
        [
            "cellWatches",
            "ignoredErrors",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(customProperties);
        else
            insertionPoint.AddBeforeSelf(customProperties);
    }

    private static NativeXmlPreserveBag? ReadMetadata(XElement customProperty)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in customProperty.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledAttribute(attribute.Name.LocalName))
                continue;

            attrs[attribute.Name.ToString()] = attribute.Value;
        }

        var children = customProperty.Elements()
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

        var serialized = XmlNativeBagSerializer.Serialize(attrs, children);
        if (serialized is null)
            return null;

        var bag = new NativeXmlPreserveBag();
        bag.Set("customPr", serialized);
        return bag;
    }

    private static readonly IReadOnlyCollection<string> ModeledCustomPropertyAttributes = ["name", "id"];

    private static XElement ToXml(WorksheetCustomProperty property, XNamespace workbookNs)
    {
        var element = new XElement(
            workbookNs + "customPr",
            new XAttribute("name", property.Name),
            new XAttribute("id", property.Id.ToString(CultureInfo.InvariantCulture)));

        XmlNativeBagSerializer.ApplyToElement(element, property.Metadata?.Get("customPr"), ModeledCustomPropertyAttributes);

        return element;
    }

    private static bool IsModeledAttribute(string name) =>
        name is "name" or "id";

    private static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
