namespace Freexcel.Core.Model;

/// <summary>Structured Excel table metadata loaded from XLSX packages.</summary>
public sealed class StructuredTableModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public GridRange Range { get; init; }
    public bool HasAutoFilter { get; init; }
    public bool TotalsRowShown { get; init; }
    public int? HeaderRowCount { get; init; }
    public int? TotalsRowCount { get; init; }
    public bool? InsertRow { get; init; }
    public bool? InsertRowShift { get; init; }
    public bool? Published { get; init; }
    public string? Comment { get; init; }
    public string? StyleName { get; init; }
    public bool ShowFirstColumn { get; init; }
    public bool ShowLastColumn { get; init; }
    public bool ShowRowStripes { get; init; }
    public bool ShowColumnStripes { get; init; }
    public string PackagePart { get; init; } = "";
    public string? NativeSortStateXml { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }
    public IReadOnlyList<string>? NativeChildXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAutoFilterAttributes { get; init; }
    public IReadOnlyList<string>? NativeAutoFilterChildXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeStyleInfoAttributes { get; init; }
    public IReadOnlyList<string>? NativeStyleInfoChildXmls { get; init; }
    public List<StructuredTableColumnModel> Columns { get; } = [];
    public List<StructuredTableFilterColumnModel> FilterColumns { get; } = [];
}

public sealed record StructuredTableColumnModel(
    int Id,
    string Name,
    string? TotalsRowLabel = null,
    string? TotalsRowFunction = null,
    string? CalculatedColumnFormula = null,
    string? TotalsRowFormula = null,
    IReadOnlyList<string>? NativeChildXmls = null,
    IReadOnlyDictionary<string, string>? NativeAttributes = null);

public sealed record StructuredTableFilterColumnModel
{
    public int ColumnId { get; init; }
    public IReadOnlyList<string> Values { get; init; }
    public bool IncludeBlank { get; init; }
    public IReadOnlyList<string> NativeFilterXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }
    public string? NativeFilterXml => NativeFilterXmls.FirstOrDefault();

    public StructuredTableFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank = false,
        string? NativeFilterXml = null)
        : this(
            ColumnId,
            Values,
            IncludeBlank,
            string.IsNullOrWhiteSpace(NativeFilterXml) ? [] : [NativeFilterXml],
            null)
    {
    }

    public StructuredTableFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<string> NativeFilterXmls,
        IReadOnlyDictionary<string, string>? NativeAttributes = null)
    {
        this.ColumnId = ColumnId;
        this.Values = Values;
        this.IncludeBlank = IncludeBlank;
        this.NativeFilterXmls = NativeFilterXmls;
        this.NativeAttributes = NativeAttributes;
    }
}
