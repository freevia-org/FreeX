namespace Freexcel.Core.Model;

/// <summary>
/// Represents a workbook containing one or more worksheets.
/// This is the top-level domain object.
/// </summary>
public sealed class Workbook
{
    private static readonly char[] InvalidSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];
    private readonly List<Sheet> _sheets = [];
    private readonly List<CellStyle> _styles = [CellStyle.Default];

    /// <summary>Unique identifier for this workbook instance.</summary>
    public WorkbookId Id { get; }

    /// <summary>File name or title of the workbook.</summary>
    public string Name { get; set; }

    /// <summary>All sheets in order.</summary>
    public IReadOnlyList<Sheet> Sheets => _sheets;

    /// <summary>Named ranges defined in this workbook (case-insensitive keys).</summary>
    public Dictionary<string, GridRange> NamedRanges { get; } =
        new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Define or replace a named range.</summary>
    public void DefineNamedRange(string name, GridRange range) => NamedRanges[name] = range;

    /// <summary>Remove a named range. Returns true if found and removed.</summary>
    public bool RemoveNamedRange(string name) => NamedRanges.Remove(name);

    /// <summary>Try to get a named range. Returns false if not found.</summary>
    public bool TryGetNamedRange(string name, out GridRange range) =>
        NamedRanges.TryGetValue(name, out range);

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
        return sheet;
    }

    /// <summary>Insert a sheet at a specific position.</summary>
    public Sheet InsertSheet(int index, string name)
    {
        EnsureCanUseSheetName(name);
        var sheet = new Sheet(SheetId.New(), name);
        _sheets.Insert(index, sheet);
        return sheet;
    }

    /// <summary>Reinsert an existing sheet instance at a specific position.</summary>
    public void InsertSheet(int index, Sheet sheet)
    {
        EnsureCanUseSheetName(sheet.Name, sheet.Id);
        _sheets.Insert(index, sheet);
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

    /// <summary>Remove a sheet by its ID. Returns true if found and removed.</summary>
    public bool RemoveSheet(SheetId sheetId)
    {
        var idx = _sheets.FindIndex(s => s.Id == sheetId);
        if (idx < 0) return false;
        _sheets.RemoveAt(idx);
        return true;
    }

    /// <summary>Get a sheet by ID, or null if not found.</summary>
    public Sheet? GetSheet(SheetId sheetId)
    {
        return _sheets.Find(s => s.Id == sheetId);
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
        for (int i = 0; i < _styles.Count; i++)
        {
            if (_styles[i].Equals(style))
                return new StyleId(i);
        }
        _styles.Add(style.Clone());
        return new StyleId(_styles.Count - 1);
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
