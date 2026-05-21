using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxSlicerTimelineWriter
{
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

        foreach (var style in workbook.PivotTableStyles.Where(style => !string.IsNullOrWhiteSpace(style.Name)))
        {
            var styleXml = ToPivotTableStyleXml(style, workbookNs);
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

    private static XElement ToPivotTableStyleXml(PivotTableStyleModel style, XNamespace workbookNs) =>
        new(
            workbookNs + "tableStyle",
            new XAttribute("name", style.Name),
            new XAttribute("pivot", style.AppliesToPivotTables ? "1" : "0"),
            new XAttribute("table", style.AppliesToTables ? "1" : "0"),
            new XAttribute("count", style.Elements.Count.ToString(CultureInfo.InvariantCulture)),
            style.Elements
                .Where(element => !string.IsNullOrWhiteSpace(element.Type))
                .Select(element => new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", element.Type),
                    element.DifferentialFormatId is { } dxfId ? new XAttribute("dxfId", dxfId.ToString(CultureInfo.InvariantCulture)) : null,
                    element.Size is { } size ? new XAttribute("size", size.ToString(CultureInfo.InvariantCulture)) : null)));

    private static void SaveSlicerTimelines(ZipArchive archive, Workbook workbook)
    {
        XNamespace slicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
        XNamespace freexcelNs = "https://freexcel.local/xlsx/slicerTimelineState";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var slicerIndex = 1;
        foreach (var slicer in workbook.Slicers)
        {
            var slicerPath = string.IsNullOrWhiteSpace(slicer.PackagePart)
                ? $"xl/slicers/slicer{slicerIndex}.xml"
                : slicer.PackagePart.TrimStart('/').Replace('\\', '/');
            var cachePath = $"xl/slicerCaches/slicerCache{slicerIndex}.xml";

            XlsxPackageXmlEditor.ReplaceXml(archive, slicerPath, new XDocument(
                new XElement(slicerNs + "slicer",
                    new XAttribute("name", slicer.Name),
                    OptionalAttribute("caption", slicer.Caption),
                    OptionalAttribute("style", slicer.StyleName),
                    new XAttribute("cache", string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName))));
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, new XDocument(
                new XElement(slicerNs + "slicerCacheDefinition",
                    new XAttribute("name", string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName),
                    OptionalAttribute("sourceName", slicer.SourceFieldName),
                    new XElement(slicerNs + "pivotTables",
                        new XElement(slicerNs + "pivotTable", OptionalAttribute("name", slicer.SourcePivotTableName))),
                    new XElement(freexcelNs + "selectedItems",
                        slicer.SelectedItems.Select(item =>
                            new XElement(freexcelNs + "selectedItem", new XAttribute("value", item)))))));
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(slicerPath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdSlicerCache"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2007/relationships/slicerCache"),
                        new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(slicerPath, cachePath))))));
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

            XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, new XDocument(
                new XElement(slicerNs + "timeline",
                    new XAttribute("name", timeline.Name),
                    OptionalAttribute("caption", timeline.Caption),
                    OptionalAttribute("style", timeline.StyleName),
                    new XAttribute("cache", string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName))));
            XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, new XDocument(
                new XElement(slicerNs + "timelineCacheDefinition",
                    new XAttribute("name", string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName),
                    OptionalAttribute("sourceName", timeline.SourceFieldName),
                    OptionalAttribute("startDate", timeline.StartDate),
                    OptionalAttribute("endDate", timeline.EndDate),
                    OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                    OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                    new XElement(slicerNs + "pivotTables",
                        new XElement(slicerNs + "pivotTable", OptionalAttribute("name", timeline.SourcePivotTableName))))));
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(timelinePath), new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdTimelineCache"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/timelineCache"),
                        new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(timelinePath, cachePath))))));
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{timelinePath}", "application/vnd.ms-excel.timeline+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{cachePath}", "application/vnd.ms-excel.timelineCache+xml");
            timelineIndex++;
        }
    }

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}
