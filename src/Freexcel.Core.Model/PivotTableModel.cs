namespace Freexcel.Core.Model;

public sealed class PivotCacheModel
{
    public int CacheId { get; init; }
    public PivotCacheSourceType SourceType { get; init; } = PivotCacheSourceType.Unknown;
    public string? SourceSheetName { get; init; }
    public string? SourceReference { get; init; }
    public string? SourceTableName { get; init; }
    public string PackagePart { get; init; } = "";
    public List<PivotCacheFieldModel> Fields { get; } = [];
}

public enum PivotCacheSourceType
{
    Unknown,
    WorksheetRange,
    Table,
    External
}

public sealed record PivotCacheFieldModel(
    string Name,
    int? NumberFormatId = null);

public sealed class PivotTableModel
{
    public string Name { get; init; } = "";
    public int CacheId { get; init; }
    public GridRange SourceRange { get; init; }
    public GridRange TargetRange { get; init; }
    public string PackagePart { get; init; } = "";
    public List<PivotFieldModel> RowFields { get; } = [];
    public List<PivotFieldModel> ColumnFields { get; } = [];
    public List<PivotFieldModel> PageFields { get; } = [];
    public List<PivotDataFieldModel> DataFields { get; } = [];
}

public sealed record PivotFieldModel(
    int SourceFieldIndex);

public sealed record PivotDataFieldModel(
    int SourceFieldIndex,
    string Name,
    string SummaryFunction,
    int? NumberFormatId = null);
