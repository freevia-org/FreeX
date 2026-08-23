using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelineXmlAuthoring
{
    private const string SlicerSelectionExtensionUri = "{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}";
    private const string TableSlicerCacheExtensionUri = "{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
    private static readonly XNamespace FreexSelectionNs = "https://freex.local/xlsx/slicerTimelineState";

    public static XDocument BuildSlicerPart(SlicerModel slicer, string cacheName) =>
        new(
            new XElement(SlicerNs + "slicers",
                new XAttribute(XNamespace.Xmlns + "mc", MarkupCompatNs.NamespaceName),
                new XAttribute(MarkupCompatNs + "Ignorable", "x"),
                new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                new XElement(SlicerNs + "slicer",
                    new XAttribute("name", slicer.Name),
                    OptionalAttribute("caption", slicer.Caption),
                    OptionalAttribute("style", slicer.StyleName),
                    new XAttribute("cache", cacheName),
                    new XAttribute("rowHeight", "228600"),
                    slicer.ColumnCount != 1
                        ? new XAttribute("columnCount", slicer.ColumnCount.ToString(CultureInfo.InvariantCulture))
                        : null,
                    !slicer.ShowCaption
                        ? new XAttribute("showCaption", "0")
                        : null)));

    public static XDocument BuildSlicerCacheDefinition(
        Workbook workbook,
        XDocument workbookXml,
        SlicerModel slicer,
        string cacheName,
        IEnumerable<string?> pivotTableNames)
    {
        var isTableSlicer = slicer.SourceTableId is not null &&
                            string.IsNullOrWhiteSpace(slicer.SourcePivotTableName);
        var extensions = BuildSlicerCacheExtensions(slicer, isTableSlicer);

        return new XDocument(
            new XElement(SlicerNs + "slicerCacheDefinition",
                slicer.SelectedItems.Count == 0
                    ? null
                    : new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                isTableSlicer
                    ? new XAttribute(XNamespace.Xmlns + "x15", TimelineNs.NamespaceName)
                    : null,
                new XAttribute("name", cacheName),
                OptionalAttribute("sourceName", slicer.SourceFieldName),
                isTableSlicer
                    ? null
                    : new XElement(SlicerNs + "pivotTables",
                        pivotTableNames.Select(pivotTableName => new XElement(
                            SlicerNs + "pivotTable",
                            OptionalAttribute("name", pivotTableName),
                            new XAttribute(
                                "tabId",
                                XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(
                                    workbook,
                                    workbookXml,
                                    pivotTableName))))),
                isTableSlicer
                    ? null
                    : XlsxPivotSlicerCacheData.BuildPivotSlicerCacheDataElement(workbook, slicer),
                extensions.Count == 0
                    ? null
                    : new XElement(SlicerNs + "extLst", extensions)));
    }

    public static XDocument BuildTimelinePart(TimelineModel timeline, string cacheName) =>
        new(
            new XElement(TimelineNs + "timelines",
                new XElement(TimelineNs + "timeline",
                    new XAttribute("name", timeline.Name),
                    OptionalAttribute("caption", timeline.Caption),
                    OptionalAttribute("style", timeline.StyleName),
                    new XAttribute("cache", cacheName),
                    timeline.Level is { } level
                        ? new XAttribute("level", level.ToString(CultureInfo.InvariantCulture))
                        : null,
                    (timeline.SelectionLevel ?? timeline.Level) is { } selectionLevel
                        ? new XAttribute("selectionLevel", selectionLevel.ToString(CultureInfo.InvariantCulture))
                        : null,
                    timeline.ScrollPosition is { Length: > 0 } scrollPosition
                        ? new XAttribute("scrollPosition", scrollPosition + "T00:00:00")
                        : null)));

    public static XDocument BuildTimelineCacheDefinition(
        TimelineModel timeline,
        string cacheName,
        IEnumerable<string?> pivotTableNames) =>
        new(
            new XElement(TimelineNs + "timelineCacheDefinition",
                new XAttribute("name", cacheName),
                OptionalAttribute("sourceName", timeline.SourceFieldName),
                OptionalAttribute("startDate", timeline.StartDate),
                OptionalAttribute("endDate", timeline.EndDate),
                OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                new XElement(TimelineNs + "pivotTables",
                    pivotTableNames.Select(pivotTableName => new XElement(
                        TimelineNs + "pivotTable",
                        OptionalAttribute("name", pivotTableName))))));

    private static List<XElement> BuildSlicerCacheExtensions(SlicerModel slicer, bool isTableSlicer)
    {
        var extensions = new List<XElement>();
        if (isTableSlicer)
        {
            extensions.Add(new XElement(WorkbookNs + "ext",
                new XAttribute("uri", TableSlicerCacheExtensionUri),
                new XElement(TimelineNs + "tableSlicerCache",
                    new XAttribute("tableId", slicer.SourceTableId!.Value.ToString(CultureInfo.InvariantCulture)),
                    slicer.SourceTableColumnId is { } tableColumn
                        ? new XAttribute("column", tableColumn.ToString(CultureInfo.InvariantCulture))
                        : null)));
        }

        if (slicer.SelectedItems.Count > 0)
        {
            extensions.Add(new XElement(WorkbookNs + "ext",
                new XAttribute("uri", SlicerSelectionExtensionUri),
                new XElement(FreexSelectionNs + "selectedItems",
                    slicer.SelectedItems.Select(item =>
                        new XElement(FreexSelectionNs + "selectedItem", new XAttribute("value", item))))));
        }

        return extensions;
    }

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}
