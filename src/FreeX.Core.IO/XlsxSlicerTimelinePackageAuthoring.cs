using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelinePackageAuthoring
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static string ResolvePivotHostTabId(
        Workbook workbook,
        XDocument workbookXml,
        string? sourcePivotTableName)
    {
        if (string.IsNullOrWhiteSpace(sourcePivotTableName))
            return "1";

        string? hostSheetName = null;
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.PivotTables.Any(pivot =>
                    string.Equals(pivot.Name, sourcePivotTableName, StringComparison.OrdinalIgnoreCase)))
            {
                hostSheetName = sheet.Name;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(hostSheetName))
            return "1";

        var sheetsElement = workbookXml.Root?.Element(WorkbookNs + "sheets");
        foreach (var sheetElement in sheetsElement?.Elements(WorkbookNs + "sheet") ?? [])
        {
            if (string.Equals(sheetElement.Attribute("name")?.Value, hostSheetName, StringComparison.OrdinalIgnoreCase))
            {
                var sheetId = sheetElement.Attribute("sheetId")?.Value;
                if (!string.IsNullOrWhiteSpace(sheetId))
                    return sheetId;
                break;
            }
        }

        return "1";
    }

    public static void EnsurePartRelationship(
        ZipArchive archive,
        string sourcePart,
        string targetPart,
        string relationshipType)
    {
        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(sourcePart);
        var relsXml = archive.GetEntry(relationshipPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relsXml,
            PackageRelNs,
            sourcePart,
            targetPart,
            relationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, relationshipPath, relsXml);
    }

    public static string EnsureWorksheetRelationship(
        ZipArchive archive,
        string worksheetPath,
        string targetPart,
        string relationshipType)
    {
        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsXml = archive.GetEntry(relationshipPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        var relationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relsXml,
            PackageRelNs,
            worksheetPath,
            targetPart,
            relationshipType);
        XlsxPackageXmlEditor.ReplaceXml(archive, relationshipPath, relsXml);
        return relationshipId;
    }

    public static void EnsureWorkbookExtensionRef(
        XDocument workbookXml,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        EnsureExtensionRef(
            root,
            extensionNs,
            prefix,
            extensionUri,
            containerName,
            childName,
            relationshipId);
    }

    public static void EnsureWorksheetExtensionRef(
        ZipArchive archive,
        string worksheetPath,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var root = worksheetXml.Root;
        if (root is null)
            return;

        EnsureExtensionRef(
            root,
            extensionNs,
            prefix,
            extensionUri,
            containerName,
            childName,
            relationshipId);
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static void EnsureExtensionRef(
        XElement root,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId)
    {
        EnsureNamespace(root, "r", RelNs);
        EnsureNamespace(root, prefix, extensionNs);
        AddIgnorablePrefix(root, prefix);
        var extension = EnsureExtension(root, extensionNs, prefix, extensionUri);
        var container = extension.Element(extensionNs + containerName);
        if (container is null)
        {
            container = new XElement(extensionNs + containerName);
            extension.Add(container);
        }

        container.Elements(extensionNs + childName)
            .Where(element => string.Equals(
                element.Attribute(RelNs + "id")?.Value,
                relationshipId,
                StringComparison.OrdinalIgnoreCase))
            .Remove();
        container.Add(new XElement(extensionNs + childName, new XAttribute(RelNs + "id", relationshipId)));
    }

    private static XElement EnsureExtension(
        XElement root,
        XNamespace extensionNs,
        string prefix,
        string extensionUri)
    {
        var extensionList = root.Element(WorkbookNs + "extLst");
        if (extensionList is null)
        {
            extensionList = new XElement(WorkbookNs + "extLst");
            root.Add(extensionList);
        }

        XElement? extension = null;
        foreach (var element in extensionList.Elements(WorkbookNs + "ext"))
        {
            if (string.Equals(element.Attribute("uri")?.Value, extensionUri, StringComparison.OrdinalIgnoreCase))
            {
                extension = element;
                break;
            }
        }

        if (extension is not null)
        {
            extension.SetAttributeValue("uri", extensionUri);
            EnsureNamespace(extension, prefix, extensionNs);
            return extension;
        }

        extension = new XElement(
            WorkbookNs + "ext",
            new XAttribute("uri", extensionUri),
            new XAttribute(XNamespace.Xmlns + prefix, extensionNs.NamespaceName));
        extensionList.Add(extension);
        return extension;
    }

    private static void EnsureNamespace(XElement element, string prefix, XNamespace ns)
    {
        if (element.Attribute(XNamespace.Xmlns + prefix) is null)
            element.SetAttributeValue(XNamespace.Xmlns + prefix, ns.NamespaceName);
    }

    private static void AddIgnorablePrefix(XElement root, string prefix)
    {
        EnsureNamespace(root, "mc", MarkupCompatNs);
        var current = root.Attribute(MarkupCompatNs + "Ignorable")?.Value;
        var prefixes = (current ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (prefixes.Any(value => string.Equals(value, prefix, StringComparison.OrdinalIgnoreCase)))
            return;

        prefixes.Add(prefix);
        root.SetAttributeValue(MarkupCompatNs + "Ignorable", string.Join(" ", prefixes));
    }
}
