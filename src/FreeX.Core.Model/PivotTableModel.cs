namespace FreeX.Core.Model;

public sealed class PivotCacheModel
{
    public int CacheId { get; init; }
    public PivotCacheSourceType SourceType { get; init; } = PivotCacheSourceType.Unknown;
    public string? SourceSheetName { get; set; }
    public string? SourceReference { get; set; }
    public string? SourceTableName { get; set; }
    /// <summary>
    /// R104: the stable <see cref="StructuredTableModel.Id"/> of the structured table this cache is
    /// bound to, established the first time a table-backed refresh resolves <see
    /// cref="SourceTableName"/> against a live table (mirrors <see cref="SlicerModel.SourceTableId"/>'s
    /// stable-identity pattern for the analogous slicer-to-table binding). Null until that first
    /// resolution (e.g. a cache freshly loaded from a file, where the OOXML/JSON source carries only the
    /// name). Once set, a table-backed refresh must re-resolve by THIS id rather than by name alone —
    /// otherwise "Convert to Range" on the original table followed by an unrelated table being renamed
    /// to reuse the freed name would silently re-bind this cache (and its pivot) to that unrelated
    /// table's data, since <see cref="SourceTableName"/> alone cannot distinguish "the same table,
    /// renamed" from "a different table that now happens to share the name".
    /// </summary>
    public int? SourceTableId { get; set; }
    public int? ConnectionId { get; set; }
    public bool IsOlap { get; set; }
    public string PackagePart { get; init; } = "";
    public bool RefreshOnLoad { get; set; }
    public bool SaveData { get; set; } = true;
    public bool EnableRefresh { get; set; } = true;
    public bool PreserveSourceSortFilter { get; set; } = true;
    public int? MissingItemsLimit { get; set; }
    public int? RecordCount { get; set; }
    public int? CreatedVersion { get; set; }
    public int? MinRefreshableVersion { get; set; }
    public int? RefreshedVersion { get; set; }
    public string? RefreshedBy { get; set; }
    public string? RefreshedDateIso { get; set; }
    /// <summary>
    /// Verbatim &lt;pivotCacheRecords&gt; XML captured from the original package, kept only for cache
    /// sources (External/Consolidation/Scenario) that have no live worksheet range the writer can
    /// re-derive records from. Preserved as passthrough so an offline-cached query/consolidation
    /// result is never silently truncated to an empty records part on re-save (data-loss risk).
    /// Null for ordinary worksheet/table caches, whose records are always regenerated live.
    /// </summary>
    public string? RawRecordsXml { get; set; }
    public List<PivotCacheFieldModel> Fields { get; } = [];
    /// <summary>
    /// R116-io-pivot-calcitem-part: Calculated Items (PivotTable Analyze &gt; Fields, Items &amp; Sets &gt;
    /// Calculated Item) belong to CT_PivotCacheDefinition (ECMA-376 18.10.1.3), not
    /// CT_pivotTableDefinition -- every PivotTableModel that shares this cache surfaces the SAME list
    /// (mirroring real Excel, where a calculated item is a cache-wide concept every pivot on that cache
    /// sees identically), loaded from this cache's own pivotCacheDefinitionN.xml part.
    /// </summary>
    public List<PivotCalculatedItemModel> CalculatedItems { get; } = [];
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
    IReadOnlyList<string>? SharedItems = null,
    /// <summary>
    /// Element kind for each shared item in <see cref="SharedItems"/> ('s', 'n', 'd', 'b', 'm').
    /// When present, the writer uses the original element kind instead of re-inferring from the value.
    /// Null means the writer should infer the kind (for items created fresh in FreeX).
    /// </summary>
    IReadOnlyList<char>? SharedItemKinds = null,
    string? Formula = null,
    bool IsDatabaseField = true,
    PivotFieldGrouping Grouping = PivotFieldGrouping.None,
    double? GroupStart = null,
    double? GroupEnd = null,
    double? GroupInterval = null,
    /// <summary>
    /// Raw ISO dateTime bounds from a date-type CT_RangePr (groupBy=years/quarters/months/days), e.g.
    /// "2024-03-01T00:00:00". Excel emits these instead of startNum/endNum for a date-grouped field; when
    /// present they take precedence over <see cref="GroupStart"/>/<see cref="GroupEnd"/> on save
    /// (R36-io-pivot-cache-2-2).
    /// </summary>
    string? GroupStartDate = null,
    string? GroupEndDate = null,
    /// <summary>
    /// The group's own label list from a native CT_GroupItems (ECMA-376 18.10.1.36), e.g. "Jan".."Dec"
    /// for a month-grouped date field, or the numeric-range bucket labels for a number-range grouping.
    /// The pivotTable definition's pivotField/items index into this list to render the grouped field's
    /// headers, so dropping it on save leaves those indexes pointing at nothing (R78-io-pivotcache-5-2).
    /// Null means the field carries no group (or the group's labels were never captured).
    /// </summary>
    IReadOnlyList<string>? GroupItems = null);

public sealed class PivotTableModel
{
    private bool _showRowGrandTotals = true;
    private bool _showColumnGrandTotals = true;

