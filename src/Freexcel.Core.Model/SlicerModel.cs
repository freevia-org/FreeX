namespace Freexcel.Core.Model;

public sealed class SlicerModel
{
    public string Name { get; init; } = "";
    public string CacheName { get; init; } = "";
    public string? SourcePivotTableName { get; init; }
    public string? SourceFieldName { get; init; }
    public List<string> SelectedItems { get; } = [];
    public string PackagePart { get; init; } = "";
}
