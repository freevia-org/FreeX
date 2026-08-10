using FreeX.Core.Model;
using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelineMetadataReader
{
    public static SlicerTimelinePackageMetadata Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive);
        }
        catch
        {
            return SlicerTimelinePackageMetadata.Empty;
        }
    }

    internal static SlicerTimelinePackageMetadata Load(ZipArchive archive)
    {
        var slicers = new List<SlicerModel>();
        var timelines = new List<TimelineModel>();
        try
        {
            var slicerCaches = archive.Entries
                .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicerCaches/"))
                .Select(entry => (Path: XlsxPackagePath.NormalizeEntryPath(entry), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadSlicerCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);
            // Excel does not declare a slicer/timeline relationship in the drawing's rels; the slicer drawing
            // lives in an mc:AlternateContent → mc:Choice → graphicFrame linked to the slicer BY NAME
            // (<sle:slicer name="..."/>). So anchors are keyed by control name, not by package part.
            var drawingMetadataByName = ReadDrawingMetadata(archive);

            foreach (var slicerEntry in archive.Entries.Where(entry =>
                         XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/slicers/")))
            {
                var slicerXml = LoadXml(slicerEntry);
                var packagePart = XlsxPackagePath.NormalizeEntryPath(slicerEntry);
                // A single xl/slicers/slicerN.xml part can declare multiple <slicer> elements
                // (e.g. file 03 carries both "Category" and "Who"). Read them all.
                foreach (var slicerElement in EnumerateChildren(slicerXml.Root, "slicer"))
                {
                    var name = slicerElement.Attribute("name")?.Value ?? "";
                    var cacheName = slicerElement.Attribute("cache")?.Value ?? "";
                    slicerCaches.TryGetValue(cacheName, out var cache);
                    var hasDrawing = drawingMetadataByName.TryGetValue(name, out var drawingMetadata);
                    var slicer = new SlicerModel
                    {
                        Name = name,
                        Caption = slicerElement.Attribute("caption")?.Value,
                        CacheName = cacheName,
                        SourcePivotTableName = cache?.PivotTableName,
                        ConnectedPivotTableNames = (cache?.PivotTableNames ?? []).ToList(),
                        SourceFieldName = cache?.SourceFieldName,
                        StyleName = slicerElement.Attribute("style")?.Value,
                        ColumnCount = ParseColumnCount(slicerElement.Attribute("columnCount")?.Value),
                        ShowCaption = ParseBool(slicerElement.Attribute("showCaption")?.Value, defaultValue: true),
                        PackagePart = packagePart,
                        DrawingAnchor = hasDrawing ? drawingMetadata.Anchor : null,
                        DrawingShapeName = hasDrawing ? drawingMetadata.ShapeName : null,
                        SourceSheetName = hasDrawing ? drawingMetadata.SheetName : null,
                        SourceTableId = cache?.TableId,
                        SourceTableColumnId = cache?.TableColumnId,
                        CacheItems = (cache?.CacheItems ?? []).ToList()
                    };
                    slicer.SelectedItems.AddRange(cache?.SelectedItems ?? []);
                    slicers.Add(slicer);
                }
            }

            var timelineCaches = archive.Entries
                .Where(entry => XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelineCaches/"))
                .Select(entry => (Path: XlsxPackagePath.NormalizeEntryPath(entry), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadTimelineCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);

            foreach (var timelineEntry in archive.Entries.Where(entry =>
                         XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/timelines/")))
            {
                var timelineXml = LoadXml(timelineEntry);
                var timelineElement = RootOrFirstChild(timelineXml.Root, "timeline");
                var cacheName = timelineElement?.Attribute("cache")?.Value ?? "";
                timelineCaches.TryGetValue(cacheName, out var cache);
                var packagePart = XlsxPackagePath.NormalizeEntryPath(timelineEntry);
                var timelineName = timelineElement?.Attribute("name")?.Value ?? "";
                var hasDrawing = drawingMetadataByName.TryGetValue(timelineName, out var drawingMetadata);
                var levelAttr = timelineElement?.Attribute("level")?.Value;
                int? level = TryReadInt(levelAttr, out var levelVal) ? levelVal : null;
                var selectionLevelAttr = timelineElement?.Attribute("selectionLevel")?.Value;
                int? selectionLevel = TryReadInt(selectionLevelAttr, out var selLvlVal) ? selLvlVal : null;
                var scrollPositionRaw = timelineElement?.Attribute("scrollPosition")?.Value;
                timelines.Add(new TimelineModel
                {
                    Name = timelineName,
                    Caption = timelineElement?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    ConnectedPivotTableNames = (cache?.PivotTableNames ?? []).ToList(),
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = timelineElement?.Attribute("style")?.Value,
                    StartDate = cache?.StartDate,
                    EndDate = cache?.EndDate,
                    SelectedStartDate = cache?.SelectedStartDate,
                    SelectedEndDate = cache?.SelectedEndDate,
                    PackagePart = packagePart,
                    DrawingAnchor = hasDrawing ? drawingMetadata.Anchor : null,
                    DrawingShapeName = hasDrawing ? drawingMetadata.ShapeName : null,
                    SourceSheetName = hasDrawing ? drawingMetadata.SheetName : null,
                    Level = level,
                    SelectionLevel = selectionLevel,
                    ScrollPosition = NormalizeTimelineDate(scrollPositionRaw)
                });
            }
        }
        catch
        {
            // Slicer/timeline metadata should never block loading ordinary workbook content.
        }

        return new SlicerTimelinePackageMetadata(slicers, timelines);
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    private static XElement? RootOrFirstChild(XElement? root, string localName)
    {
        if (root is null)
            return null;

        if (HasLocalName(root, localName))
            return root;

        return FirstChildByLocalName(root, localName);
    }

    private static XElement? FirstChildByLocalName(XElement root, string localName)
    {
        foreach (var element in root.Elements())
        {
            if (HasLocalName(element, localName))
                return element;
        }

        return null;
    }

    private static XElement? FirstDescendantByLocalName(XElement? root, string localName)
    {
        if (root is null)
            return null;

        foreach (var element in root.Descendants())
        {
            if (HasLocalName(element, localName))
                return element;
        }

        return null;
    }

    private static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string? ReadPivotTableName(XElement? root) =>
        FirstDescendantByLocalName(root, "pivotTable")?.Attribute("name")?.Value;

    /// <summary>
    /// R133-io-slicer-timeline-multipivot: reads EVERY <c>&lt;pivotTable name=".."/&gt;</c> a
    /// slicerCache/timelineCache's <c>&lt;pivotTables&gt;</c> list carries, in document order -- Excel
    /// allows one slicer/timeline to drive several pivot tables at once ("Report Connections"), and
    /// <see cref="ReadPivotTableName"/> (used for <see cref="SlicerModel.SourcePivotTableName"/>/
    /// <see cref="TimelineModel.SourcePivotTableName"/>) only ever captures the FIRST one. Feeds
    /// <see cref="SlicerModel.ConnectedPivotTableNames"/>/<see cref="TimelineModel.ConnectedPivotTableNames"/>
    /// so the save-side rewriter can preserve every connection instead of collapsing them onto the single
    /// primary name.
    /// </summary>
    private static IReadOnlyList<string> ReadPivotTableNames(XElement? root)
    {
        if (root is null)
            return [];

        return root.Descendants()
            .Where(element => HasLocalName(element, "pivotTable"))
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
    }

    private static SlicerCacheMetadata ReadSlicerCache(XDocument xml)
    {
        var root = xml.Root;
        var tableSlicerCache = FirstDescendantByLocalName(root, "tableSlicerCache");
        int? tableId = TryReadInt(tableSlicerCache?.Attribute("tableId")?.Value, out var tid) ? tid : null;
        int? tableColumn = TryReadInt(tableSlicerCache?.Attribute("column")?.Value, out var tcol) ? tcol : null;

        return new SlicerCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            ReadPivotTableName(root),
            root?.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "selectedItem", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute("value")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList() ?? [],
            tableId,
            tableColumn,
            ReadSlicerCacheItems(root),
            ReadPivotTableNames(root));
    }

    // Pivot slicers carry their available items as <data><tabular><items><i x="N" s="1"/>...> — the
    // x is the 0-based index into the pivot cache field's shared items and s="1" marks a selected item.
    private static IReadOnlyList<SlicerCacheItem> ReadSlicerCacheItems(XElement? root)
    {
        var itemsElement = FirstDescendantByLocalName(root, "items");
        if (itemsElement is null)
            return [];

        var items = new List<SlicerCacheItem>();
        foreach (var item in itemsElement.Elements())
        {
            if (!HasLocalName(item, "i"))
                continue;
            if (!TryReadInt(item.Attribute("x")?.Value, out var index))
                continue;
            var selected = string.Equals(item.Attribute("s")?.Value, "1", StringComparison.Ordinal);
            items.Add(new SlicerCacheItem(index, selected));
        }

        return items;
    }

    private static TimelineCacheMetadata ReadTimelineCache(XDocument xml)
    {
        var root = xml.Root;
        var state = FirstDescendantByLocalName(root, "state");
        var selection = FirstDescendantByLocalName(state, "selection");
        var bounds = FirstDescendantByLocalName(state, "bounds");
        return new TimelineCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            ReadPivotTableName(root),
            NormalizeTimelineDate(root?.Attribute("startDate")?.Value ?? bounds?.Attribute("startDate")?.Value),
            NormalizeTimelineDate(root?.Attribute("endDate")?.Value ?? bounds?.Attribute("endDate")?.Value),
            NormalizeTimelineDate(root?.Attribute("selectedStartDate")?.Value ?? selection?.Attribute("startDate")?.Value),
            NormalizeTimelineDate(root?.Attribute("selectedEndDate")?.Value ?? selection?.Attribute("endDate")?.Value),
            ReadPivotTableNames(root));
    }

    /// <summary>
    /// Resolves the drawing anchor + host-sheet name for every slicer/timeline in the package, keyed by the
    /// control's NAME. Excel emits the slicer drawing inside an mc:AlternateContent → mc:Choice →
    /// graphicFrame whose graphicData links to the slicer by name (<c>&lt;sle:slicer name="..."/&gt;</c>),
    /// with empty/absent drawing rels — so name matching (not a slicer relationship, not part index) is the
    /// only reliable association. The mc:Fallback (a placeholder "not supported" shape) is deliberately
    /// ignored so it is never read as the real anchor.
    /// </summary>
    private static IReadOnlyDictionary<string, DrawingControlMetadata> ReadDrawingMetadata(ZipArchive archive)
    {
        var result = new Dictionary<string, DrawingControlMetadata>(StringComparer.OrdinalIgnoreCase);
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var sheetNamesByDrawingPath = BuildSheetNamesByDrawingPath(archive);

        foreach (var drawingEntry in archive.Entries.Where(entry =>
                     XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/drawings/")))
        {
            var drawingPath = XlsxPackagePath.NormalizeEntryPath(drawingEntry);
            sheetNamesByDrawingPath.TryGetValue(drawingPath, out var sheetName);
            var drawingXml = LoadXml(drawingEntry);

            foreach (var anchor in drawingXml.Descendants(spreadsheetDrawingNs + "twoCellAnchor"))
            {
                var metadata = ReadSlicerTimelineAnchor(anchor, spreadsheetDrawingNs, sheetName);
                if (metadata is null || string.IsNullOrEmpty(metadata.Value.Name))
                    continue;

                // First writer wins per name (defensive against duplicate names across drawings).
                result.TryAdd(metadata.Value.Name, metadata.Value.Metadata);
            }
        }

        return result;
    }

    /// <summary>
    /// If <paramref name="anchor"/> hosts a slicer/timeline graphicFrame (inside the mc:Choice of an
    /// mc:AlternateContent), returns its name + anchor metadata. Returns null for ordinary drawings and for
    /// the mc:Fallback placeholder.
    /// </summary>
    private static (string Name, DrawingControlMetadata Metadata)? ReadSlicerTimelineAnchor(
        XElement anchor,
        XNamespace spreadsheetDrawingNs,
        string? sheetName)
    {
        var from = ReadAnchorPoint(anchor.Element(spreadsheetDrawingNs + "from"), spreadsheetDrawingNs);
        var to = ReadAnchorPoint(anchor.Element(spreadsheetDrawingNs + "to"), spreadsheetDrawingNs);
        if (from is null || to is null)
            return null;

        // Only consider the mc:Choice branch (the real slicer/timeline); never the mc:Fallback placeholder.
        var choice = FindAlternateContentChoice(anchor);
        var searchRoot = choice ?? anchor;
        var controlName = ReadSlicerTimelineLinkName(searchRoot);
        if (controlName is null)
            return null;

        // Prefer the graphicFrame's cNvPr name (the on-sheet shape name) for display; fall back to the link.
        var shapeName = ReadFirstShapeName(searchRoot, spreadsheetDrawingNs) ?? controlName;
        var metadata = new DrawingControlMetadata(new DrawingAnchorRange(from, to), shapeName, sheetName);
        return (controlName, metadata);
    }

    // The slicer/timeline link is <sle:slicer name="..."/> (drawing/2010 slicer),
    // <tle:timeline name="..."/>, or Excel's DrawingML 2012 <tsle:timeslicer name="..."/>.
    // Match by local name to stay namespace-tolerant.
    private static string? ReadSlicerTimelineLinkName(XElement root)
    {
        foreach (var element in root.Descendants())
        {
            if ((HasLocalName(element, "slicer") ||
                 HasLocalName(element, "timeline") ||
                 HasLocalName(element, "timeslicer")) &&
                element.Attribute("name")?.Value is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
    }

    private static XElement? FindAlternateContentChoice(XElement anchor)
    {
        foreach (var alternateContent in anchor.Elements())
        {
            if (!HasLocalName(alternateContent, "AlternateContent"))
                continue;
            foreach (var child in alternateContent.Elements())
            {
                if (HasLocalName(child, "Choice"))
                    return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps each drawing part path (e.g. <c>xl/drawings/drawing3.xml</c>) to the display name of the
    /// worksheet that references it, by walking workbook.xml (name + r:id) → workbook.xml.rels (r:id →
    /// worksheet part) → worksheets/_rels/sheetN.xml.rels (drawing r:id → drawing part).
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildSheetNamesByDrawingPath(ZipArchive archive)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || workbookRelsEntry is null)
            return result;

        // r:id -> worksheet part path
        var sheetTargetByRelId = LoadXml(workbookRelsEntry)
            .Root?
            .Elements(packageRelNs + "Relationship")
            .Where(r => (r.Attribute("Type")?.Value ?? "").Contains("worksheet", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                r => r.Attribute("Id")?.Value ?? "",
                r => NormalizePartPath("xl/workbook.xml", r.Attribute("Target")?.Value ?? ""),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var sheet in EnumerateDescendantsByLocalName(LoadXml(workbookEntry).Root, "sheet"))
        {
            var name = sheet.Attribute("name")?.Value;
            var relId = sheet.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId) ||
                !sheetTargetByRelId.TryGetValue(relId, out var sheetPath))
            {
                continue;
            }

            var drawingPath = ResolveSheetDrawingPath(archive, sheetPath);
            if (drawingPath is not null)
                result[drawingPath] = name;
        }

        return result;
    }

    private static string? ResolveSheetDrawingPath(ZipArchive archive, string sheetPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(sheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var rel in LoadXml(relsEntry).Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!(rel.Attribute("Type")?.Value ?? "").Contains("drawing", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = rel.Attribute("Target")?.Value ?? "";
            // Skip non-drawing "drawing"-typed rels defensively (e.g. ctrlProp); only xl/drawings/* parts.
            var resolved = NormalizePartPath(sheetPath, target);
            if (resolved.Contains("/drawings/", StringComparison.OrdinalIgnoreCase))
                return resolved;
        }

        return null;
    }

    private static string? ReadFirstShapeName(XElement root, XNamespace spreadsheetDrawingNs)
    {
        foreach (var element in root.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            if (element.Attribute("name")?.Value is { Length: > 0 } name)
                return name;
        }

        return null;
    }

    private static DrawingAnchorPoint? ReadAnchorPoint(XElement? point, XNamespace spreadsheetDrawingNs)
    {
        if (point is null ||
            !TryReadUInt(point.Element(spreadsheetDrawingNs + "col")?.Value, out var column) ||
            !TryReadUInt(point.Element(spreadsheetDrawingNs + "row")?.Value, out var row))
        {
            return null;
        }

        _ = long.TryParse(point.Element(spreadsheetDrawingNs + "colOff")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columnOffset);
        _ = long.TryParse(point.Element(spreadsheetDrawingNs + "rowOff")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowOffset);
        return new DrawingAnchorPoint(column, columnOffset, row, rowOffset);
    }

    private static bool TryReadUInt(string? text, out uint value) =>
        uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryReadInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static int ParseColumnCount(string? text) =>
        TryReadInt(text, out var value) && value > 0 ? value : 1;

    private static string? NormalizeTimelineDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return text.Trim();
    }

    // OOXML boolean attribute: "0"/"false" => false, "1"/"true" => true, absent/unknown => default.
    private static bool ParseBool(string? text, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        return text.Trim() switch
        {
            "0" => false,
            "1" => true,
            var t when string.Equals(t, "false", StringComparison.OrdinalIgnoreCase) => false,
            var t when string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) => true,
            _ => defaultValue,
        };
    }

    private static IEnumerable<XElement> EnumerateChildren(XElement? root, string localName)
    {
        if (root is null)
            yield break;

        // The slicer/timeline root may itself be the element, or its children may be the elements.
        if (HasLocalName(root, localName))
        {
            yield return root;
            yield break;
        }

        foreach (var element in root.Elements())
        {
            if (HasLocalName(element, localName))
                yield return element;
        }
    }

    private static IEnumerable<XElement> EnumerateDescendantsByLocalName(XElement? root, string localName)
    {
        if (root is null)
            yield break;

        foreach (var element in root.Descendants())
        {
            if (HasLocalName(element, localName))
                yield return element;
        }
    }

    private static string NormalizePartPath(string sourcePart, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "";

        var sourceDirectory = sourcePart.Contains('/', StringComparison.Ordinal)
            ? sourcePart[..(sourcePart.LastIndexOf('/') + 1)]
            : "";
        var combined = target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : sourceDirectory + target;
        var parts = new List<string>();
        foreach (var part in combined.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join("/", parts);
    }
}

internal sealed record SlicerTimelinePackageMetadata(
    IReadOnlyList<SlicerModel> Slicers,
    IReadOnlyList<TimelineModel> Timelines)
{
    public static SlicerTimelinePackageMetadata Empty { get; } = new([], []);
}

internal sealed record SlicerCacheMetadata(
    string Name,
    string? SourceFieldName,
    string? PivotTableName,
    IReadOnlyList<string> SelectedItems,
    int? TableId,
    int? TableColumnId,
    IReadOnlyList<SlicerCacheItem> CacheItems,
    IReadOnlyList<string> PivotTableNames);

internal sealed record TimelineCacheMetadata(
    string Name,
    string? SourceFieldName,
    string? PivotTableName,
    string? StartDate,
    string? EndDate,
    string? SelectedStartDate,
    string? SelectedEndDate,
    IReadOnlyList<string> PivotTableNames);

internal readonly record struct DrawingControlMetadata(
    DrawingAnchorRange Anchor,
    string? ShapeName,
    string? SheetName);
