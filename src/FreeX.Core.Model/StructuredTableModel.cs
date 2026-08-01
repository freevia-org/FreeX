namespace FreeX.Core.Model;

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

    /// <summary>
    /// Sets (or clears) <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/> for the
    /// column identified by <paramref name="columnId"/>, replacing the immutable record in
    /// <see cref="Columns"/> in place. This is the model-layer plumbing that lets edit-time
    /// detection of a calculated column (the editing/command layer noticing the same formula was
    /// entered across a table column's data rows, the way Excel auto-fills calculated columns)
    /// persist the formula so <c>ResizeStructuredTableCommand.FillGrownCalculatedColumns</c> can
    /// propagate it into newly added rows. Native XLSX loads that already populate
    /// <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/> from
    /// <c>&lt;calculatedColumnFormula&gt;</c> continue to flow through the reader and never need
    /// this method. Returns true if the column was found and updated.
    /// </summary>
    public bool SetCalculatedColumnFormula(int columnId, string? formula, bool isArrayFormula = false)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Id != columnId)
                continue;

            Columns[i] = Columns[i] with
            {
                CalculatedColumnFormula = formula,
                IsCalculatedColumnFormulaArray = !string.IsNullOrWhiteSpace(formula) && isArrayFormula
            };
            return true;
        }

        return false;
    }
}

public sealed record StructuredTableColumnModel(
    int Id,
    string Name,
    string? TotalsRowLabel = null,
    string? TotalsRowFunction = null,
    string? CalculatedColumnFormula = null,
    string? TotalsRowFormula = null,
    IReadOnlyList<string>? NativeChildXmls = null,
    IReadOnlyDictionary<string, string>? NativeAttributes = null,
    bool IsCalculatedColumnFormulaArray = false,
    bool IsTotalsRowFormulaArray = false);

public sealed record StructuredTableFilterColumnModel
{
    public int ColumnId { get; init; }
    public IReadOnlyList<string> Values { get; init; }
    public bool IncludeBlank { get; init; }
    public IReadOnlyList<StructuredTableCustomFilterModel> CustomFilters { get; init; }
    public bool CustomFiltersAnd { get; init; }
    public string? CustomFiltersAndRaw { get; init; }
    public IReadOnlyDictionary<string, string>? NativeCustomFiltersAttributes { get; init; }
    public IReadOnlyList<string> NativeFilterXmls { get; init; }
    public IReadOnlyDictionary<string, string>? NativeAttributes { get; init; }
    public string? NativeFilterXml => NativeFilterXmls.Count == 0 ? null : NativeFilterXmls[0];

    // R107-commands-autofilter-table-color-sync-1: mirrors WorksheetAutoFilterColumnModel.ColorFilter
    // -- a table has no dxfId to give a fresh Filter-by-Cell/Font-Colour criterion until
    // XlsxAutoFilterColorFilterDxfWriter allocates one at save time (see its StructuredTable overload),
    // so this stays a first-class (not NativeFilterXmls-passthrough) field the writer resolves then,
    // unlike Top10/custom-criterion which need no dxf and can be built as raw XML eagerly (see
    // TopBottomFilterCommand.BuildTop10Xml).
    // R111-io-structured-table-colorfilter-roundtrip-1: XlsxStructuredTableMetadataReader now also
    // populates this from a loaded file's <colorFilter> element (mirroring
    // XlsxWorksheetAutoFilterXmlMapper.ReadColorFilter) and XlsxStructuredTableNativeMetadataReader
    // .ReadFilterXmls excludes "colorFilter" from the NativeFilterXmls passthrough it used to fall
    // back to -- a loaded colorFilter used to vanish on the very next save because the writer already
    // excludes "colorFilter" from that same passthrough (see XlsxStructuredTableWriter.ToFilterColumnXml)
    // while nothing set this typed field, so neither path ever emitted it. Now there is exactly one
    // producer (this field) and exactly one consumer (the writer's ColorFilter branch), so a
    // round-tripped colorFilter is never dropped nor emitted twice.
    public WorksheetAutoFilterColorFilterModel? ColorFilter { get; init; }

    // R111-io-structured-table-dategroup-roundtrip-1: mirrors WorksheetAutoFilterColumnModel.DateGroups
    // -- Excel's built-in Year/Quarter/Month/Day checklist filter on a date column writes a Table's
    // <filters> element with ONLY <dateGroupItem> children (no plain <filter val=.../> children at
    // all). Before this field existed, XlsxStructuredTableMetadataReader.ReadFilterColumns had nowhere
    // to put those dateGroupItem children -- it only ever read <filter> into Values -- so a
    // date-grouped filterColumn had Values.Count==0, and (since ReadFilterXmls already excludes
    // "filters" from the NativeFilterXmls passthrough the same way it excludes "customFilters") no
    // NativeFilterXmls fallback either. It failed every disjunct of the inclusion guard and the whole
    // filterColumn -- and the user's date-filter criterion -- vanished on load, before a save could
    // even run. This is the single typed home for those dateGroupItem children, exactly like
    // WorksheetAutoFilterColumnModel.DateGroups holds them for the sheet-level AutoFilter path.
    public IReadOnlyList<WorksheetAutoFilterDateGroupItemModel> DateGroups { get; init; } = [];

    public StructuredTableFilterColumnModel(
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
        : this(
            ColumnId,
            Values,
            IncludeBlank,
            [],
            false,
            null,
            null,
            NativeFilterXmls,
            NativeAttributes)
    {
    }

    public StructuredTableFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<StructuredTableCustomFilterModel> CustomFilters,
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
            NativeFilterXmls,
            NativeAttributes)
    {
    }

    public StructuredTableFilterColumnModel(
        int ColumnId,
        IReadOnlyList<string> Values,
        bool IncludeBlank,
        IReadOnlyList<StructuredTableCustomFilterModel> CustomFilters,
        bool CustomFiltersAnd,
        string? CustomFiltersAndRaw,
        IReadOnlyDictionary<string, string>? NativeCustomFiltersAttributes,
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
        this.NativeFilterXmls = NativeFilterXmls;
        this.NativeAttributes = NativeAttributes;
    }
}

public sealed record StructuredTableCustomFilterModel(
    string? Operator,
    string? Value,
    IReadOnlyDictionary<string, string>? NativeAttributes = null);
