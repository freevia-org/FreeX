namespace Freexcel.Core.Model;

public sealed class TimelineModel
{
    public string Name { get; init; } = "";
    public string? Caption { get; init; }
    public string CacheName { get; init; } = "";
    public string? SourcePivotTableName { get; init; }
    public string? SourceFieldName { get; init; }
    public string? StyleName { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? SelectedStartDate { get; set; }
    public string? SelectedEndDate { get; set; }
    public string PackagePart { get; init; } = "";
    public DrawingAnchorRange? DrawingAnchor { get; init; }
    public string? DrawingShapeName { get; init; }
}
