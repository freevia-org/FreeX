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
    public bool ShowSubtotals { get; set; }
    public List<PivotFieldModel> RowFields { get; } = [];
    public List<PivotFieldModel> ColumnFields { get; } = [];
    public List<PivotFieldModel> PageFields { get; } = [];
    public List<PivotDataFieldModel> DataFields { get; } = [];
    public List<PivotCalculatedFieldModel> CalculatedFields { get; } = [];
    public List<PivotCalculatedItemModel> CalculatedItems { get; } = [];
    public List<PivotLabelFilterModel> LabelFilters { get; } = [];
    public List<PivotValueFilterModel> ValueFilters { get; } = [];
    public List<PivotSortModel> Sorts { get; } = [];
}

public sealed record PivotFieldModel(
    int SourceFieldIndex,
    string? SelectedItem = null,
    IReadOnlyList<string>? SelectedItems = null,
    PivotFieldGrouping Grouping = PivotFieldGrouping.None,
    double? GroupStart = null,
    double? GroupEnd = null,
    double? GroupInterval = null);

public enum PivotFieldGrouping
{
    None,
    Year,
    Quarter,
    Month,
    Day,
    NumberRange
}

public sealed record PivotDataFieldModel(
    int SourceFieldIndex,
    string Name,
    string SummaryFunction,
    int? NumberFormatId = null,
    string? CalculatedFieldName = null);

public sealed record PivotCalculatedFieldModel(
    string Name,
    string Formula);

public sealed record PivotCalculatedItemModel(
    int SourceFieldIndex,
    string Name,
    string Formula);

public sealed record PivotLabelFilterModel(
    int SourceFieldIndex,
    PivotLabelFilterKind Kind,
    string Value);

public enum PivotLabelFilterKind
{
    Equals,
    DoesNotEqual,
    BeginsWith,
    EndsWith,
    Contains,
    DoesNotContain
}

public sealed record PivotValueFilterModel(
    int DataFieldIndex,
    PivotValueFilterKind Kind,
    int Count = 0,
    double? ComparisonValue = null);

public enum PivotValueFilterKind
{
    Top,
    Bottom,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equals,
    DoesNotEqual
}

public sealed record PivotSortModel(
    PivotSortTarget Target,
    PivotSortDirection Direction,
    int DataFieldIndex = 0,
    int FieldIndex = 0);

public enum PivotSortTarget
{
    Label,
    Value
}

public enum PivotSortDirection
{
    Ascending,
    Descending
}
