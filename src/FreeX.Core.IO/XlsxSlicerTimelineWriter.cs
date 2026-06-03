using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelineWriter
{
    private const string SlicerRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/slicer";
    private const string SlicerCacheRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/slicerCache";
    private const string TimelineRelationshipType = "http://schemas.microsoft.com/office/2010/relationships/Timeline";
    private const string TimelineCacheRelationshipType = "http://schemas.microsoft.com/office/2010/relationships/TimelineCache";
    private const string SlicerWorkbookExtensionUri = "{BBE1A952-AA13-448E-AADC-164F8A28A991}";
    private const string TimelineWorkbookExtensionUri = "{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}";
    private const string SlicerWorksheetExtensionUri = "{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}";
    private const string TimelineWorksheetExtensionUri = "{7E03D99C-DC04-49D9-9315-930204A7B6E9}";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

    public static void SavePivotTableStyles(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        SavePivotTableStyles(archive, workbook);
    }

    public static void SaveSlicerTimelines(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        SaveSlicerTimelines(archive, workbook);
    }

    private static void SavePivotTableStyles(ZipArchive archive, Workbook workbook)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return;

        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        var targetRoot = stylesXml.Root;
        if (targetRoot is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableStyles = targetRoot.Element(workbookNs + "tableStyles");
        if (tableStyles is null)
        {
            tableStyles = new XElement(workbookNs + "tableStyles");
            targetRoot.Add(tableStyles);
        }

        var existingStylesByName = tableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);
        var differentialStyleCount = targetRoot
            .Element(workbookNs + "dxfs")?
            .Elements(workbookNs + "dxf")
            .Count() ?? 0;

        foreach (var style in workbook.PivotTableStyles.Where(style => !string.IsNullOrWhiteSpace(style.Name)))
        {
            var styleXml = ToPivotTableStyleXml(style, workbookNs, differentialStyleCount);
            if (existingStylesByName.TryGetValue(style.Name, out var existingStyle))
                existingStyle.ReplaceWith(styleXml);
            else
                tableStyles.Add(styleXml);
        }

        tableStyles.SetAttributeValue(
            "count",
            tableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    private static XElement ToPivotTableStyleXml(
        PivotTableStyleModel style,
        XNamespace workbookNs,
        int differentialStyleCount)
    {
        var elements = style.Elements
            .Where(element => !string.IsNullOrWhiteSpace(element.Type))
            .Select(element => ToPivotTableStyleElementXml(element, workbookNs, differentialStyleCount))
            .ToList();

        return new XElement(
            workbookNs + "tableStyle",
            new XAttribute("name", style.Name),
            new XAttribute("pivot", style.AppliesToPivotTables ? "1" : "0"),
            new XAttribute("table", style.AppliesToTables ? "1" : "0"),
            new XAttribute("count", elements.Count.ToString(CultureInfo.InvariantCulture)),
            elements);
    }

    private static XElement ToPivotTableStyleElementXml(
        PivotTableStyleElementModel element,
        XNamespace workbookNs,
        int differentialStyleCount) =>
        new(
            workbookNs + "tableStyleElement",
            new XAttribute("type", element.Type),
            element.DifferentialFormatId is { } dxfId && dxfId >= 0 && dxfId < differentialStyleCount
                ? new XAttribute("dxfId", dxfId.ToString(CultureInfo.InvariantCulture))
                : null,
            element.Size is { } size ? new XAttribute("size", size.ToString(CultureInfo.InvariantCulture)) : null);

    private static void SaveSlicerTimelines(ZipArchive archive, Workbook workbook)
    {
        XNamespace freexNs = "https://freex.local/xlsx/slicerTimelineState";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(workbookRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var slicerIndex = 1;
        foreach (var slicer in workbook.Slicers)
        {
            var slicerPath = string.IsNullOrWhiteSpace(slicer.PackagePart)
                ? $"xl/slicers/slicer{slicerIndex}.xml"
                : slicer.PackagePart.TrimStart('/').Replace('\\', '/');
            var cachePath = $"xl/slicerCaches/slicerCache{slicerIndex}.xml";
            var cacheName = string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName;

            XlsxPackageXmlEditor.ReplaceXml(archive, slicerPath, new XDocument(
                new XElement(SlicerNs + "slicers",
                    new XAttribute(XNamespace.Xmlns + "mc", MarkupCompatNs.NamespaceName),
                    new XAttribute(MarkupCompatNs + "Ignorable", "x"),
                    new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                    new XElement(SlicerNs + "slicer",
                        new XAttribute("name", slicer.Name),
                        OptionalAttribute("caption", slicer.Caption),
                        OptionalAttribute("style", slicer.StyleName),
                        new XAttribute("cache", cacheName),
                        new XAttribute("rowHeight", "228600")))));
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, new XDocument(
                new XElement(SlicerNs + "slicerCacheDefinition",
                    new XAttribute("name", cacheName),
                    OptionalAttribute("sourceName", slicer.SourceFieldName),
                    new XElement(SlicerNs + "pivotTables",
                        new XElement(
                            SlicerNs + "pivotTable",
                            OptionalAttribute("name", slicer.SourcePivotTableName),
                            new XAttribute("tabId", "1"))),
                    slicer.SelectedItems.Count == 0
                        ? null
                        : new XElement(SlicerNs + "extLst",
                            new XElement(SlicerNs + "ext",
                                new XAttribute("uri", "{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}"),
                                new XElement(freexNs + "selectedItems",
                                    slicer.SelectedItems.Select(item =>
                                        new XElement(freexNs + "selectedItem", new XAttribute("value", item)))))))));
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                cachePath,
                SlicerCacheRelationshipType);
            EnsureWorkbookExtensionRef(
                workbookXml,
                SlicerNs,
                "x14",
                SlicerWorkbookExtensionUri,
                "slicerCaches",
                "slicerCache",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, slicer.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var slicerRelationshipId = EnsureWorksheetRelationship(archive, worksheetPath, slicerPath, SlicerRelationshipType);
                EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    SlicerNs,
                    "x14",
                    SlicerWorksheetExtensionUri,
                    "slicerList",
                    "slicer",
                    slicerRelationshipId);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{slicerPath}", "application/vnd.ms-excel.slicer+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.ms-excel.slicerCache+xml");
            slicerIndex++;
        }

        var timelineIndex = 1;
        foreach (var timeline in workbook.Timelines)
        {
            var timelinePath = string.IsNullOrWhiteSpace(timeline.PackagePart)
                ? $"xl/timelines/timeline{timelineIndex}.xml"
                : timeline.PackagePart.TrimStart('/').Replace('\\', '/');
            var cachePath = $"xl/timelineCaches/timelineCache{timelineIndex}.xml";
            var cacheName = string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName;

            XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, new XDocument(
                new XElement(TimelineNs + "timelines",
                    new XElement(TimelineNs + "timeline",
                        new XAttribute("name", timeline.Name),
                        OptionalAttribute("caption", timeline.Caption),
                        OptionalAttribute("style", timeline.StyleName),
                        new XAttribute("cache", cacheName)))));
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, new XDocument(
                new XElement(TimelineNs + "timelineCacheDefinition",
                    new XAttribute("name", cacheName),
                    OptionalAttribute("sourceName", timeline.SourceFieldName),
                    OptionalAttribute("startDate", timeline.StartDate),
                    OptionalAttribute("endDate", timeline.EndDate),
                    OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                    OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                    new XElement(TimelineNs + "pivotTables",
                        new XElement(TimelineNs + "pivotTable", OptionalAttribute("name", timeline.SourcePivotTableName))))));
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                cachePath,
                TimelineCacheRelationshipType);
            EnsureWorkbookExtensionRef(
                workbookXml,
                TimelineNs,
                "x15",
                TimelineWorkbookExtensionUri,
                "timelineCacheRefs",
                "timelineCacheRef",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, timeline.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var timelineRelationshipId = EnsureWorksheetRelationship(archive, worksheetPath, timelinePath, TimelineRelationshipType);
                EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    TimelineNs,
                    "x15",
                    TimelineWorksheetExtensionUri,
                    "timelineRefs",
                    "timelineRef",
                    timelineRelationshipId);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{timelinePath}", "application/vnd.ms-excel.Timeline+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.ms-excel.TimelineCache+xml");
            timelineIndex++;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static string ResolveWorksheetPath(Workbook workbook, string? sourcePivotTableName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePivotTableName))
        {
            for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
            {
                if (workbook.Sheets[sheetIndex].PivotTables.Any(pivot =>
                        string.Equals(pivot.Name, sourcePivotTableName, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"xl/worksheets/sheet{sheetIndex + 1}.xml";
                }
            }
        }

        return workbook.Sheets.Count == 0 ? "" : "xl/worksheets/sheet1.xml";
    }

    private static string EnsureWorksheetRelationship(
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

    private static void EnsureWorkbookExtensionRef(
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

        EnsureNamespace(root, "r", RelNs);
        EnsureNamespace(root, prefix, extensionNs);
        AddIgnorablePrefix(root, prefix);
        var extension = EnsureExtension(root, WorkbookNs, extensionNs, prefix, extensionUri);
        var container = extension.Element(extensionNs + containerName);
        if (container is null)
        {
            container = new XElement(extensionNs + containerName);
            extension.Add(container);
        }

        container.Elements(extensionNs + childName)
            .Where(element => string.Equals(element.Attribute(RelNs + "id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase))
            .Remove();
        container.Add(new XElement(extensionNs + childName, new XAttribute(RelNs + "id", relationshipId)));
    }

    private static void EnsureWorksheetExtensionRef(
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

        EnsureNamespace(root, "r", RelNs);
        EnsureNamespace(root, prefix, extensionNs);
        AddIgnorablePrefix(root, prefix);
        var extension = EnsureExtension(root, WorkbookNs, extensionNs, prefix, extensionUri);
        var container = extension.Element(extensionNs + containerName);
        if (container is null)
        {
            container = new XElement(extensionNs + containerName);
            extension.Add(container);
        }

        container.Elements(extensionNs + childName)
            .Where(element => string.Equals(element.Attribute(RelNs + "id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase))
            .Remove();
        container.Add(new XElement(extensionNs + childName, new XAttribute(RelNs + "id", relationshipId)));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
    }

    private static XElement EnsureExtension(
        XElement root,
        XNamespace workbookNs,
        XNamespace extensionNs,
        string prefix,
        string extensionUri)
    {
        var extensionList = root.Element(workbookNs + "extLst");
        if (extensionList is null)
        {
            extensionList = new XElement(workbookNs + "extLst");
            root.Add(extensionList);
        }

        var extension = extensionList.Elements(workbookNs + "ext")
            .FirstOrDefault(element => string.Equals(element.Attribute("uri")?.Value, extensionUri, StringComparison.OrdinalIgnoreCase));
        if (extension is not null)
        {
            EnsureNamespace(extension, prefix, extensionNs);
            return extension;
        }

        extension = new XElement(
            workbookNs + "ext",
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

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}
