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
        set { _formulaText = value; CachedAst = null; ArrayMode = FormulaArrayMode.Dynamic; }
    }

    /// <summary>
    /// Whether a multi-cell formula result spills (<see cref="FormulaArrayMode.Dynamic"/>) or implicitly
    /// intersects to a scalar (<see cref="FormulaArrayMode.Implicit"/>). Defaults to Dynamic.
    /// </summary>
    public FormulaArrayMode ArrayMode { get; set; } = FormulaArrayMode.Dynamic;

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

    /// <summary>Creates a deep copy of this cell. Preserves the cached AST to avoid re-parsing.</summary>
    public Cell Clone()
    {
        var copy = new Cell
        {
            Value = Value,
            IgnoreFormulaError = IgnoreFormulaError,
            StyleId = StyleId,
            QuotePrefix = QuotePrefix
        };
        copy._formulaText = _formulaText;
        copy.CachedAst = CachedAst;
        copy.ArrayMode = ArrayMode;
        return copy;
    }
}
