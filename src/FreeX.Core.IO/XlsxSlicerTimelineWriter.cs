using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;
using static FreeX.Core.IO.XlsxSlicerTimelineRelationshipTypes;

namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelineWriter
{
    private const string SlicerWorkbookExtensionUri = "{BBE1A952-AA13-448e-AADC-164F8A28A991}";
    private const string TimelineWorkbookExtensionUri = "{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}";
    private const string SlicerWorksheetExtensionUri = "{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}";
    private const string TimelineWorksheetExtensionUri = "{7E03D99C-DC04-49D9-9315-930204A7B6E9}";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

    /// <summary>
    /// R133x-io-slicer-timeline-multipivot-writer: resolves every distinct pivot table name a
    /// slicer/timeline connects to, primary name first. A slicer/timeline can drive SEVERAL pivot
    /// tables at once (Excel's "Report Connections") -- <paramref name="connectedNames"/> (populated at
    /// load with every <c>&lt;pivotTable&gt;</c> entry the control's cache carried, see
    /// <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>)
    /// is the authoritative list of ALL connections, while <paramref name="primaryName"/>
    /// (<c>SourcePivotTableName</c>) only ever tracks the first/primary one. This fresh-writer path only
    /// ever runs for a workbook with no preserved source package (see the <c>hasSourcePackage</c> gate at
    /// the call site) -- a multi-connection slicer reaches it when the connections were populated by a
    /// NON-xlsx load (e.g. FreeX's own native JSON format, which round-trips
    /// <c>ConnectedPivotTableNames</c> too) and the workbook is then saved AS xlsx for the first time, so
    /// this must author every connection, not just the primary one, or "Save As .xlsx" silently drops
    /// every Report Connection but the first. Falls back to a single-entry list of just
    /// <paramref name="primaryName"/> when <paramref name="connectedNames"/> is empty (the common,
    /// single-pivot-connection case), so an unchanged shape keeps producing the exact same XML as before.
    /// </summary>
    private static List<string> ResolveConnectedPivotTableNames(string? primaryName, IReadOnlyList<string> connectedNames)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryName))
            result.Add(primaryName);

        foreach (var name in connectedNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (!result.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                result.Add(name);
        }

        return result;
    }

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

    private static HashSet<int> GetUsedSlicerCacheIndices(ZipArchive archive, string directory, string stem)
    {
        var used = new HashSet<int>();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                var file = name[directory.Length..^".xml".Length];
                if (file.StartsWith(stem, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(file[stem.Length..], out var index))
                {
                    used.Add(index);
                }
            }
        }

        return used;
    }

    private static int AllocateNextIndex(HashSet<int> usedIndices)
    {
        var index = 1;
        while (usedIndices.Contains(index))
            index++;
        usedIndices.Add(index);
        return index;
    }

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

        // Allocate cache indices against existing archive entries so we never overwrite an
        // unrelated pre-existing slicerCache part. Track emitted cache names so a shared cache
        // (multiple slicers pointing at the same CacheName) is written exactly once.
        var usedSlicerCacheIndices = GetUsedSlicerCacheIndices(archive, "xl/slicerCaches/", "slicerCache");
        var emittedSlicerCachesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var slicerIndex = 1;
        foreach (var slicer in workbook.Slicers)
        {
            var slicerPath = string.IsNullOrWhiteSpace(slicer.PackagePart)
                ? $"xl/slicers/slicer{slicerIndex}.xml"
                : XlsxPackagePath.NormalizePackagePath(slicer.PackagePart);
            var cacheName = string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName;

            // Reuse an already-written cache part for slicers that share the same CacheName.
            var isNewCache = !emittedSlicerCachesByName.TryGetValue(cacheName, out var cachePath);
            if (isNewCache)
            {
                var cacheIndex = AllocateNextIndex(usedSlicerCacheIndices);
                cachePath = $"xl/slicerCaches/slicerCache{cacheIndex}.xml";
                emittedSlicerCachesByName[cacheName] = cachePath;
            }

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
                        new XAttribute("rowHeight", "228600"),
                        // P10: columnCount/showCaption are read by XlsxSlicerTimelineMetadataReader
                        // (defaults: columnCount=1, showCaption=true when absent) but were never
                        // emitted here, so a fresh save silently dropped a non-default tile-column
                        // layout or a hidden caption band on every round trip. Only emit when the
                        // value differs from Excel's default so an unchanged default-shaped slicer's
                        // XML stays exactly as before this fix.
                        slicer.ColumnCount != 1
                            ? new XAttribute("columnCount", slicer.ColumnCount.ToString(CultureInfo.InvariantCulture))
                            : null,
                        !slicer.ShowCaption
                            ? new XAttribute("showCaption", "0")
                            : null))));
            if (isNewCache)
            {
                // P11: a table slicer (SourceTableId set, no pivot binding) binds to a structured table via
                // an x15:tableSlicerCache ext, NOT a <pivotTables> element. A pivot slicer keeps the
                // <pivotTables> binding. The FreeX selected-item ext (when present) shares the same extLst.
                var isTableSlicer = slicer.SourceTableId is not null &&
                                    string.IsNullOrWhiteSpace(slicer.SourcePivotTableName);

                var extensions = new List<XElement>();
                if (isTableSlicer)
                {
                    extensions.Add(new XElement(WorkbookNs + "ext",
                        new XAttribute("uri", "{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}"),
                        new XElement(TimelineNs + "tableSlicerCache",
                            new XAttribute("tableId", slicer.SourceTableId!.Value.ToString(CultureInfo.InvariantCulture)),
                            slicer.SourceTableColumnId is { } tableColumn
                                ? new XAttribute("column", tableColumn.ToString(CultureInfo.InvariantCulture))
                                : null)));
                }

                if (slicer.SelectedItems.Count > 0)
                {
                    extensions.Add(new XElement(WorkbookNs + "ext",
                        new XAttribute("uri", "{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}"),
                        new XElement(freexNs + "selectedItems",
                            slicer.SelectedItems.Select(item =>
                                new XElement(freexNs + "selectedItem", new XAttribute("value", item))))));
                }

                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath!, new XDocument(
                    new XElement(SlicerNs + "slicerCacheDefinition",
                        slicer.SelectedItems.Count == 0
                            ? null
                            : new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                        isTableSlicer
                            ? new XAttribute(XNamespace.Xmlns + "x15", TimelineNs.NamespaceName)
                            : null,
                        new XAttribute("name", cacheName),
                        OptionalAttribute("sourceName", slicer.SourceFieldName),
                        // R133x-io-slicer-timeline-multipivot-writer: author EVERY connected pivot
                        // table (Excel's "Report Connections"), not just the primary one, or a
                        // multi-connection slicer's other connections are silently dropped on a fresh
                        // (no-source-package) save. See ResolveConnectedPivotTableNames.
                        isTableSlicer
                            ? null
                            : new XElement(SlicerNs + "pivotTables",
                                ResolveConnectedPivotTableNames(slicer.SourcePivotTableName, slicer.ConnectedPivotTableNames)
                                    .Select(pivotTableName => new XElement(
                                        SlicerNs + "pivotTable",
                                        OptionalAttribute("name", pivotTableName),
                                        new XAttribute("tabId", XlsxSlicerTimelinePackageAuthoring.ResolvePivotHostTabId(workbook, workbookXml, pivotTableName))))),
                        // P14 (R44-io-pivot-filter-page-3-2): a pivot slicer's <data><tabular><items>
                        // list is the ONLY thing real Excel (and FreeX's own reload, via
                        // XlsxSlicerTimelineMetadataReader.ReadSlicerCacheItems ->
                        // SlicerModel.CacheItems) draws its item/button tiles from -- the fx:
                        // selectedItems extLst below is a FreeX-private fallback, not something Excel
                        // or a fresh reload can see. Without it the just-inserted slicer renders with
                        // zero buttons. Emit it whenever the bound pivot cache field's shared items can
                        // be resolved by name; table slicers have no such field and keep relying on the
                        // x15:tableSlicerCache binding instead.
                        isTableSlicer
                            ? null
                            : XlsxPivotSlicerCacheData.BuildPivotSlicerCacheDataElement(workbook, slicer),
                        extensions.Count == 0
                            ? null
                            : new XElement(SlicerNs + "extLst", extensions))));
            }
            var resolvedCachePath = cachePath!;
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                resolvedCachePath,
                SlicerCacheRelationshipType);
            XlsxSlicerTimelinePackageAuthoring.EnsurePartRelationship(archive, slicerPath, resolvedCachePath, SlicerCacheRelationshipType);
            XlsxSlicerTimelinePackageAuthoring.EnsureWorkbookExtensionRef(
                workbookXml,
                SlicerNs,
                "x14",
                SlicerWorkbookExtensionUri,
                "slicerCaches",
                "slicerCache",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, slicer.SourceSheetName, slicer.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var slicerRelationshipId = XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetRelationship(archive, worksheetPath, slicerPath, SlicerRelationshipType);
                XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    SlicerNs,
                    "x14",
                    SlicerWorksheetExtensionUri,
                    "slicerList",
                    "slicer",
                    slicerRelationshipId);

                // R83-io-slicer-timeline-5-1: author the graphicFrame anchor too, or this slicer has no
                // on-sheet shape at all after this save (see XlsxSlicerTimelineDrawingWriter).
                XlsxSlicerTimelineDrawingWriter.EnsureSlicerAnchor(archive, worksheetPath, slicer);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{slicerPath}", "application/vnd.ms-excel.slicer+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{resolvedCachePath}", "application/vnd.ms-excel.slicerCache+xml");
            slicerIndex++;
        }

        // Allocate timeline cache indices against existing archive entries (same pattern as slicer caches).
        var usedTimelineCacheIndices = GetUsedSlicerCacheIndices(archive, "xl/timelineCaches/", "timelineCache");
        var emittedTimelineCachesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var timelineIndex = 1;
        foreach (var timeline in workbook.Timelines)
        {
            var timelinePath = string.IsNullOrWhiteSpace(timeline.PackagePart)
                ? $"xl/timelines/timeline{timelineIndex}.xml"
                : XlsxPackagePath.NormalizePackagePath(timeline.PackagePart);
            var cacheName = string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName;

            // Reuse an already-written cache part for timelines that share the same CacheName.
            var isNewTimelineCache = !emittedTimelineCachesByName.TryGetValue(cacheName, out var cachePath);
            if (isNewTimelineCache)
            {
                var cacheIndex = AllocateNextIndex(usedTimelineCacheIndices);
                cachePath = $"xl/timelineCaches/timelineCache{cacheIndex}.xml";
                emittedTimelineCachesByName[cacheName] = cachePath;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, new XDocument(
                new XElement(TimelineNs + "timelines",
                    new XElement(TimelineNs + "timeline",
                        new XAttribute("name", timeline.Name),
                        OptionalAttribute("caption", timeline.Caption),
                        OptionalAttribute("style", timeline.StyleName),
                        new XAttribute("cache", cacheName),
                        timeline.Level is { } lvl
                            ? new XAttribute("level", lvl.ToString(CultureInfo.InvariantCulture))
                            : null,
                        // G9: selectionLevel is independent from level; use SelectionLevel when set,
                        // fall back to Level so files without a distinct selection level still emit
                        // a valid selectionLevel attribute (Excel requires it when level is present).
                        (timeline.SelectionLevel ?? timeline.Level) is { } selLvl
                            ? new XAttribute("selectionLevel", selLvl.ToString(CultureInfo.InvariantCulture))
                            : null,
                        timeline.ScrollPosition is { Length: > 0 } scrollPos
                            ? new XAttribute("scrollPosition", scrollPos + "T00:00:00")
                            : null))));
            if (isNewTimelineCache)
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath!, new XDocument(
                    new XElement(TimelineNs + "timelineCacheDefinition",
                        new XAttribute("name", cacheName),
                        OptionalAttribute("sourceName", timeline.SourceFieldName),
                        OptionalAttribute("startDate", timeline.StartDate),
                        OptionalAttribute("endDate", timeline.EndDate),
                        OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                        OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                        // R133x-io-slicer-timeline-multipivot-writer: author EVERY connected pivot
                        // table, not just the primary one -- see the sibling slicer-cache comment above.
                        new XElement(TimelineNs + "pivotTables",
                            ResolveConnectedPivotTableNames(timeline.SourcePivotTableName, timeline.ConnectedPivotTableNames)
                                .Select(pivotTableName => new XElement(TimelineNs + "pivotTable", OptionalAttribute("name", pivotTableName)))))));
            }

            var resolvedTimelineCachePath = cachePath!;
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                resolvedTimelineCachePath,
                TimelineCacheRelationshipType);
            XlsxSlicerTimelinePackageAuthoring.EnsurePartRelationship(archive, timelinePath, resolvedTimelineCachePath, TimelineCacheRelationshipType);
            XlsxSlicerTimelinePackageAuthoring.EnsureWorkbookExtensionRef(
                workbookXml,
                TimelineNs,
                "x15",
                TimelineWorkbookExtensionUri,
                "timelineCacheRefs",
                "timelineCacheRef",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, timeline.SourceSheetName, timeline.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var timelineRelationshipId = XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetRelationship(archive, worksheetPath, timelinePath, TimelineRelationshipType);
                XlsxSlicerTimelinePackageAuthoring.EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    TimelineNs,
                    "x15",
                    TimelineWorksheetExtensionUri,
                    "timelineRefs",
                    "timelineRef",
                    timelineRelationshipId);

                // R83-io-slicer-timeline-5-1: author the graphicFrame anchor too, or this timeline has no
                // on-sheet shape at all after this save (see XlsxSlicerTimelineDrawingWriter).
                XlsxSlicerTimelineDrawingWriter.EnsureTimelineAnchor(archive, worksheetPath, timeline);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{timelinePath}", "application/vnd.ms-excel.Timeline+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{resolvedTimelineCachePath}", "application/vnd.ms-excel.TimelineCache+xml");
            timelineIndex++;
        }

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, workbookRelsPath, workbookRelsXml);
    }

    // R83-io-slicer-timeline-5-2: a slicer/timeline anchored on a DIFFERENT sheet than its bound pivot
    // table (a common "dashboard" pattern -- e.g. pivot on "Data", slicer placed on "Dashboard") must be
    // wired to ITS OWN sheet, not the pivot's -- so sourceSheetName is consulted FIRST and only falls
    // back to the pivot-table lookup (the control's default sheet, matching a same-sheet insert, where
    // SourceSheetName is never set) when it is absent.
    private static string ResolveWorksheetPath(Workbook workbook, string? sourceSheetName, string? sourcePivotTableName)
    {
        if (!string.IsNullOrWhiteSpace(sourceSheetName))
        {
            for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
            {
                if (string.Equals(workbook.Sheets[sheetIndex].Name, sourceSheetName, StringComparison.OrdinalIgnoreCase))
                    return $"xl/worksheets/sheet{sheetIndex + 1}.xml";
            }
        }

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

    private static XAttribute? OptionalAttribute(string name, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}
