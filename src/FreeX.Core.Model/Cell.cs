namespace FreeX.Core.Model;

/// <summary>
/// How a formula that produces a multi-cell result behaves in a scalar context.
/// </summary>
public enum FormulaArrayMode
{
    /// <summary>Modern dynamic array: the result spills. Default for authored/edited formulas.</summary>
    Dynamic,

    /// <summary>
    /// Legacy implicit intersection: a range used in a scalar context resolves to the single cell that
    /// intersects the formula's own row/column (Excel's implicit <c>@</c>). Set by the loader for plain
    /// (non-array) formulas read from a workbook.
    /// </summary>
    Implicit,
}

/// <summary>
/// Allocation-free snapshot of every field preserved by <see cref="Cell.Clone"/>. Keeping this
/// state in Core.Model gives command snapshots the same copy contract without allocating a
/// temporary <see cref="Cell"/> for every occupied cell they capture.
/// </summary>
internal readonly record struct CellCopyState(
    ScalarValue Value,
    string? FormulaText,
    object? CachedAst,
    bool IgnoreFormulaError,
    StyleId StyleId,
    bool QuotePrefix,
    FormulaArrayMode ArrayMode,
    uint LegacyArrayRows,
    uint LegacyArrayCols);

/// <summary>
/// Represents a single cell in a worksheet.
/// A cell holds an optional formula string and a computed/entered value.
/// </summary>
public sealed class Cell
{
    /// <summary>The computed or directly-entered value of the cell.</summary>
    public ScalarValue Value { get; set; } = BlankValue.Instance;

    /// <summary>
    /// The formula text (without leading '='), or null if this cell has a literal value.
    /// Setting this property automatically clears <see cref="CachedAst"/>.
    /// </summary>
    private string? _formulaText;
    public string? FormulaText
    {
        get => _formulaText;
        // Assigning formula text means the cell was authored/edited, which is a modern (Dynamic) formula.
        // The loader marks legacy formulas Implicit explicitly after constructing the cell.
        set
        {
            _formulaText = value;
            CachedAst = null;
            ArrayMode = FormulaArrayMode.Dynamic;
            // A freshly authored/edited formula is never a legacy fixed-extent CSE array formula
            // even if this cell previously held one (see LegacyArrayRows).
            LegacyArrayRows = 0;
            LegacyArrayCols = 0;
        }
    }

    /// <summary>
    /// Whether a multi-cell formula result spills (<see cref="FormulaArrayMode.Dynamic"/>) or implicitly
    /// intersects to a scalar (<see cref="FormulaArrayMode.Implicit"/>). Defaults to Dynamic.
    /// </summary>
    public FormulaArrayMode ArrayMode { get; set; } = FormulaArrayMode.Dynamic;

    /// <summary>
    /// For a legacy multi-cell CSE array formula (Ctrl+Shift+Enter; ECMA-376 <c>&lt;f t="array"
    /// ref="..."/&gt;</c>), the row/column extent of the ref range as originally declared/entered.
    /// Zero (the default) means "not a fixed-extent legacy array formula" -- an ordinary dynamic-array
    /// formula (<see cref="ArrayMode"/> Dynamic) free-spills to whatever size its result naturally is.
    /// When non-zero, the recalc engine confines the formula's result to exactly this many rows/cols
    /// instead of letting it spill/negotiate with neighboring cells: Excel's legacy CSE semantics never
    /// grow or shrink the originally selected range, silently dropping extra result values and showing
    /// #N/A in any declared cell the natural result doesn't reach. Set by the file loader; reset to 0
    /// whenever <see cref="FormulaText"/> is reassigned (a fresh edit is always a modern formula).
    /// </summary>
    public uint LegacyArrayRows { get; set; }

    /// <summary>See <see cref="LegacyArrayRows"/>.</summary>
    public uint LegacyArrayCols { get; set; }

    /// <summary>Whether this cell contains a formula.</summary>
    public bool HasFormula => FormulaText is not null;

    /// <summary>
    /// Cached parsed AST for this cell's formula (stored as <see cref="object?"/> to avoid
    /// a project-reference from Core.Model to Core.Formula). The calc engine casts it to
    /// <c>FormulaNode</c> before use. Cleared automatically when <see cref="FormulaText"/> changes.
    /// </summary>
    public object? CachedAst { get; set; }

    /// <summary>Whether formula error checking should skip this cell.</summary>
    public bool IgnoreFormulaError { get; set; }

    /// <summary>The style applied to this cell.</summary>
    public StyleId StyleId { get; set; } = StyleId.Default;

    /// <summary>
    /// Whether this cell was forced to text via a leading apostrophe (ECMA-376 cellXfs
    /// <c>xf@quotePrefix</c>). A cell with this set stores its value as text even though it looks
    /// numeric, shows Excel's "Number Stored as Text" indicator, and replays the leading apostrophe
    /// in the formula bar when re-edited. Defaults to false (no quote prefix).
    /// </summary>
    public bool QuotePrefix { get; set; }

    /// <summary>Creates a cell with a literal value (no formula).</summary>
    public static Cell FromValue(ScalarValue value) => new() { Value = value };

    /// <summary>Creates a cell with a formula. The value will be computed by the calc engine.</summary>
    public static Cell FromFormula(string formulaText) => new() { FormulaText = formulaText };

    /// <summary>Captures every field preserved by <see cref="Clone"/> without allocating.</summary>
    internal CellCopyState CaptureCopyState() =>
        new(
            Value,
            _formulaText,
            CachedAst,
            IgnoreFormulaError,
            StyleId,
            QuotePrefix,
            ArrayMode,
            LegacyArrayRows,
            LegacyArrayCols);

    /// <summary>Creates a cell from the canonical copy-state representation.</summary>
    internal static Cell FromCopyState(CellCopyState state)
    {
        var copy = new Cell
        {
            Value = state.Value,
            CachedAst = state.CachedAst,
            IgnoreFormulaError = state.IgnoreFormulaError,
            StyleId = state.StyleId,
            QuotePrefix = state.QuotePrefix,
            ArrayMode = state.ArrayMode,
            LegacyArrayRows = state.LegacyArrayRows,
            LegacyArrayCols = state.LegacyArrayCols
        };
        copy._formulaText = state.FormulaText;
        return copy;
    }

    /// <summary>Creates a deep copy of this cell. Preserves the cached AST to avoid re-parsing.</summary>
    public Cell Clone() => FromCopyState(CaptureCopyState());
}
