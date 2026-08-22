using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;
using static FreeX.Core.IO.XlsxSlicerTimelineRelationshipTypes;

namespace FreeX.Core.IO;

/// <summary>
/// P7 fix: slicer/timeline SELECTION/RANGE/LEVEL is discarded on a full save of an xlsx-loaded workbook.
/// On the source-package (loaded) save path the slicer/timeline/slicerCache/timelineCache parts are
/// PRESERVED verbatim by <c>PreserveSourcePackageParts</c> — so any change the in-memory model made to a
/// slicer's selected items, a timeline's selected date range, or a timeline's level/selectionLevel was
/// silently replayed back to the ORIGINAL values on save.
/// <para>
/// This rewriter runs AFTER the source parts have been preserved (so it edits each part at its final path)
/// and rewrites ONLY the selection/range/level values in place from the current model, mirroring exactly
/// what <see cref="XlsxSlicerTimelineMetadataReader"/> parses on load, and leaving every other byte
/// (graphicFrame, style, caption, columnCount, pivot binding, table binding, package graph) untouched. It
/// is a strict no-op when a control's model selection state is empty/absent and the preserved part already
/// carries no selection — this is what keeps the corpus/schema retention tests (whose fixtures declare no
/// selection) byte-stable, exactly like <see cref="XlsxSourceDrawingGeometryRewriter"/> does for anchors.
/// </para>
/// <para>
/// This mirrors the "re-apply model state onto the preserved part after preservation" shape used by
/// <see cref="XlsxSourceDrawingGeometryRewriter"/> and <see cref="XlsxX14DataValidationWriter"/>. It never
/// calls <see cref="XlsxSlicerTimelineWriter.SaveSlicerTimelines"/> (the fresh-writer emission), so it can
/// never clobber the preserved native XML or the critical package parts.
/// </para>
/// </summary>
internal static class XlsxSlicerTimelineStateRewriter
{
    // FreeX-custom extLst used by the fresh writer to persist a slicer's selected item CAPTIONS
    // (XlsxSlicerTimelineWriter emits <ext uri="{9F2C6F77-...}"><selectedItems><selectedItem value=".."/>).
    // The reader parses SelectedItems from any descendant <selectedItem @value> (namespace-tolerant).
    private static readonly XNamespace FreexSelectionNs = "https://freex.local/xlsx/slicerTimelineState";
    private const string SlicerSelectionExtensionUri = "{9F2C6F77-9A06-4E1E-AF41-4DB3CB03A6A6}";
    private const string TableSlicerCacheExtensionUri = "{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}";

    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // R37-io-slicer-timeline-1: namespaces/relationship types/extension URIs needed to author a brand-new
    // slicer/timeline part, mirroring XlsxSlicerTimelineWriter's fresh-save shape exactly (see
    // AppendNewControls below).
    private static readonly XNamespace SlicerXmlNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineXmlNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace MarkupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SlicerWorkbookExtensionUri = "{BBE1A952-AA13-448e-AADC-164F8A28A991}";
    private const string TimelineWorkbookExtensionUri = "{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}";
    private const string SlicerWorksheetExtensionUri = "{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}";
    private const string TimelineWorksheetExtensionUri = "{7E03D99C-DC04-49D9-9315-930204A7B6E9}";

    /// <summary>Cheap gate: is there any slicer/timeline whose selection/range/level the model can carry?</summary>
    public static bool HasSlicerTimelineState(Workbook workbook) =>
        workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0;

    public static void Save(Stream packageStream, Workbook workbook)
    {
        if (!HasSlicerTimelineState(workbook))
            return;

        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        // R37-io-slicer-timeline-1: a slicer/timeline added (AddSlicerCommand/AddTimelineCommand) to an
        // already-loaded (source-preserved) workbook has no preserved xl/slicers|timelines/* part at all --
        // PreserveSourcePackageParts only restores parts that existed in the ORIGINAL source archive, and
        // XlsxSlicerTimelineWriter.SaveSlicerTimelines (the only code that authors brand-new parts) is gated
        // to the no-source-package path, so it never runs here. Detect such controls (by name, against what
        // the archive already carries) and author their parts now, so the control is never silently dropped.
        // Must run before the rewrite passes below so a same-save selection on a brand-new control is
        // patched by the same logic that handles a preserved one.
        AppendNewControls(archive, workbook);

        RewriteSlicerSelections(archive, workbook);
        RewriteTimelineState(archive, workbook);
    }

