using Freexcel.Core.Model;
using System.IO.Compression;
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

            foreach (var slicerEntry in archive.Entries.Where(entry =>
                         entry.FullName.Replace('\\', '/').StartsWith("xl/slicers/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                var slicerXml = LoadXml(slicerEntry);
                var root = slicerXml.Root;
                var cacheName = root?.Attribute("cache")?.Value ?? "";
                slicerCaches.TryGetValue(cacheName, out var cache);
                slicers.Add(new SlicerModel
                {
                    Name = root?.Attribute("name")?.Value ?? "",
                    Caption = root?.Attribute("caption")?.Value,
                    CacheName = cacheName,
                    SourcePivotTableName = cache?.PivotTableName,
                    SourceFieldName = cache?.SourceFieldName,
                    StyleName = root?.Attribute("style")?.Value,
                    PackagePart = slicerEntry.FullName.Replace('\\', '/')
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
                    PackagePart = timelineEntry.FullName.Replace('\\', '/')
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
