namespace Freexcel.Core.Model;

public sealed class SlicerModel
{
    public string Name { get; init; } = "";
    public string? Caption { get; init; }
    public string CacheName { get; init; } = "";
    public string? SourcePivotTableName { get; init; }
    public string? SourceFieldName { get; init; }
    public string? StyleName { get; init; }
    public List<string> SelectedItems { get; } = [];
    public string PackagePart { get; init; } = "";
    public DrawingAnchorRange? DrawingAnchor { get; init; }
    public string? DrawingShapeName { get; init; }
}

public sealed record DrawingAnchorPoint(
    uint Column,
    long ColumnOffsetEmu,
    uint Row,
    long RowOffsetEmu);

public sealed record DrawingAnchorRange(
    DrawingAnchorPoint From,
    DrawingAnchorPoint To);