    public string Name { get; set; } = "";
    public int CacheId { get; init; }
    public GridRange SourceRange { get; set; }
    public GridRange TargetRange { get; set; }
    public GridRange? LastRenderedRange { get; set; }
    public string PackagePart { get; init; } = "";
    public int? CreatedVersion { get; set; }
    public int? UpdatedVersion { get; set; }
    public int? MinRefreshableVersion { get; set; }
    public bool DataOnRows { get; set; } = true;
    public int FirstHeaderRow { get; set; } = 1;
    public int FirstDataRow { get; set; } = 1;
    public int FirstDataColumn { get; set; } = 1;
    // R90-render-pivot-layout-5-1: Excel's own PivotTable defaults (and CT_pivotField's
    // defaultSubtotal/subtotalTop schema defaults, both true) are subtotals ON, placed at the TOP of
    // each group -- a freshly created pivot must match that, not render with subtotals off entirely.
    public bool ShowSubtotals { get; set; } = true;
    public PivotSubtotalPlacement SubtotalPlacement { get; set; } = PivotSubtotalPlacement.Top;
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
    // R90-render-pivot-layout-5-3: Excel's out-of-the-box default report layout is Compact Form (CT_
    // pivotTableDefinition's compact attribute defaults to true), not Tabular -- a freshly created
    // pivot must match that.
    public PivotReportLayout ReportLayout { get; set; } = PivotReportLayout.Compact;
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
    /// <summary>
    /// CT_pivotTableDefinition's fieldListSortAscending (default false): whether the PivotTable Field
    /// List panel sorts fields A-to-Z instead of in data-source order.
    /// </summary>
    public bool FieldListSortAscending { get; set; }
    public bool MergeAndCenterLabels { get; set; }
    public bool ShowItemsWithNoDataOnRows { get; set; }
    public bool ShowItemsWithNoDataOnColumns { get; set; }
    public bool PageOverThenDown { get; set; }
    public int PageWrap { get; set; }
    public string? EmptyValueText { get; set; }
    public bool ApplyNumberFormats { get; set; } = true;
    public bool ApplyBorderFormats { get; set; } = true;
    public bool ApplyFontFormats { get; set; } = true;
    public bool ApplyPatternFormats { get; set; } = true;
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
    double? GroupInterval = null,
    bool? ShowAll = null,
    bool? IncludeNewItemsInFilter = null,
    bool? MultipleItemSelectionAllowed = null,
    bool? DragToRow = null,
    bool? DragToColumn = null,
    bool? DragToPage = null,
    bool? DragToData = null,
    bool? ShowDropDowns = null,
    /// <summary>
    /// True for a <see cref="PivotTableModel.PageFields"/> entry that exists ONLY to carry a
    /// slicer/timeline's value filter for a field the user never dragged into the Filters area
    /// (see H10). Excel filters the pivot in that case without showing a Filters-area box for the
    /// field, so renderers must still honor this field in <c>MatchesFieldSelections</c> but must
    /// exclude it from the visible page-field row span / header writing.
    /// </summary>
    bool IsUnplacedFilterField = false,
    /// <summary>
    /// See <see cref="PivotCacheFieldModel.GroupStartDate"/>. Carried on this intermediate model only when
    /// used as the return value of a cache-field-group parse (R36-io-pivot-cache-2-2).
    /// </summary>
    string? GroupStartDate = null,
    string? GroupEndDate = null,
    /// <summary>
    /// See <see cref="PivotCacheFieldModel.GroupItems"/>. Carried on this intermediate model only when
    /// used as the return value of a cache-field-group parse (R78-io-pivotcache-5-2).
    /// </summary>
    IReadOnlyList<string>? GroupItems = null,
    /// <summary>
    /// R75-io-pivottable-layout-4-2: this field's own CT_PivotField "defaultSubtotal" setting (whether
    /// subtotals show for this specific row/column field), independent of any other axis field. Null means
    /// the file carried no per-field override; callers fall back to <see
    /// cref="PivotTableModel.ShowSubtotals"/> (the table-wide default previously the only place this was
    /// modeled).
    /// </summary>
    bool? ShowSubtotals = null,
    /// <summary>
    /// R75-io-pivottable-layout-4-2: this field's own CT_PivotField "subtotalTop" setting. Null means no
    /// per-field override; callers fall back to <see cref="PivotTableModel.SubtotalPlacement"/>.
    /// </summary>
    PivotSubtotalPlacement? SubtotalPlacement = null,
    /// <summary>
    /// R75-io-pivottable-layout-4-3: this field's own CT_PivotField compact/outline report form. Null means
    /// no per-field override (e.g. a non-axis field, which never carries these attributes); callers fall
    /// back to <see cref="PivotTableModel.ReportLayout"/>.
    /// </summary>
    PivotReportLayout? ReportLayout = null);

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
    Between,
    // Excel's Row/Column Label "Date Filters" submenu (ST_PivotFilterType date* and relative-period
    // tokens, R36-io-pivot-cache-2-3). DateEqual..DateNotBetween carry an explicit date Value/Value2;
    // the relative-period kinds below carry no value at all -- Excel computes the range dynamically
    // from the current date at filter-apply time.
    DateEqual,
    DateNotEqual,
    DateOlderThan,
    DateOlderThanOrEqual,
    DateNewerThan,
    DateNewerThanOrEqual,
    DateBetween,
    DateNotBetween,
    Yesterday,
    Today,
    Tomorrow,
    LastWeek,
    ThisWeek,
    NextWeek,
    LastMonth,
    ThisMonth,
    NextMonth,
    LastQuarter,
    ThisQuarter,
    NextQuarter,
    LastYear,
    ThisYear,
    NextYear,
    YearToDate
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
