namespace FreeX.Core.IO;

internal static class XlsxSlicerTimelineRelationshipTypes
{
    public const string SlicerRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/slicer";
    public const string SlicerCacheRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/slicerCache";
    public const string TimelineRelationshipType = "http://schemas.microsoft.com/office/2010/relationships/Timeline";
    public const string TimelineCacheRelationshipType = "http://schemas.microsoft.com/office/2010/relationships/TimelineCache";
    public const string TimelineRelationshipType2011 = "http://schemas.microsoft.com/office/2011/relationships/timeline";
    public const string TimelineCacheRelationshipType2011 = "http://schemas.microsoft.com/office/2011/relationships/timelineCache";

    public static bool IsTimeline(string? relationshipType) =>
        string.Equals(relationshipType, TimelineRelationshipType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relationshipType, TimelineRelationshipType2011, StringComparison.OrdinalIgnoreCase);

    public static bool IsTimelineCache(string? relationshipType) =>
        string.Equals(relationshipType, TimelineCacheRelationshipType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relationshipType, TimelineCacheRelationshipType2011, StringComparison.OrdinalIgnoreCase);
}
