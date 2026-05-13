namespace Freexcel.Core.Model;

/// <summary>
/// Represents a single cell in a worksheet.
/// A cell holds an optional formula string and a computed/entered value.
/// </summary>
public sealed class Cell
{
    /// <summary>The computed or directly-entered value of the cell.</summary>
    public ScalarValue Value { get; set; } = new BlankValue();

    /// <summary>
    /// The formula text (without leading '='), or null if this cell has a literal value.
    /// </summary>
    public string? FormulaText { get; set; }

    /// <summary>Whether this cell contains a formula.</summary>
    public bool HasFormula => FormulaText is not null;

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
        StyleId = StyleId
    };
}
