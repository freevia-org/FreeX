using Freexcel.Core.Model;
using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxSlicerTimelineMetadataReader
{
    public static SlicerTimelinePackageMetadata Load(Stream xlsxStream)
    {
        var slicers = new List<SlicerModel>();
        var timelines = new List<TimelineModel>();
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var slicerCaches = archive.Entries
                .Where(entry => entry.FullName.Replace('\\', '/').StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Path: entry.FullName.Replace('\\', '/'), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadSlicerCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);
            var drawingMetadataByPart = ReadDrawingMetadata(archive);

            foreach (var slicerEntry in archive.Entries.Where(entry =>
                         entry.FullName.Replace('\\', '/').StartsWith("xl/slicers/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var slicerXml = LoadXml(slicerEntry);
                var root = slicerXml.Root;
                var cacheName = root?.Attribute("cache")?.Value ?? "";
                slicerCaches.TryGetValue(cacheName, out var cache);
                var packagePart = slicerEntry.FullName.Replace('\\', '/');
                drawingMetadataByPart.TryGetValue(packagePart, out var drawingMetadata);
                slicers.Add(new SlicerModel
                {
                    Name = root?.Attribute("name")?.Value ?? "",
                    Caption = root?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = root?.Attribute("style")?.Value,
                    PackagePart = packagePart,
                    DrawingAnchor = drawingMetadata?.Anchor,
                    DrawingShapeName = drawingMetadata?.ShapeName
                });
                slicers[^1].SelectedItems.AddRange(cache?.SelectedItems ?? []);
            }

            var timelineCaches = archive.Entries
                .Where(entry => entry.FullName.Replace('\\', '/').StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase))
                .Select(entry => (Path: entry.FullName.Replace('\\', '/'), Xml: LoadXml(entry)))
                .Select(item => (item.Path, Cache: ReadTimelineCache(item.Xml)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Cache.Name))
                .ToDictionary(item => item.Cache.Name, item => item.Cache, StringComparer.OrdinalIgnoreCase);

            foreach (var timelineEntry in archive.Entries.Where(entry =>
                         entry.FullName.Replace('\\', '/').StartsWith("xl/timelines/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var timelineXml = LoadXml(timelineEntry);
                var root = timelineXml.Root;
                var cacheName = root?.Attribute("cache")?.Value ?? "";
                timelineCaches.TryGetValue(cacheName, out var cache);
                var packagePart = timelineEntry.FullName.Replace('\\', '/');
                drawingMetadataByPart.TryGetValue(packagePart, out var drawingMetadata);
                timelines.Add(new TimelineModel
                {
                    Name = root?.Attribute("name")?.Value ?? "",
                    Caption = root?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = root?.Attribute("style")?.Value,
                    StartDate = cache?.StartDate,
                    EndDate = cache?.EndDate,
                    SelectedStartDate = cache?.SelectedStartDate,
                    SelectedEndDate = cache?.SelectedEndDate,
                    PackagePart = packagePart,
                    DrawingAnchor = drawingMetadata?.Anchor,
                    DrawingShapeName = drawingMetadata?.ShapeName
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
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static SlicerCacheMetadata ReadSlicerCache(XDocument xml)
    {
        var root = xml.Root;
        return new SlicerCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            root?.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "pivotTable", StringComparison.OrdinalIgnoreCase))?.Attribute("name")?.Value,
            root?.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "selectedItem", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute("value")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList() ?? []);
    }

    private static TimelineCacheMetadata ReadTimelineCache(XDocument xml)
    {
        var root = xml.Root;
        return new TimelineCacheMetadata(
            root?.Attribute("name")?.Value ?? "",
            root?.Attribute("sourceName")?.Value,
            root?.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "pivotTable", StringComparison.OrdinalIgnoreCase))?.Attribute("name")?.Value,
            root?.Attribute("startDate")?.Value,
            root?.Attribute("endDate")?.Value,
            root?.Attribute("selectedStartDate")?.Value,
            root?.Attribute("selectedEndDate")?.Value);
    }

    private static IReadOnlyDictionary<string, DrawingControlMetadata> ReadDrawingMetadata(ZipArchive archive)
    {
        var result = new Dictionary<string, DrawingControlMetadata>(StringComparer.OrdinalIgnoreCase);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        foreach (var drawingEntry in archive.Entries.Where(entry =>
                     entry.FullName.Replace('\\', '/').StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var drawingPath = drawingEntry.FullName.Replace('\\', '/');
            var relsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
            var relsEntry = archive.GetEntry(relsPath);
            if (relsEntry is null)
                continue;

            var relatedTargets = LoadXml(relsEntry)
                .Root?
                .Elements(packageRelNs + "Relationship")
                .Select(element => new
                {
                    Type = element.Attribute("Type")?.Value ?? "",
                    Target = element.Attribute("Target")?.Value ?? ""
                })
                .Where(rel => rel.Type.Contains("slicer", StringComparison.OrdinalIgnoreCase) ||
                              rel.Type.Contains("timeline", StringComparison.OrdinalIgnoreCase))
                .Select(rel => NormalizePartPath(drawingPath, rel.Target))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList() ?? [];
            if (relatedTargets.Count == 0)
                continue;

            var drawingXml = LoadXml(drawingEntry);
            var anchors = drawingXml
                .Descendants(spreadsheetDrawingNs + "twoCellAnchor")
                .Select(anchor => ReadTwoCellAnchor(anchor, spreadsheetDrawingNs))
                .Where(metadata => metadata is not null)
                .Select(metadata => metadata!)
                .ToList();

            for (var index = 0; index < anchors.Count && index < relatedTargets.Count; index++)
                result[relatedTargets[index]] = anchors[index];
        }

        return result;
    }

    private static DrawingControlMetadata? ReadTwoCellAnchor(XElement anchor, XNamespace spreadsheetDrawingNs)
    {
        var from = ReadAnchorPoint(anchor.Element(spreadsheetDrawingNs + "from"), spreadsheetDrawingNs);
        var to = ReadAnchorPoint(anchor.Element(spreadsheetDrawingNs + "to"), spreadsheetDrawingNs);
        if (from is null || to is null)
            return null;

        var shapeName = anchor
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .FirstOrDefault()
            ?.Attribute("name")
            ?.Value;
        return new DrawingControlMetadata(new DrawingAnchorRange(from, to), shapeName);
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
    IReadOnlyList<TimelineModel> Timelines);

internal sealed record SlicerCacheMetadata(
    string Name,
    string? SourceFieldName,
    string? PivotTableName,
    IReadOnlyList<string> SelectedItems);

internal sealed record TimelineCacheMetadata(
    string Name,
    string? SourceFieldName,
    string? PivotTableName,
    string? StartDate,
    string? EndDate,
    string? SelectedStartDate,
    string? SelectedEndDate);

internal sealed record DrawingControlMetadata(
    DrawingAnchorRange Anchor,
    string? ShapeName);
