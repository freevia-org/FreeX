namespace Freexcel.Core.Model;

public sealed class PivotCacheModel
{
    public int CacheId { get; init; }
    public PivotCacheSourceType SourceType { get; init; } = PivotCacheSourceType.Unknown;
    public string? SourceSheetName { get; set; }
    public string? SourceReference { get; set; }
    public string? SourceTableName { get; set; }
    public int? ConnectionId { get; set; }
    public bool IsOlap { get; set; }
    public string PackagePart { get; init; } = "";
    public bool RefreshOnLoad { get; set; } = true;
    public bool SaveData { get; set; } = true;
    public bool EnableRefresh { get; set; } = true;
    public bool PreserveSourceSortFilter { get; set; } = true;
    public int? MissingItemsLimit { get; set; }
    public int? RefreshedVersion { get; set; }
    public string? RefreshedBy { get; set; }
    public List<PivotCacheFieldModel> Fields { get; } = [];
}

public enum PivotCacheSourceType
{
    Unknown,
    WorksheetRange,
    Table,
    External,
    Consolidation,
    Scenario
}

public sealed record PivotCacheFieldModel(
    string Name,
    int? NumberFormatId = null,
    int? SharedItemCount = null,
    bool ContainsBlank = false,
    bool ContainsString = false,
    bool ContainsNumber = false,
    bool ContainsDate = false,
    bool ContainsMixedTypes = false,
    bool ContainsSemiMixedTypes = false,
    bool ContainsNonDate = false,
    bool ContainsInteger = false,
    bool ContainsLongText = false,
    double? MinValue = null,
    double? MaxValue = null,
    string? MinDate = null,
    string? MaxDate = null,
    IReadOnlyList<string>? SharedItems = null);

public sealed class PivotTableModel
{
    private bool _showRowGrandTotals = true;
    private bool _showColumnGrandTotals = true;

    public string Name { get; init; } = "";
    public int CacheId { get; init; }
    public GridRange SourceRange { get; set; }
    public GridRange TargetRange { get; init; }
    public string PackagePart { get; init; } = "";
    public bool ShowSubtotals { get; set; }
    public PivotSubtotalPlacement SubtotalPlacement { get; set; } = PivotSubtotalPlacement.Bottom;
    public bool ShowGrandTotals
    {
        get => _showRowGrandTotals || _showColumnGrandTotals;
        set
        {
            _showRowGrandTotals = value;
            _showColumnGrandTotals = value;
        }
    }
    public bool ShowRowGrandTotals
    {
        get => _showRowGrandTotals;
        set => _showRowGrandTotals = value;
    }
    public bool ShowColumnGrandTotals
    {
        get => _showColumnGrandTotals;
        set => _showColumnGrandTotals = value;
    }
    public bool RepeatItemLabels { get; set; } = true;
    public bool BlankLineAfterItems { get; set; }
    public PivotReportLayout ReportLayout { get; set; } = PivotReportLayout.Tabular;
    public int CompactRowLabelIndent { get; set; } = 1;
    public string StyleName { get; set; } = "PivotStyleLight16";
    public bool ShowRowHeaders { get; set; } = true;
    public bool ShowColumnHeaders { get; set; } = true;
    public bool ShowRowStripes { get; set; }
    public bool ShowColumnStripes { get; set; }
    public bool ShowFieldHeaders { get; set; } = true;
    public bool ShowContextualTooltips { get; set; } = true;
    public bool ShowPropertiesInTooltips { get; set; } = true;
    public bool ShowClassicLayout { get; set; }
    public bool MergeAndCenterLabels { get; set; }
    public bool ShowItemsWithNoDataOnRows { get; set; }
    public bool ShowItemsWithNoDataOnColumns { get; set; }
    public bool PageOverThenDown { get; set; }
    public int PageWrap { get; set; }
    public string? EmptyValueText { get; set; }
    public bool AutofitColumnsOnUpdate { get; set; } = true;
    public bool PreserveFormattingOnUpdate { get; set; } = true;
    public bool ShowExpandCollapseButtons { get; set; } = true;
    public bool EnableDrill { get; set; } = true;
    public bool AsteriskTotals { get; set; }
    public bool MultipleFieldFilters { get; set; } = true;
    public bool EnableFieldDialog { get; set; } = true;
    public bool EnableFieldProperties { get; set; } = true;
    public bool EnableDataValueEditing { get; set; }
    public bool PrintTitles { get; set; }
    public bool PrintExpandCollapseButtons { get; set; }
    public string? AltTextTitle { get; set; }
    public string? AltTextDescription { get; set; }
    public string? DataCaption { get; set; }
    public string? GrandTotalCaption { get; set; }
    public string? MissingCaption { get; set; }
    public string? ErrorCaption { get; set; }
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

public enum PivotSubtotalPlacement
{
    Bottom,
    Top
}

public enum PivotReportLayout
{
    Compact,
    Outline,
    Tabular
}

public sealed record PivotDataFieldModel(
    int SourceFieldIndex,
    string Name,
    string SummaryFunction,
    int? NumberFormatId = null,
    string? CalculatedFieldName = null,
    PivotShowValuesAs ShowValuesAs = PivotShowValuesAs.None,
    int? BaseFieldIndex = null,
    string? BaseItem = null,
    string? NumberFormatCode = null)
{
    public PivotDataFieldModel(
        int SourceFieldIndex,
        string Name,
        string SummaryFunction,
        int? NumberFormatId,
        string? CalculatedFieldName,
        PivotShowValuesAs ShowValuesAs,
        int? BaseFieldIndex,
        string? BaseItem)
        : this(SourceFieldIndex, Name, SummaryFunction, NumberFormatId, CalculatedFieldName, ShowValuesAs, BaseFieldIndex, BaseItem, null)
    {
    }
}

public enum PivotShowValuesAs
{
    None,
    PercentOfGrandTotal,
    PercentOfRowTotal,
    PercentOfColumnTotal,
    RunningTotalIn,
    DifferenceFrom,
    PercentDifferenceFrom,
    RankSmallest,
    RankLargest,
    Index,
    PercentOfParentRowTotal,
    PercentOfParentColumnTotal,
    PercentOfParentTotal
}

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
    string Value,
    string? Value2 = null);

public enum PivotLabelFilterKind
{
    Equals,
    DoesNotEqual,
    BeginsWith,
    EndsWith,
    Contains,
    DoesNotContain,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between
}

public sealed record PivotValueFilterModel(
    int DataFieldIndex,
    PivotValueFilterKind Kind,
    int Count = 0,
    double? ComparisonValue = null,
    double? ComparisonValue2 = null,
    int? SourceFieldIndex = null);

public enum PivotValueFilterKind
{
    Top,
    Bottom,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equals,
    DoesNotEqual,
    Between,
    NotBetween,
    AboveAverage,
    BelowAverage
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
