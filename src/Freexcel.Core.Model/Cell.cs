namespace Freexcel.Core.Model;

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
        set { _formulaText = value; CachedAst = null; }
    }

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

    /// <summary>Creates a cell with a literal value (no formula).</summary>
    public static Cell FromValue(ScalarValue value) => new() { Value = value };

    /// <summary>Creates a cell with a formula. The value will be computed by the calc engine.</summary>
    public static Cell FromFormula(string formulaText) => new() { FormulaText = formulaText };

    /// <summary>Creates a deep copy of this cell.</summary>
    public Cell Clone() => new()
    {
        Value = Value,
        FormulaText = FormulaText,
        IgnoreFormulaError = IgnoreFormulaError,
        StyleId = StyleId
    };
}