    /// <summary>
    /// Authors package parts for any <see cref="SlicerModel"/>/<see cref="TimelineModel"/> in the workbook
    /// whose control NAME is not already represented by an <c>xl/slicers/</c>/<c>xl/timelines/</c> entry in
    /// the archive -- i.e. a control added to the in-memory model after the workbook was loaded, which
    /// PreserveSourcePackageParts (restoring only parts that existed in the ORIGINAL source archive) can
    /// never bring back. Mirrors <see cref="XlsxSlicerTimelineWriter.SaveSlicerTimelines"/>'s fresh-save
    /// shape for the new parts only; every already-preserved slicer/timeline part is left completely
    /// untouched (this never re-emits an existing part, so byte-for-byte preservation of unrelated/unedited
    /// controls is unaffected).
    /// </summary>
    private static void AppendNewControls(ZipArchive archive, Workbook workbook)
    {
        List<SlicerModel> newSlicers = [];
        if (workbook.Slicers.Count > 0)
        {
            var existingSlicerNames = CollectExistingControlNames(archive, "xl/slicers/", "slicer");
            newSlicers = workbook.Slicers
                .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name) && !existingSlicerNames.Contains(slicer.Name))
                .ToList();
        }

        List<TimelineModel> newTimelines = [];
        if (workbook.Timelines.Count > 0)
        {
            var existingTimelineNames = CollectExistingControlNames(archive, "xl/timelines/", "timeline");
            newTimelines = workbook.Timelines
                .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name) && !existingTimelineNames.Contains(timeline.Name))
                .ToList();
        }

        if (newSlicers.Count == 0 && newTimelines.Count == 0)
            return;

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        const string workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(workbookRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        if (newSlicers.Count > 0)
            AppendNewSlicers(archive, workbook, workbookXml, workbookRelsXml, worksheetPathMap, newSlicers);

        if (newTimelines.Count > 0)
            AppendNewTimelines(archive, workbook, workbookXml, workbookRelsXml, worksheetPathMap, newTimelines);

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        XlsxPackageXmlEditor.ReplaceXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static HashSet<string> CollectExistingControlNames(ZipArchive archive, string directory, string elementLocalName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, directory)))
        {
            var xml = XlsxPackageXmlEditor.LoadXml(entry);
            foreach (var element in EnumerateByLocalName(xml.Root, elementLocalName))
            {
                var name = element.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }

        return names;
    }

    private static void AppendNewSlicers(
        ZipArchive archive,
        Workbook workbook,
        XDocument workbookXml,
        XDocument workbookRelsXml,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        List<SlicerModel> newSlicers)
    {
        var usedSlicerIndices = GetUsedIndices(archive, "xl/slicers/", "slicer");
        var usedCacheIndices = GetUsedIndices(archive, "xl/slicerCaches/", "slicerCache");
        var emittedCachesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Seed already-emitted cache paths from the existing archive so a new slicer sharing a CacheName
        // with an already-preserved slicer reuses that cache instead of authoring a duplicate.
        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicerCaches/"))
                     .ToList())
        {
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var cacheName = cacheXml.Root?.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(cacheName))
                emittedCachesByName.TryAdd(cacheName, XlsxPackagePath.NormalizeEntryPath(cacheEntry));
        }

        foreach (var slicer in newSlicers)
        {
            var slicerIndex = AllocateNextIndex(usedSlicerIndices);
            var slicerPath = $"xl/slicers/slicer{slicerIndex}.xml";
            var cacheName = string.IsNullOrWhiteSpace(slicer.CacheName) ? $"Slicer_{slicerIndex}" : slicer.CacheName;

            var isNewCache = !emittedCachesByName.TryGetValue(cacheName, out var cachePath);
            if (isNewCache)
            {
                var cacheIndex = AllocateNextIndex(usedCacheIndices);
                cachePath = $"xl/slicerCaches/slicerCache{cacheIndex}.xml";
                emittedCachesByName[cacheName] = cachePath;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, slicerPath, new XDocument(
                new XElement(SlicerXmlNs + "slicers",
                    new XAttribute(XNamespace.Xmlns + "mc", MarkupCompatNs.NamespaceName),
                    new XAttribute(MarkupCompatNs + "Ignorable", "x"),
                    new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                    new XElement(SlicerXmlNs + "slicer",
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
                            : null))));

            if (isNewCache)
            {
                var isTableSlicer = slicer.SourceTableId is not null &&
                                    string.IsNullOrWhiteSpace(slicer.SourcePivotTableName);

                var extensions = new List<XElement>();
                if (isTableSlicer)
                {
                    extensions.Add(new XElement(WorkbookNs + "ext",
                        new XAttribute("uri", TableSlicerCacheExtensionUri),
                        new XElement(TimelineXmlNs + "tableSlicerCache",
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

                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath!, new XDocument(
                    new XElement(SlicerXmlNs + "slicerCacheDefinition",
                        slicer.SelectedItems.Count == 0
                            ? null
                            : new XAttribute(XNamespace.Xmlns + "x", WorkbookNs.NamespaceName),
                        isTableSlicer
                            ? new XAttribute(XNamespace.Xmlns + "x15", TimelineXmlNs.NamespaceName)
                            : null,
                        new XAttribute("name", cacheName),
                        OptionalAttribute("sourceName", slicer.SourceFieldName),
                        isTableSlicer
                            ? null
                            : new XElement(SlicerXmlNs + "pivotTables",
                                new XElement(
                                    SlicerXmlNs + "pivotTable",
                                    OptionalAttribute("name", slicer.SourcePivotTableName),
                                    new XAttribute("tabId", ResolvePivotHostTabId(workbook, workbookXml, slicer.SourcePivotTableName)))),
                        // R84-io-slicer-append-tabular: a pivot slicer ADDED to an already-loaded
                        // (source-preserved) workbook (AddSlicerCommand) must carry the SAME native
                        // <data><tabular pivotCacheId=".."><items><i x=".." s="1"/> list the fresh writer
                        // emits (R44-io-pivot-filter-page-3-2) -- it is the ONLY form real Excel and FreeX's
                        // own reload (XlsxSlicerTimelineMetadataReader -> SlicerModel.CacheItems) draw the
                        // slicer's item tiles from; the fx: selectedItems extLst below is a FreeX-private
                        // fallback neither reads. Shared with the fresh writer via XlsxPivotSlicerCacheData so
                        // both stamp the required pivotCacheId from the OWNING pivot cache's id
                        // (R83-io-slicer-tabular-pivotcacheid). Null for a table slicer (no bound pivot cache
                        // field), keeping it purely on the x15:tableSlicerCache binding exactly as before.
                        isTableSlicer
                            ? null
                            : XlsxPivotSlicerCacheData.BuildPivotSlicerCacheDataElement(workbook, slicer),
                        extensions.Count == 0
                            ? null
                            : new XElement(SlicerXmlNs + "extLst", extensions))));
            }

            var resolvedCachePath = cachePath!;
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                resolvedCachePath,
                SlicerCacheRelationshipType);
            EnsurePartRelationship(archive, slicerPath, resolvedCachePath, SlicerCacheRelationshipType);
            EnsureWorkbookExtensionRef(
                workbookXml,
                SlicerXmlNs,
                "x14",
                SlicerWorkbookExtensionUri,
                "slicerCaches",
                "slicerCache",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, worksheetPathMap, slicer.SourceSheetName, slicer.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var slicerRelationshipId = EnsureWorksheetRelationship(archive, worksheetPath, slicerPath, SlicerRelationshipType);
                EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    SlicerXmlNs,
                    "x14",
                    SlicerWorksheetExtensionUri,
                    "slicerList",
                    "slicer",
                    slicerRelationshipId);

                // R83-io-slicer-timeline-5-1: author the graphicFrame anchor too, or this brand-new
                // slicer has no on-sheet shape at all after this save (see XlsxSlicerTimelineDrawingWriter).
                XlsxSlicerTimelineDrawingWriter.EnsureSlicerAnchor(archive, worksheetPath, slicer);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{slicerPath}", "application/vnd.ms-excel.slicer+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{resolvedCachePath}", "application/vnd.ms-excel.slicerCache+xml");
        }
    }

    private static void AppendNewTimelines(
        ZipArchive archive,
        Workbook workbook,
        XDocument workbookXml,
        XDocument workbookRelsXml,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        List<TimelineModel> newTimelines)
    {
        var usedTimelineIndices = GetUsedIndices(archive, "xl/timelines/", "timeline");
        var usedTimelineCacheIndices = GetUsedIndices(archive, "xl/timelineCaches/", "timelineCache");
        var emittedCachesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelineCaches/"))
                     .ToList())
        {
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var cacheName = cacheXml.Root?.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(cacheName))
                emittedCachesByName.TryAdd(cacheName, XlsxPackagePath.NormalizeEntryPath(cacheEntry));
        }

        foreach (var timeline in newTimelines)
        {
            var timelineIndex = AllocateNextIndex(usedTimelineIndices);
            var timelinePath = $"xl/timelines/timeline{timelineIndex}.xml";
            var cacheName = string.IsNullOrWhiteSpace(timeline.CacheName) ? $"Timeline_{timelineIndex}" : timeline.CacheName;

            var isNewTimelineCache = !emittedCachesByName.TryGetValue(cacheName, out var cachePath);
            if (isNewTimelineCache)
            {
                var cacheIndex = AllocateNextIndex(usedTimelineCacheIndices);
                cachePath = $"xl/timelineCaches/timelineCache{cacheIndex}.xml";
                emittedCachesByName[cacheName] = cachePath;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, new XDocument(
                new XElement(TimelineXmlNs + "timelines",
                    new XElement(TimelineXmlNs + "timeline",
                        new XAttribute("name", timeline.Name),
                        OptionalAttribute("caption", timeline.Caption),
                        OptionalAttribute("style", timeline.StyleName),
                        new XAttribute("cache", cacheName),
                        timeline.Level is { } lvl
                            ? new XAttribute("level", lvl.ToString(CultureInfo.InvariantCulture))
                            : null,
                        (timeline.SelectionLevel ?? timeline.Level) is { } selLvl
                            ? new XAttribute("selectionLevel", selLvl.ToString(CultureInfo.InvariantCulture))
                            : null,
                        timeline.ScrollPosition is { Length: > 0 } scrollPos
                            ? new XAttribute("scrollPosition", scrollPos + "T00:00:00")
                            : null))));

            if (isNewTimelineCache)
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath!, new XDocument(
                    new XElement(TimelineXmlNs + "timelineCacheDefinition",
                        new XAttribute("name", cacheName),
                        OptionalAttribute("sourceName", timeline.SourceFieldName),
                        OptionalAttribute("startDate", timeline.StartDate),
                        OptionalAttribute("endDate", timeline.EndDate),
                        OptionalAttribute("selectedStartDate", timeline.SelectedStartDate),
                        OptionalAttribute("selectedEndDate", timeline.SelectedEndDate),
                        new XElement(TimelineXmlNs + "pivotTables",
                            new XElement(TimelineXmlNs + "pivotTable", OptionalAttribute("name", timeline.SourcePivotTableName))))));
            }

            var resolvedTimelineCachePath = cachePath!;
            var cacheRelationshipId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                workbookRelsXml,
                PackageRelNs,
                "xl/workbook.xml",
                resolvedTimelineCachePath,
                TimelineCacheRelationshipType);
            EnsurePartRelationship(archive, timelinePath, resolvedTimelineCachePath, TimelineCacheRelationshipType);
            EnsureWorkbookExtensionRef(
                workbookXml,
                TimelineXmlNs,
                "x15",
                TimelineWorkbookExtensionUri,
                "timelineCacheRefs",
                "timelineCacheRef",
                cacheRelationshipId);

            var worksheetPath = ResolveWorksheetPath(workbook, worksheetPathMap, timeline.SourceSheetName, timeline.SourcePivotTableName);
            if (!string.IsNullOrWhiteSpace(worksheetPath))
            {
                var timelineRelationshipId = EnsureWorksheetRelationship(archive, worksheetPath, timelinePath, TimelineRelationshipType);
                EnsureWorksheetExtensionRef(
                    archive,
                    worksheetPath,
                    TimelineXmlNs,
                    "x15",
                    TimelineWorksheetExtensionUri,
                    "timelineRefs",
                    "timelineRef",
                    timelineRelationshipId);

                // R83-io-slicer-timeline-5-1: author the graphicFrame anchor too, or this brand-new
                // timeline has no on-sheet shape at all after this save (see XlsxSlicerTimelineDrawingWriter).
                XlsxSlicerTimelineDrawingWriter.EnsureTimelineAnchor(archive, worksheetPath, timeline);
            }

            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{timelinePath}", "application/vnd.ms-excel.Timeline+xml");
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, $"/{resolvedTimelineCachePath}", "application/vnd.ms-excel.TimelineCache+xml");
        }
    }

    // Mirrors XlsxSlicerTimelineWriter.ResolvePivotHostTabId: the slicerCache's pivotTable/@tabId is the
    // sheetId of the worksheet hosting the pivot, resolved by name; falls back to "1" for a degenerate
    // package (no matching sheet/sheetId found).
    private static string ResolvePivotHostTabId(Workbook workbook, XDocument workbookXml, string? sourcePivotTableName)
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

    // Resolves the ACTUAL preserved worksheet part path for the sheet HOSTING THE CONTROL ITSELF, via the
    // real workbook-sheet-to-part-path map (XlsxWorkbookWorksheetPathMap) rather than assuming a fresh-save
    // "sheetN.xml" naming convention -- a source-preserved package's worksheet part names do not necessarily
    // match the model's sheet order, so the fresh writer's naive index-based fallback would be wrong here.
    // R83-io-slicer-timeline-5-2: a slicer/timeline anchored on a DIFFERENT sheet than its bound pivot
    // table (a common "dashboard" pattern -- e.g. pivot on "Data", slicer placed on "Dashboard") must be
    // wired to ITS OWN sheet, not the pivot's -- so sourceSheetName is consulted FIRST and only falls back
    // to a pivot-table lookup (the control's default sheet, matching a same-sheet insert) when it is
    // absent, exactly mirroring how a freshly-inserted control (which never sets SourceSheetName) still
    // resolves correctly to the pivot's host sheet.
    private static string? ResolveWorksheetPath(
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        string? sourceSheetName,
        string? sourcePivotTableName)
    {
        if (worksheetPathMap is null)
            return null;

        if (!string.IsNullOrWhiteSpace(sourceSheetName) &&
            worksheetPathMap.SheetPathsByName.TryGetValue(sourceSheetName, out var directPath))
        {
            return directPath;
        }

        if (string.IsNullOrWhiteSpace(sourcePivotTableName))
            return null;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.PivotTables.Any(pivot =>
                    string.Equals(pivot.Name, sourcePivotTableName, StringComparison.OrdinalIgnoreCase)) &&
                worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var path))
            {
                return path;
            }
        }

        return null;
    }

    private static HashSet<int> GetUsedIndices(ZipArchive archive, string directory, string stem)
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

    private static void EnsurePartRelationship(
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

        XElement? extension = null;
        foreach (var element in extensionList.Elements(workbookNs + "ext"))
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

    private static void RewriteSlicerSelections(ZipArchive archive, Workbook workbook)
    {
        if (workbook.Slicers.Count == 0)
            return;

        // Model slicers keyed by their control name (the association the reader uses).
        var slicersByName = new Dictionary<string, SlicerModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var slicer in workbook.Slicers)
            slicersByName.TryAdd(slicer.Name, slicer);

        // Resolve, per slicer part, which cache part backs each <slicer cache="..."> so we can patch the
        // matching cache root. Caches are keyed by their root @name (same as the reader).
        var cacheNamesBySlicerName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slicerEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicers/"))
                     .ToList())
        {
            var slicerXml = XlsxPackageXmlEditor.LoadXml(slicerEntry);
            foreach (var slicerElement in EnumerateByLocalName(slicerXml.Root, "slicer"))
            {
                var name = slicerElement.Attribute("name")?.Value;
                var cacheName = slicerElement.Attribute("cache")?.Value;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(cacheName))
                    cacheNamesBySlicerName.TryAdd(name, cacheName);
            }
        }

        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicerCaches/"))
                     .ToList())
        {
            var cachePath = XlsxPackagePath.NormalizeEntryPath(cacheEntry);
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheName = root.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(cacheName))
                continue;

            // Find a model slicer bound to this cache (by <slicer cache="..">). If none of the slicers that
            // reference this cache is present in the model, leave the part alone.
            //
            // R49-io-slicer-timeline-3-2: Excel's "linked slicers" (the same slicer widget copied to
            // another sheet) share one slicerCache, so MULTIPLE slicer names can map to this cacheName
            // here. Only one of their SlicerModel instances is the one a SetSlicerSelectionCommand
            // actually mutated -- its SelectionCaptured flag is the only signal distinguishing "the user's
            // live edit" from a linked sibling's stale post-load snapshot. Prefer a captured model over
            // whichever match is enumerated first, so the user's change isn't silently reverted by an
            // untouched linked sibling; fall back to the first match when none has captured a change
            // (idempotent re-save of an untouched shared cache -- matches the previous behaviour exactly).
            SlicerModel? model = null;
            foreach (var pair in cacheNamesBySlicerName)
            {
                if (string.Equals(pair.Value, cacheName, StringComparison.OrdinalIgnoreCase) &&
                    slicersByName.TryGetValue(pair.Key, out var candidate))
                {
                    model ??= candidate;
                    if (candidate.SelectionCaptured)
                    {
                        model = candidate;
                        break;
                    }
                }
            }

            if (model is null)
                continue;

            var changed = RewriteSlicerCacheSelection(root, model);
            changed |= RewriteNativeCacheItemSelection(archive, root, model, workbook);
            changed |= RewriteCachePivotTableBinding(root, model.ConnectedPivotTableNames, model.SourcePivotTableName);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, cacheXml);
        }
    }

    /// <summary>
    /// Reconciles the cache part's FreeX selected-item extLst (<c>&lt;selectedItem value=".."/&gt;</c>) with
    /// the model's <see cref="SlicerModel.SelectedItems"/>, the exact list the reader parses into
    /// <c>SelectedItems</c>. Returns true when the part XML changed. No-op (returns false) when the model
    /// has no selection AND the part carries none, so a corpus cache with no selection stays byte-stable.
    /// </summary>
    private static bool RewriteSlicerCacheSelection(XElement cacheRoot, SlicerModel model)
    {
        var existing = cacheRoot
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "selectedItem", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("value")?.Value ?? "")
            .ToList();

        var desired = model.SelectedItems;

        // Nothing to do when both are empty (keeps no-selection corpus caches untouched), or when the
        // preserved list already equals the model list (idempotent re-save of an unchanged workbook).
        if (existing.Count == desired.Count && existing.SequenceEqual(desired, StringComparer.Ordinal))
            return false;

        // Drop any existing FreeX selected-item extLst so we can re-emit the model's list cleanly, leaving
        // every other extLst ext (and every other cache attribute/child) intact.
        cacheRoot
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "selectedItems", StringComparison.OrdinalIgnoreCase))
            .Where(element => element.Ancestors().Any(ancestor =>
                string.Equals(ancestor.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ancestor.Attribute("uri")?.Value, SlicerSelectionExtensionUri, StringComparison.OrdinalIgnoreCase)))
            .Remove();
        RemoveEmptyFreexSelectionExtensions(cacheRoot);

        if (desired.Count == 0)
            return true;

        var slicerNs = cacheRoot.Name.Namespace;
        var extList = cacheRoot.Element(slicerNs + "extLst");
        if (extList is null)
        {
            extList = new XElement(slicerNs + "extLst");
            cacheRoot.Add(extList);
        }

        extList.Add(new XElement(WorkbookNs + "ext",
            new XAttribute("uri", SlicerSelectionExtensionUri),
            new XElement(FreexSelectionNs + "selectedItems",
                desired.Select(item =>
                    new XElement(FreexSelectionNs + "selectedItem", new XAttribute("value", item))))));
        return true;
    }

    private static void RemoveEmptyFreexSelectionExtensions(XElement cacheRoot)
    {
        cacheRoot
            .Descendants()
            .Where(element =>
                string.Equals(element.Name.LocalName, "ext", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("uri")?.Value, SlicerSelectionExtensionUri, StringComparison.OrdinalIgnoreCase) &&
                !element.HasElements)
            .Remove();
    }

    /// <summary>
    /// R69-io-slicer-timeline-6-2: rewrites the persisted <c>&lt;pivotTables&gt;&lt;pivotTable name=".."/&gt;</c>
    /// binding inside a slicerCache/timelineCache definition to match the model's CURRENT
    /// <see cref="SlicerModel.SourcePivotTableName"/>/<see cref="TimelineModel.SourcePivotTableName"/> value.
    /// Renaming a connected pivot table (<c>RenamePivotTableCommand</c>) updates only the in-memory model's
    /// source name; on the hasSourcePackage save path the cache part is otherwise preserved verbatim, so the
    /// saved <c>&lt;pivotTable name="..."/&gt;</c> would keep naming the OLD pivot table, breaking the
    /// slicer/timeline-to-pivot connection on reopen. Shared by both <see cref="RewriteSlicerSelections"/>
    /// and <see cref="RewriteTimelineState"/> since a slicerCacheDefinition and a timelineCacheDefinition
    /// both carry the binding in the identical <c>&lt;pivotTables&gt;&lt;pivotTable name="..."/&gt;</c> shape.
    /// No-op when the model carries no pivot binding (e.g. a table slicer's cache has no
    /// <c>&lt;pivotTables&gt;</c> element at all) or when the preserved name already matches the model
    /// (idempotent re-save of an un-renamed pivot table stays byte-stable).
    /// <para>
    /// R133-io-slicer-timeline-multipivot: a slicer/timeline can be connected to SEVERAL pivot tables at
    /// once, so the preserved <c>&lt;pivotTables&gt;</c> list can carry more than one
    /// <c>&lt;pivotTable name=".."/&gt;</c> entry. The old implementation stamped EVERY entry with the
    /// single <paramref name="currentPivotTableName"/> (the model's primary connection only), which
    /// silently collapsed every other connection onto that one name on save -- the other pivot tables then
    /// stop being driven by the control on reopen. When <paramref name="connectedPivotTableNames"/> (read
    /// at load from every <c>&lt;pivotTable&gt;</c> entry this same cache carries, see
    /// <see cref="XlsxSlicerTimelineMetadataReader"/>) accounts for every preserved entry, each entry is
    /// reconciled POSITIONALLY against its own list slot instead -- a rename
    /// (<see cref="FreeX.Core.Commands.RenamePivotTableCommand"/> updates matching entries in that list
    /// too, see its Apply/Revert) only touches the ONE renamed entry, leaving every other connection
    /// exactly as preserved. Falls back to the legacy single-name behaviour when the list is empty (a
    /// freshly-authored, never-loaded control) or its count no longer matches the preserved entries (an
    /// unexpected structural change underneath us) -- both keep the common single-pivot case working
    /// exactly as before.
    /// </para>
    /// </summary>
    private static bool RewriteCachePivotTableBinding(
        XElement cacheRoot, IReadOnlyList<string> connectedPivotTableNames, string? currentPivotTableName)
    {
        var pivotTablesElement = cacheRoot
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "pivotTables", StringComparison.OrdinalIgnoreCase));
        if (pivotTablesElement is null)
            return false;

        var pivotTableElements = pivotTablesElement.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "pivotTable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pivotTableElements.Count == 0)
            return false;

        if (connectedPivotTableNames.Count == pivotTableElements.Count)
        {
            var changed = false;
            for (var i = 0; i < pivotTableElements.Count; i++)
            {
                // R133-io-slicer-timeline-multipivot-2: entry 0 is always driven by currentPivotTableName
                // (SourcePivotTableName) rather than connectedPivotTableNames[0]. SourcePivotTableName is
                // the single authoritative "primary connection" field -- every rename path
                // (RenamePivotTableCommand) updates it AND keeps connectedPivotTableNames[0] in lockstep,
                // but nothing enforces that invariant against a caller that mutates SourcePivotTableName
                // directly (e.g. FreeXR69SlicerTimelinePivotRenameTests exercises exactly that shape).
                // Trusting a possibly-stale connectedPivotTableNames[0] there would silently re-save the
                // OLD pivot name for the primary connection -- the exact bug this rewriter exists to
                // prevent. Entries beyond index 0 have no such single-field mirror, so they still come
                // positionally from the list.
                var desiredName = i == 0 && !string.IsNullOrWhiteSpace(currentPivotTableName)
                    ? currentPivotTableName
                    : connectedPivotTableNames[i];
                if (string.IsNullOrWhiteSpace(desiredName))
                    continue;
                changed |= SetOptionalAttribute(pivotTableElements[i], "name", desiredName);
            }

            return changed;
        }

        if (string.IsNullOrWhiteSpace(currentPivotTableName))
            return false;

        var legacyChanged = false;
        foreach (var pivotTableElement in pivotTableElements)
            legacyChanged |= SetOptionalAttribute(pivotTableElement, "name", currentPivotTableName);

        return legacyChanged;
    }

    /// <summary>
    /// R11-xlsx-pivot-slicer-1: a pivot slicer cache's NATIVE selection form is
    /// <c>&lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt;</c> (see
    /// <see cref="XlsxSlicerTimelineMetadataReader"/>'s <c>ReadSlicerCacheItems</c>) — Excel reads the
    /// selection from THESE flags, never from the FreeX-private extLst that
    /// <see cref="RewriteSlicerCacheSelection"/> reconciles. On a source-preserved workbook these native
    /// <c>&lt;i s="1"&gt;</c> flags are copied verbatim, so a FreeX-side selection change (which only
    /// updates <see cref="SlicerModel.SelectedItems"/>, not <see cref="SlicerModel.CacheItems"/>) never
    /// reached them and Excel kept showing the stale selection. This resolves each cache item's caption
    /// from the pivot cache field's shared items (mirroring FreeX.Core.Commands.SlicerItemResolver's
    /// normalization) and rewrites its <c>s</c>
    /// flag to match whether that caption is in the model's current <see cref="SlicerModel.SelectedItems"/>.
    /// No-op when the part carries no native tabular items, or when every flag already matches the model
    /// (idempotent re-save of an unchanged workbook stays byte-stable). Also a no-op when
    /// <see cref="SlicerModel.SelectedItems"/> is empty AND <see cref="SlicerModel.SelectionCaptured"/> is
    /// false: an empty selection is otherwise ambiguous — it is the model's post-load default (the Core.IO
    /// load path never populates it from these native flags; only the host UI's
    /// <c>SlicerItemResolver.ResolvePivotCacheItems</c> projects a PARTIAL native selection into it, and even
    /// that resolver deliberately skips projecting when every item is selected) AND it is what a user's
    /// explicit Clear-Filter (<c>SetSlicerSelectionCommand</c> with an empty list) produces.
    /// <see cref="SlicerModel.SelectionCaptured"/> disambiguates only this empty case: false means "the
    /// model never captured/changed the selection" (leave the preserved native <c>s</c> flags untouched);
    /// true with an empty <see cref="SlicerModel.SelectedItems"/> means "the user explicitly cleared the
    /// filter to select-all" and every native <c>s</c> flag must be stripped so the clear round-trips instead
    /// of silently reverting to the stale native selection. A non-empty <see cref="SlicerModel.SelectedItems"/>
    /// always rewrites the native flags to match it, regardless of <see cref="SlicerModel.SelectionCaptured"/>.
    /// </summary>
    private static bool RewriteNativeCacheItemSelection(ZipArchive archive, XElement cacheRoot, SlicerModel model, Workbook workbook)
    {
        var itemsElement = cacheRoot
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "items", StringComparison.OrdinalIgnoreCase));
        if (itemsElement is null)
            return false;

        // R117-io-slicer-cacheitem-growth: append a native <i x="N"/> for every index the model's
        // CacheItems now carries (PivotTableRefreshService.ExtendBoundSlicerCacheItems appends one when
        // a refresh discovers a new distinct pivot-cache value) that this preserved part does not yet
        // represent -- BEFORE and INDEPENDENT of the SelectedItems/SelectionCaptured-gated rewrite
        // below, since a plain Refresh-then-Save with no selection change at all must still persist the
        // new item, or it is invisible again on the very next reload (the bug this fix addresses).
        var changed = AppendMissingCacheItemEntries(itemsElement, model);

        if (model.SelectedItems.Count == 0 && !model.SelectionCaptured)
            return changed;

        // R26-io-pivot-deep-2: resolve captions from the RAW <sharedItems> XML (indexed exactly as Excel
        // wrote it, including <m/> missing-value slots) -- NOT from PivotCacheFieldModel.SharedItems, which
        // XlsxPivotCacheReader has already filtered <m/> out of, shifting every later index out of alignment
        // with the native <i x="N"> this loop is patching.
        var rawCaptions = ResolveRawSharedItemCaptions(archive, workbook, model);
        if (rawCaptions is null)
            return changed;

        var selected = new HashSet<string>(model.SelectedItems, StringComparer.OrdinalIgnoreCase);

        foreach (var itemElement in itemsElement.Elements())
        {
            if (!string.Equals(itemElement.Name.LocalName, "i", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(itemElement.Attribute("x")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;
            if (index < 0 || index >= rawCaptions.Count)
                continue;

            var caption = rawCaptions[index];
            if (string.IsNullOrEmpty(caption))
                continue;

            var shouldBeSelected = selected.Contains(caption);
            changed |= SetSelectedFlag(itemElement, shouldBeSelected);
        }

        return changed;
    }

    /// <summary>
    /// R117-io-slicer-cacheitem-growth: appends a native <c>&lt;i x="N"/&gt;</c> element for every
    /// <see cref="SlicerCacheItem"/> in <paramref name="model"/>'s <see cref="SlicerModel.CacheItems"/>
    /// whose <see cref="SlicerCacheItem.Index"/> is not already present as an <c>&lt;i x="N"&gt;</c>
    /// under <paramref name="itemsElement"/> -- this is the ONLY thing that lets a pivot slicer loaded
    /// from an existing file ever surface a value that first appeared after the file was last saved
    /// (<see cref="PivotTableRefreshService"/>'s in-memory append onto <see cref="SlicerModel.CacheItems"/>
    /// otherwise has nowhere to go on save, and the item is invisible again on the very next reload).
    /// Appended purely by INDEX -- no caption/<see cref="SlicerModel.SelectedItems"/> lookup needed --
    /// using the model's own <see cref="SlicerCacheItem.IsSelected"/> (set true by
    /// <c>PivotTableRefreshService.ExtendBoundSlicerCacheItems</c> for a brand-new item, mirroring
    /// Excel's own "include new items" default) for the new element's <c>s</c> flag, via the same
    /// omit-when-false convention <see cref="SetSelectedFlag"/> uses. New elements are appended, in
    /// ascending index order, at the END of the existing sequence -- the OOXML schema places no
    /// ordering constraint on sibling <c>&lt;i&gt;</c> elements, and appending (rather than inserting in
    /// index order) never disturbs any already-preserved entry or its position. A strict no-op (and the
    /// existing sequence is left byte-identical) when every model CacheItem index is already present.
    /// </summary>
    private static bool AppendMissingCacheItemEntries(XElement itemsElement, SlicerModel model)
    {
        if (model.CacheItems.Count == 0)
            return false;

        var existingIndices = new HashSet<int>();
        foreach (var itemElement in itemsElement.Elements())
        {
            if (!string.Equals(itemElement.Name.LocalName, "i", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(itemElement.Attribute("x")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var existingIndex))
                existingIndices.Add(existingIndex);
        }

        var itemNamespace = itemsElement.Name.Namespace;
        var changed = false;
        foreach (var cacheItem in model.CacheItems.OrderBy(item => item.Index))
        {
            if (!existingIndices.Add(cacheItem.Index))
                continue;

            var newElement = new XElement(
                itemNamespace + "i",
                new XAttribute("x", cacheItem.Index.ToString(CultureInfo.InvariantCulture)));
            if (cacheItem.IsSelected)
                newElement.SetAttributeValue("s", "1");

            itemsElement.Add(newElement);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Sets/clears the <c>s</c> (selected) boolean attribute on a native <c>&lt;i&gt;</c> cache item.
    /// Excel's default for an absent <c>s</c> is unselected, so a false value REMOVES the attribute rather
    /// than writing <c>s="0"</c>, keeping an all-cleared re-save shaped like Excel's own output.
    /// </summary>
    private static bool SetSelectedFlag(XElement itemElement, bool selected)
    {
        var current = string.Equals(itemElement.Attribute("s")?.Value, "1", StringComparison.Ordinal);
        if (current == selected)
            return false;

        if (selected)
            itemElement.SetAttributeValue("s", "1");
        else
            itemElement.SetAttributeValue("s", null);
        return true;
    }

    /// <summary>
    /// Resolves, for the pivot cache field backing this slicer's <see cref="SlicerModel.SourceFieldName"/>
    /// (the same name-match association FreeX.Core.Commands.SlicerItemResolver uses), the per-index caption
    /// list read directly from the pivot cache definition part's RAW <c>&lt;sharedItems&gt;</c> XML -- one
    /// entry per child element, in document order, so index N lines up with a native
    /// <c>&lt;i x="N"/&gt;</c>'s own index space exactly as Excel wrote it. A <c>&lt;m/&gt;</c>
    /// (missing-value) slot (or any item with no <c>v</c> attribute) resolves to <see langword="null"/> so it
    /// can never satisfy a selection match, but it still OCCUPIES its slot -- unlike
    /// <see cref="PivotCacheFieldModel.SharedItems"/>, which <c>XlsxPivotCacheReader</c> has already filtered
    /// such items out of, shifting every later index (R26-io-pivot-deep-2).
    /// <para>
    /// R37-io-slicer-timeline-2: a TABLE slicer (<see cref="SlicerModel.SourceTableId"/> set) has no pivot
    /// cache binding at all, so the pivot-cache lookup below always falls through to <see langword="null"/>
    /// for one -- leaving <see cref="RewriteNativeCacheItemSelection"/> a permanent no-op for every table
    /// slicer and the native <c>&lt;i s="1"&gt;</c> flags Excel reads permanently stale after a selection
    /// change. Resolve the caption list from the referenced structured table's column distinct values
    /// instead through <see cref="StructuredTableCaptionResolver"/>, so the structural-row rules, scalar
    /// formatting, and first-occurrence index space patched here agree with the slicer's available items.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string?>? ResolveRawSharedItemCaptions(ZipArchive archive, Workbook workbook, SlicerModel slicer)
    {
        if (slicer.SourceTableId is { } tableId &&
            slicer.SourceTableColumnId is { } columnId &&
            StructuredTableCaptionResolver.TryResolveColumnCaptions(workbook, tableId, columnId, out var tableCaptions))
        {
            return tableCaptions;
        }

        var fieldName = slicer.SourceFieldName;
        if (string.IsNullOrWhiteSpace(fieldName))
            return null;

        // R58-io-slicer-timeline-6-1: resolve the SPECIFIC pivot cache this slicer is bound to via its
        // SourcePivotTableName -> PivotTableModel.CacheId -> PivotCacheModel, before falling back to a
        // name-only scan across every cache. Two independent pivot tables can each carry a field with the
        // same name (e.g. "Region") but different shared-item lists; scanning workbook.PivotCaches in
        // collection order and returning the first name match silently picks the wrong cache's caption
        // list whenever the slicer's bound cache isn't first, corrupting the selection on save.
        var boundCache = XlsxPivotSlicerCacheData.ResolveSlicerBoundPivotCache(workbook, slicer.SourcePivotTableName);
        if (boundCache is not null)
        {
            var captions = TryResolveSharedItemCaptions(archive, boundCache, fieldName);
            if (captions is not null)
                return captions;
        }

        foreach (var cache in workbook.PivotCaches)
        {
            if (ReferenceEquals(cache, boundCache))
                continue;

            var captions = TryResolveSharedItemCaptions(archive, cache, fieldName);
            if (captions is not null)
                return captions;
        }

        return null;
    }

    /// <summary>
    /// Reads the raw per-index caption list for <paramref name="fieldName"/> from a single, specific
    /// <see cref="PivotCacheModel"/>'s package part, or <see langword="null"/> if that cache has no such
    /// field/shared items or its part can't be loaded.
    /// </summary>
    private static IReadOnlyList<string?>? TryResolveSharedItemCaptions(ZipArchive archive, PivotCacheModel cache, string fieldName)
    {
        var field = cache.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
            candidate.SharedItems is { Count: > 0 });
        if (field is null || string.IsNullOrEmpty(cache.PackagePart))
            return null;

        var cacheEntry = archive.GetEntry(XlsxPackagePath.NormalizePackagePath(cache.PackagePart));
        if (cacheEntry is null)
            return null;

        var cacheDefinitionXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
        var cacheFieldElement = cacheDefinitionXml.Root?
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "cacheFields", StringComparison.OrdinalIgnoreCase))?
            .Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "cacheField", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("name")?.Value, fieldName, StringComparison.OrdinalIgnoreCase));

        var sharedItemsElement = cacheFieldElement?
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "sharedItems", StringComparison.OrdinalIgnoreCase));
        if (sharedItemsElement is null)
            return null;

        return sharedItemsElement
            .Elements()
            .Select(item => ResolveRawSharedItemCaption(item, field))
            .ToList();
    }

    /// <summary>Resolves a single raw <c>&lt;sharedItems&gt;</c> child's caption, or null when it has no
    /// <c>v</c> (e.g. <c>&lt;m/&gt;</c>) so it can never match a selection.</summary>
    private static string? ResolveRawSharedItemCaption(XElement item, PivotCacheFieldModel field)
    {
        var raw = item.Attribute("v")?.Value;
        if (string.IsNullOrEmpty(raw))
            return null;

        var kind = item.Name.LocalName.Length > 0 ? item.Name.LocalName[0] : (char?)null;
        return PivotSharedItemCaptionResolver.Resolve(raw, kind, field);
    }

    private static void RewriteTimelineState(ZipArchive archive, Workbook workbook)
    {
        if (workbook.Timelines.Count == 0)
            return;

        var timelinesByName = new Dictionary<string, TimelineModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var timeline in workbook.Timelines)
            timelinesByName.TryAdd(timeline.Name, timeline);

        // The timeline definition part carries level/selectionLevel/scrollPosition on <timeline>; the
        // timeline cache carries selectedStartDate/selectedEndDate. Patch both, matched by control name and
        // cache name respectively (mirroring the reader's associations).
        //
        // R49-io-slicer-timeline-3-3: keyed by TIMELINE name (mirroring RewriteSlicerSelections'
        // cacheNamesBySlicerName), not by cache name -- Excel's "linked timelines" (the same timeline
        // widget copied to another sheet) share one timelineCache, so MULTIPLE timeline names can map to
        // the same cache here and every one of them must stay reachable below, not just the first one
        // encountered.
        var cacheNameByTimelineName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var timelineEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelines/"))
                     .ToList())
        {
            var timelinePath = XlsxPackagePath.NormalizeEntryPath(timelineEntry);
            var timelineXml = XlsxPackageXmlEditor.LoadXml(timelineEntry);
            var changed = false;
            foreach (var timelineElement in EnumerateByLocalName(timelineXml.Root, "timeline"))
            {
                var name = timelineElement.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name) || !timelinesByName.TryGetValue(name, out var model))
                    continue;

                var cacheName = timelineElement.Attribute("cache")?.Value;
                if (!string.IsNullOrEmpty(cacheName))
                    cacheNameByTimelineName.TryAdd(name, cacheName);

                changed |= RewriteTimelineDefinition(timelineElement, model);
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, timelinePath, timelineXml);
        }

        foreach (var cacheEntry in archive.Entries
                     .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelineCaches/"))
                     .ToList())
        {
            var cachePath = XlsxPackagePath.NormalizeEntryPath(cacheEntry);
            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                continue;

            var cacheName = root.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(cacheName))
                continue;

            // Prefer whichever linked timeline's model actually carries a pending selection change over
            // the shared cache's current persisted state, so a user's date-range edit on one widget isn't
            // silently reverted by an untouched linked sibling that happens to be enumerated first; fall
            // back to the first match when none differs (idempotent re-save of an untouched shared cache
            // -- matches the previous behaviour exactly).
            TimelineModel? model = null;
            foreach (var pair in cacheNameByTimelineName)
            {
                if (string.Equals(pair.Value, cacheName, StringComparison.OrdinalIgnoreCase) &&
                    timelinesByName.TryGetValue(pair.Key, out var candidate))
                {
                    model ??= candidate;
                    if (TimelineCacheSelectionDiffers(root, candidate))
                    {
                        model = candidate;
                        break;
                    }
                }
            }

            if (model is null)
                continue;

            var changed = RewriteTimelineCacheSelection(root, model);
            changed |= RewriteCachePivotTableBinding(root, model.ConnectedPivotTableNames, model.SourcePivotTableName);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, cachePath, cacheXml);
        }
    }

    /// <summary>
    /// Non-mutating counterpart to <see cref="RewriteTimelineCacheSelection"/>: reports whether applying
    /// <paramref name="model"/>'s selected date range to <paramref name="cacheRoot"/> would actually change
    /// anything, using the exact same comparisons (root <c>selectedStartDate</c>/<c>selectedEndDate</c> and,
    /// when present, the native <c>&lt;state&gt;&lt;selection&gt;</c> form) without touching the XML. Used
    /// to pick, among several <see cref="TimelineModel"/> instances that share one timelineCache (linked
    /// timelines), the one whose selection actually differs from what is currently persisted -- i.e. the
    /// one carrying the user's live edit -- rather than an untouched linked sibling (R49-io-slicer-timeline-3-3).
    /// </summary>
    private static bool TimelineCacheSelectionDiffers(XElement cacheRoot, TimelineModel model)
    {
        var selectedStart = string.IsNullOrWhiteSpace(model.SelectedStartDate) ? null : model.SelectedStartDate;
        var selectedEnd = string.IsNullOrWhiteSpace(model.SelectedEndDate) ? null : model.SelectedEndDate;

        if (!AttributeMatches(cacheRoot, "selectedStartDate", selectedStart) ||
            !AttributeMatches(cacheRoot, "selectedEndDate", selectedEnd))
        {
            return true;
        }

        var selection = cacheRoot
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "selection", StringComparison.OrdinalIgnoreCase));
        if (selection is null)
            return false;

        if (selectedStart is null && selectedEnd is null)
            return true; // RewriteTimelineCacheSelection would remove this <selection> element.

        return !AttributeMatches(selection, "startDate", NormalizeSelectedDate(model.SelectedStartDate)) ||
               !AttributeMatches(selection, "endDate", NormalizeSelectedDate(model.SelectedEndDate));
    }

    /// <summary>
    /// Mirrors <see cref="SetOptionalAttribute"/>'s change test without mutating: true when
    /// <paramref name="element"/>'s <paramref name="attributeName"/> already equals <paramref name="value"/>
    /// (both null/absent counts as equal).
    /// </summary>
    private static bool AttributeMatches(XElement element, string attributeName, string? value)
    {
        var attribute = element.Attribute(attributeName);
        if (value is null)
            return attribute is null;

        return attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites ONLY the <c>level</c>/<c>selectionLevel</c>/<c>scrollPosition</c> attributes on the
    /// preserved <c>&lt;timeline&gt;</c> element to match the model, mirroring the reader. Every other
    /// attribute (name, cache, caption, style) is untouched. An attribute is only removed when the model
    /// value is null AND the attribute was present, and only added when the model value is set — so a
    /// timeline whose model carries no level/scroll state leaves the preserved part byte-stable.
    /// </summary>
    private static bool RewriteTimelineDefinition(XElement timelineElement, TimelineModel model)
    {
        var changed = false;
        changed |= SetOptionalAttribute(
            timelineElement,
            "level",
            model.Level?.ToString(CultureInfo.InvariantCulture));
        changed |= SetOptionalAttribute(
            timelineElement,
            "selectionLevel",
            (model.SelectionLevel ?? model.Level)?.ToString(CultureInfo.InvariantCulture));
        changed |= SetOptionalAttribute(
            timelineElement,
            "scrollPosition",
            string.IsNullOrEmpty(model.ScrollPosition) ? null : model.ScrollPosition + "T00:00:00");
        return changed;
    }

    /// <summary>
    /// Rewrites ONLY the selected date range on the preserved timeline cache to match the model, mirroring
    /// exactly what the reader parses: the root <c>selectedStartDate</c>/<c>selectedEndDate</c> attributes
    /// (the fresh writer's form) and, when present, the <c>&lt;state&gt;&lt;selection&gt;</c>
    /// <c>startDate</c>/<c>endDate</c> attributes (Excel's native form). The available-range
    /// <c>startDate</c>/<c>endDate</c> and every other attribute/child are left untouched.
    /// </summary>
    private static bool RewriteTimelineCacheSelection(XElement cacheRoot, TimelineModel model)
    {
        var changed = false;

        // Root-attribute form (what XlsxSlicerTimelineWriter emits, a bare yyyy-MM-dd). Only add when the
        // model has a value; only remove when the model cleared a previously-present value. Emitting the
        // bare date keeps an unchanged re-save byte-identical to the fresh writer's output.
        var selectedStart = string.IsNullOrWhiteSpace(model.SelectedStartDate) ? null : model.SelectedStartDate;
        var selectedEnd = string.IsNullOrWhiteSpace(model.SelectedEndDate) ? null : model.SelectedEndDate;
        changed |= SetOptionalAttribute(cacheRoot, "selectedStartDate", selectedStart);
        changed |= SetOptionalAttribute(cacheRoot, "selectedEndDate", selectedEnd);

        // Native <state><selection> form: patch it in place when the preserved part uses it, so a real
        // Excel timeline round-trips too. Excel's selection dates carry a time component; emit the same
        // yyyy-MM-ddT00:00:00 shape here. Never create the element when it is absent (the root form covers
        // that case and matches the fresh writer).
        var selection = cacheRoot
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "selection", StringComparison.OrdinalIgnoreCase));
        if (selection is not null)
        {
            if (selectedStart is null && selectedEnd is null)
            {
                // R17-slicer-timeline-cache-3: a native <selection> is CT_TimelineRange, whose startDate/
                // endDate are REQUIRED. A cleared filter (both model dates null) must not leave a
                // <selection/> stub with neither attribute — that is schema-invalid and Excel repairs/drops
                // the whole timeline. Remove the element itself (not the parent <state>, which also carries
                // the untouched <bounds> available-range) so a cleared filter round-trips as "no selection"
                // instead of an invalid one.
                selection.Remove();
                changed = true;
            }
            else
            {
                changed |= SetOptionalAttribute(selection, "startDate", NormalizeSelectedDate(model.SelectedStartDate));
                changed |= SetOptionalAttribute(selection, "endDate", NormalizeSelectedDate(model.SelectedEndDate));
            }
        }

        return changed;
    }

    // The model stores selected dates normalized to yyyy-MM-dd; Excel's timeline dates carry a time
    // component (e.g. "2026-03-01T00:00:00"). Emit the same yyyy-MM-ddT00:00:00 shape the fresh writer's
    // available-range attributes use, so the reader's NormalizeTimelineDate parses back the model value.
    private static string? NormalizeSelectedDate(string? date) =>
        string.IsNullOrWhiteSpace(date) ? null : date + "T00:00:00";

    /// <summary>
    /// Sets <paramref name="attributeName"/> to <paramref name="value"/> when non-null, or removes it when
    /// null. Returns true only when the XML actually changed, so an unchanged value is a no-op.
    /// </summary>
    private static bool SetOptionalAttribute(XElement element, string attributeName, string? value)
    {
        var attribute = element.Attribute(attributeName);
        if (value is null)
        {
            if (attribute is null)
                return false;
            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static IEnumerable<XElement> EnumerateByLocalName(XElement? root, string localName)
    {
        if (root is null)
            yield break;

        if (string.Equals(root.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
        {
            yield return root;
            yield break;
        }

        foreach (var element in root.Elements())
        {
            if (string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                yield return element;
        }
    }
}
