namespace Freexcel.Core.Model;

/// <summary>
/// Represents a workbook containing one or more worksheets.
/// This is the top-level domain object.
/// </summary>
public sealed class Workbook
{
    private static readonly char[] InvalidSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];
    private readonly List<Sheet> _sheets = [];
    private readonly Dictionary<SheetId, Sheet> _sheetById = [];
    private readonly List<CellStyle> _styles = [CellStyle.Default];
    private readonly Dictionary<CellStyle, int> _styleIndex = new() { [CellStyle.Default] = 0 };

    /// <summary>Unique identifier for this workbook instance.</summary>
    public WorkbookId Id { get; }

    /// <summary>File name or title of the workbook.</summary>
    public string Name { get; set; }

    /// <summary>All sheets in order.</summary>
    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Named ranges defined in this workbook (case-insensitive keys).</summary>
    public Dictionary<string, GridRange> NamedRanges { get; } =
        new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Excel-style metadata for named ranges, keyed by defined name.</summary>
    public Dictionary<string, NamedRangeMetadata> NamedRangeMetadataByName { get; } =
        new Dictionary<string, NamedRangeMetadata>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pivot cache metadata loaded from XLSX packages.</summary>
    public List<PivotCacheModel> PivotCaches { get; } = [];

    /// <summary>Slicer metadata loaded from XLSX packages.</summary>
    public List<SlicerModel> Slicers { get; } = [];

    /// <summary>Timeline metadata loaded from XLSX packages.</summary>
    public List<TimelineModel> Timelines { get; } = [];

    /// <summary>External workbook link metadata loaded from XLSX packages.</summary>
    public List<ExternalLinkModel> ExternalLinks { get; } = [];

    /// <summary>Custom PivotTable style metadata loaded from XLSX stylesheet tableStyle definitions.</summary>
    public List<PivotTableStyleModel> PivotTableStyles { get; } = [];

    /// <summary>Workbook number-format catalog entries keyed by XLSX numFmtId.</summary>
    public Dictionary<int, string> NumberFormatCatalog { get; } = [];

    /// <summary>Saved workbook view snapshots, similar to Excel Custom Views.</summary>
    public List<WorkbookCustomView> CustomViews { get; } = [];

    /// <summary>Saved What-If Analysis scenarios.</summary>
    public List<WorkbookScenario> Scenarios { get; } = [];

    /// <summary>Cells tracked in the formulas Watch Window.</summary>
    public List<CellAddress> WatchedCells { get; } = [];

    /// <summary>Formula error codes disabled in Error Checking options.</summary>
    public HashSet<string> DisabledFormulaErrorCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Workbook calculation mode.</summary>
    public WorkbookCalculationMode CalculationMode { get; set; } = WorkbookCalculationMode.Automatic;

    /// <summary>Whether workbook date serials use Excel's 1904 date system.</summary>
    public bool Uses1904DateSystem { get; set; }

    /// <summary>Whether Excel should fully recalculate the workbook when it is opened.</summary>
    public bool FullCalculationOnLoad { get; set; }

    /// <summary>Whether Excel should force a full calculation pass even if dependencies appear clean.</summary>
    public bool ForceFullCalculation { get; set; }

    /// <summary>Whether iterative calculation is enabled for circular formulas.</summary>
    public bool IterativeCalculation { get; set; }

    /// <summary>Maximum iterative-calculation passes. Null means Excel/default.</summary>
    public int? MaxCalculationIterations { get; set; }

    /// <summary>Maximum iterative-calculation change threshold. Null means Excel/default.</summary>
    public double? MaxCalculationChange { get; set; }

    /// <summary>Workbook-level theme definition for Excel-style theme colors, fonts, and effects.</summary>
    public WorkbookTheme Theme { get; set; } = WorkbookTheme.Office;

    /// <summary>Last requested workbook-window arrangement.</summary>
    public WorkbookWindowArrangement WindowArrangement { get; set; } = WorkbookWindowArrangement.Tiled;

    /// <summary>True when workbook structure operations such as sheet add/delete/rename/move are protected.</summary>
    public bool IsStructureProtected { get; set; }

    /// <summary>Password hash/text for workbook structure protection. Null means no password required.</summary>
    public string? StructureProtectionPassword { get; set; }

    /// <summary>Define or replace a named range.</summary>
    public void DefineNamedRange(string name, GridRange range)
    {
        DefineNamedRange(name, range, null);
    }

    /// <summary>Define or replace a named range and its Excel-style metadata.</summary>
    public void DefineNamedRange(string name, GridRange range, NamedRangeMetadata? metadata)
    {
        var error = ValidateNamedRangeName(name);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));

        NamedRanges[name] = range;
        NamedRangeMetadataByName[name] = metadata ?? NamedRangeMetadata.WorkbookScope;
    }

    /// <summary>Remove a named range. Returns true if found and removed.</summary>
    public bool RemoveNamedRange(string name)
    {
        NamedRangeMetadataByName.Remove(name);
        return NamedRanges.Remove(name);
    }

    /// <summary>Try to get a named range. Returns false if not found.</summary>
    public bool TryGetNamedRange(string name, out GridRange range) =>
        NamedRanges.TryGetValue(name, out range);

    /// <summary>Try to get Excel-style metadata for a named range.</summary>
    public bool TryGetNamedRangeMetadata(string name, out NamedRangeMetadata metadata) =>
        NamedRangeMetadataByName.TryGetValue(name, out metadata!);

    public Workbook(string name = "Untitled")
    {
        Id = WorkbookId.New();
        Name = name;
    }

    /// <summary>Add a new sheet with the given name. Returns the new sheet.</summary>
    public Sheet AddSheet(string name)
    {
        EnsureCanUseSheetName(name);
        var sheet = new Sheet(SheetId.New(), name);
        _sheets.Add(sheet);
        _sheetById[sheet.Id] = sheet;
        return sheet;
    }

    /// <summary>Insert a sheet at a specific position.</summary>
    public Sheet InsertSheet(int index, string name)
    {
        EnsureCanUseSheetName(name);
        var sheet = new Sheet(SheetId.New(), name);
        _sheets.Insert(index, sheet);
        _sheetById[sheet.Id] = sheet;
        return sheet;
    }

    /// <summary>Reinsert an existing sheet instance at a specific position.</summary>
    public void InsertSheet(int index, Sheet sheet)
    {
        EnsureCanUseSheetName(sheet.Name, sheet.Id);
        _sheets.Insert(index, sheet);
        _sheetById[sheet.Id] = sheet;
    }

    /// <summary>Return an Excel-compatible validation error for a sheet name, or null when valid.</summary>
    public string? ValidateSheetName(string name, SheetId? exceptSheetId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Sheet name is invalid: it cannot be blank.";

        if (name.Length > 31)
            return "Sheet name is invalid: it cannot exceed 31 characters.";

        if (name.IndexOfAny(InvalidSheetNameChars) >= 0)
            return "Sheet name is invalid: it cannot contain : \\ / ? * [ or ].";

        if (_sheets.Any(s => s.Id != exceptSheetId &&
                             string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"A sheet named '{name}' already exists.";

        return null;
    }

    private void EnsureCanUseSheetName(string name, SheetId? exceptSheetId = null)
    {
        var error = ValidateSheetName(name, exceptSheetId);
        if (error is not null)
            throw new ArgumentException(error, nameof(name));
    }

    /// <summary>Return an Excel-compatible validation error for a named range name, or null when valid.</summary>
    public string? ValidateNamedRangeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Named range name is invalid: it cannot be blank.";

        if (name.Length > 255)
            return "Named range name is invalid: it cannot exceed 255 characters.";

        if (!IsValidNamedRangeStart(name[0]) || name.Any(ch => !IsValidNamedRangeChar(ch)))
            return "Named range name is invalid: use letters, numbers, underscores, and periods; start with a letter or underscore.";

        if (CellAddress.TryParse(name, SheetId.New(), out _) || IsR1C1Reference(name))
            return "Named range name is invalid: it cannot look like a cell reference.";

        return null;
    }

    private static bool IsValidNamedRangeStart(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsValidNamedRangeChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private static bool IsR1C1Reference(string name)
    {
        if (name.Length < 4 || char.ToUpperInvariant(name[0]) != 'R')
            return false;

        var cIndex = name.IndexOf("C", 1, StringComparison.OrdinalIgnoreCase);
        if (cIndex <= 1 || cIndex == name.Length - 1)
            return false;

        return uint.TryParse(name[1..cIndex], out var row) &&
               uint.TryParse(name[(cIndex + 1)..], out var col) &&
               row is >= 1 and <= CellAddress.MaxRow &&
               col is >= 1 and <= CellAddress.MaxCol;
    }

    /// <summary>Remove a sheet by its ID. Returns true if found and removed.</summary>
    public bool RemoveSheet(SheetId sheetId)
    {
        var idx = _sheets.FindIndex(s => s.Id == sheetId);
        if (idx < 0) return false;
        _sheets.RemoveAt(idx);
        _sheetById.Remove(sheetId);
        return true;
    }

    /// <summary>Get a sheet by ID, or null if not found.</summary>
    public Sheet? GetSheet(SheetId sheetId)
    {
        _sheetById.TryGetValue(sheetId, out var sheet);
        return sheet;
    }

    /// <summary>Get a sheet by name (case-insensitive), or null if not found.</summary>
    public Sheet? GetSheet(string name)
    {
        return _sheets.Find(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get a sheet by 0-based index.</summary>
    public Sheet GetSheetAt(int index) => _sheets[index];

    /// <summary>Number of sheets.</summary>
    public int SheetCount => _sheets.Count;

    /// <summary>
    /// Register a style. If a structurally identical style already exists, returns its <see cref="StyleId"/>.
    /// Otherwise appends the style and returns a new <see cref="StyleId"/>.
    /// </summary>
    public StyleId RegisterStyle(CellStyle style)
    {
        if (_styleIndex.TryGetValue(style, out var idx))
            return new StyleId(idx);

        var clone = style.Clone();
        var newIdx = _styles.Count;
        _styles.Add(clone);
        _styleIndex[clone] = newIdx;
        return new StyleId(newIdx);
    }

    /// <summary>
    /// Get a style by id. Returns the default style if <paramref name="id"/> is out of range.
    /// Returns a copy so callers cannot mutate registry state.
    /// </summary>
    public CellStyle GetStyle(StyleId id)
    {
        int idx = id.Value;
        return (idx >= 0 && idx < _styles.Count ? _styles[idx] : _styles[0]).Clone();
    }

    /// <summary>Total number of registered styles.</summary>
    public int StyleCount => _styles.Count;

    /// <summary>Reorder a sheet from one position to another.</summary>
    public void MoveSheet(int fromIndex, int toIndex)
    {
        var sheet = _sheets[fromIndex];
        _sheets.RemoveAt(fromIndex);
        _sheets.Insert(toIndex, sheet);
    }
}

public sealed record NamedRangeMetadata(string Scope, string Comment)
{
    public static NamedRangeMetadata WorkbookScope { get; } = new("Workbook", "");
}

public sealed record WorkbookCustomView(
    string Name,
    IReadOnlyList<WorksheetCustomViewState> Sheets,
    string? Id = null,
    bool IncludePrintSettings = true,
    bool IncludeHiddenRowsColumnsAndFilterSettings = true);

public sealed record WorkbookScenario(string Name, IReadOnlyList<ScenarioCellValue> ChangingCells, string? Comment = null);

public sealed record ScenarioCellValue(CellAddress Address, ScalarValue Value);

public sealed record WorksheetCustomViewState(
    string SheetName,
    WorksheetViewMode ViewMode,
    uint FrozenRows,
    uint FrozenCols,
    uint? SplitRow,
    uint? SplitColumn,
    bool ShowGridlines = true,
    bool ShowHeadings = true,
    bool ShowRulers = true,
    int ZoomPercent = 100,
    bool ShowFormulas = false);
