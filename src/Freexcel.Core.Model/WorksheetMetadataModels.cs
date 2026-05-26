namespace Freexcel.Core.Model;

public sealed record WorksheetAutoFilterModel(string? Reference, string? NativeXml)
{
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }
    public IReadOnlyList<string>? NativeChildXmls { get; init; }
    public List<WorksheetAutoFilterColumnModel> FilterColumns { get; } = [];
}

public sealed record WorksheetAutoFilterColumnModel
{
    public int ColumnId { get; init; }
    public IReadOnlyList<string> Values { get; init; }
    public bool IncludeBlank { get; init; }
    public IReadOnlyList<WorksheetAutoFilterCustomFilterModel> CustomFilters { get; init; }
    public bool CustomFiltersAnd { get; init; }
    public string? CustomFiltersAndRaw { get; init; }
    public IReadOnlyDictionary<string, string>? NativeCustomFiltersAttributes { get; init; }
    public WorksheetAutoFilterTop10Model? Top10 { get; init; }
    public IReadOnlyList<string> NativeFilterXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }

    public WorksheetAutoFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank = false,
        string? NativeFilterXml = null)
        : this(
            ColumnId,
            Values,
            IncludeBlank,
            [],
            false,
            null,
            null,
            null,
            string.IsNullOrWhiteSpace(NativeFilterXml) ? [] : [NativeFilterXml],
            null)
    {
    }

    public WorksheetAutoFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyDictionary<string, string>? NativeAttributes = null)
        : this(
            ColumnId,
            Values,
            IncludeBlank,
            [],
            false,
            null,
            null,
            null,
            NativeFilterXmls,
            NativeAttributes)
    {
    }

    public WorksheetAutoFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel> CustomFilters,
        bool CustomFiltersAnd,
        IReadOnlyDictionary<string, string>? NativeCustomFiltersAttributes,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyDictionary<string, string>? NativeAttributes = null)
        : this(
            ColumnId,
            Values,
            IncludeBlank,
            CustomFilters,
            CustomFiltersAnd,
            null,
            NativeCustomFiltersAttributes,
            null,
            NativeFilterXmls,
            NativeAttributes)
    {
    }

    public WorksheetAutoFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<WorksheetAutoFilterCustomFilterModel> CustomFilters,
        bool CustomFiltersAnd,
        string? CustomFiltersAndRaw,
        IReadOnlyDictionary<string, string>? NativeCustomFiltersAttributes,
        WorksheetAutoFilterTop10Model? Top10,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyDictionary<string, string>? NativeAttributes = null)
    {
        this.ColumnId = ColumnId;
        this.Values = Values;
        this.IncludeBlank = IncludeBlank;
        this.CustomFilters = CustomFilters;
        this.CustomFiltersAnd = CustomFiltersAnd;
        this.CustomFiltersAndRaw = CustomFiltersAndRaw;
        this.NativeCustomFiltersAttributes = NativeCustomFiltersAttributes;
        this.Top10 = Top10;
        this.NativeFilterXmls = NativeFilterXmls;
        this.NativeAttributes = NativeAttributes;
    }
}

public sealed record WorksheetAutoFilterCustomFilterModel(
    string? Operator,
    string? Value,
    IReadOnlyDictionary<string, string>? NativeAttributes = null);

public sealed record WorksheetAutoFilterTop10Model(
    bool Top = true,
    bool Percent = false,
    double? Value = null,
    double? FilterValue = null,
    string? TopRaw = null,
    string? PercentRaw = null,
    string? ValueRaw = null,
    string? FilterValueRaw = null,
    IReadOnlyDictionary<string, string>? NativeAttributes = null);

public sealed class WorksheetProtectionMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPageSetupMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPrintOptionsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetSheetFormatMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetDimensionMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetSheetPropertiesMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPrimaryViewMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetPageBreaksMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<uint, Dictionary<string, string>> BreakNativeAttributes { get; set; } = [];
}

public sealed class WorksheetCellWatchesMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> WatchNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorksheetIgnoredErrorsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> ErrorNativeAttributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorksheetSingleXmlCellsModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetSingleXmlCellModel> Cells { get; set; } = [];
}

public sealed class WorksheetSingleXmlCellModel
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public int? XmlCellPropertyId { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetPageMarginsMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetHeaderFooterMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed class WorksheetSmartTagsModel
{
    public string? NativeXml { get; set; }
    public List<WorksheetCellSmartTagsModel> Cells { get; set; } = [];
}

public sealed class WorksheetCellSmartTagsModel
{
    public string? Reference { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetCellSmartTagModel> Tags { get; set; } = [];
}

public sealed class WorksheetCellSmartTagModel
{
    public string? Type { get; set; }
    public bool? Deleted { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetCellSmartTagPropertyModel> Properties { get; set; } = [];
}

public sealed class WorksheetCellSmartTagPropertyModel
{
    public string? Key { get; set; }
    public string? Value { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetDataConsolidationModel
{
    public string? Function { get; set; }
    public bool? LeftLabels { get; set; }
    public bool? TopLabels { get; set; }
    public bool? Link { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetDataConsolidationReferenceModel> References { get; set; } = [];
}

public sealed class WorksheetDataConsolidationReferenceModel
{
    public string? Reference { get; set; }
    public string? Sheet { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetSortStateModel
{
    public string? Reference { get; set; }
    public bool? ColumnSort { get; set; }
    public bool? CaseSensitive { get; set; }
    public string? SortMethod { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetSortConditionModel> Conditions { get; set; } = [];
}

public sealed class WorksheetSortConditionModel
{
    public string? Reference { get; set; }
    public bool? Descending { get; set; }
    public string? SortBy { get; set; }
    public string? CustomList { get; set; }
    public string? DxfId { get; set; }
    public string? IconSet { get; set; }
    public string? IconId { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetAdditionalViewsModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<WorksheetAdditionalViewModel> Views { get; set; } = [];
}

public sealed class WorksheetAdditionalViewModel
{
    public string? WorkbookViewId { get; set; }
    public string? NativeXml { get; set; }
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorksheetCustomPropertyMetadataModel
{
    public Dictionary<string, string> NativeAttributes { get; set; } = new(StringComparer.Ordinal);
    public List<string> NativeChildXmls { get; set; } = [];
}

public sealed record WorksheetCustomProperty(
    string Name,
    int Id,
    WorksheetCustomPropertyMetadataModel? Metadata = null);

public sealed record WorksheetPhoneticProperties(string? FontId, string? Type, string? Alignment);
