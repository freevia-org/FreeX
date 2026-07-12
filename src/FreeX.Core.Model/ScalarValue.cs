namespace FreeX.Core.Model;

/// <summary>
/// Base type for all cell values. A cell always contains exactly one ScalarValue.
/// </summary>
public abstract record ScalarValue;

/// <summary>Represents an empty cell.</summary>
public sealed record BlankValue() : ScalarValue
{
    public static readonly BlankValue Instance = new();
}

/// <summary>Represents a numeric cell value (all Excel numbers are IEEE 754 doubles).</summary>
public sealed record NumberValue(double Value) : ScalarValue;

/// <summary>Represents a boolean cell value.</summary>
public sealed record BoolValue(bool Value) : ScalarValue;

/// <summary>Represents a text/string cell value.</summary>
public sealed record TextValue(string Value) : ScalarValue;

/// <summary>
/// Represents a date/time value stored as an OLE Automation date (double).
/// Excel stores dates as serial numbers; this preserves that representation.
/// </summary>
public sealed record DateTimeValue(double Value) : ScalarValue
{
    public DateTime ToDateTime() => DateTime.FromOADate(Value);
    public static DateTimeValue FromDateTime(DateTime dt) => new(dt.ToOADate());
}

/// <summary>Represents a cell error value (e.g. #DIV/0!, #VALUE!, #REF!).</summary>
public sealed record ErrorValue(string Code) : ScalarValue
{
    public static readonly ErrorValue DivByZero = new("#DIV/0!");
    public static readonly ErrorValue Value = new("#VALUE!");
    public static readonly ErrorValue Ref = new("#REF!");
    public static readonly ErrorValue Name = new("#NAME?");
    public static readonly ErrorValue Null = new("#NULL!");
    public static readonly ErrorValue NA = new("#N/A");
    public static readonly ErrorValue Num = new("#NUM!");
    public static readonly ErrorValue Circular = new("#CIRCULAR!");
    public static readonly ErrorValue Spill = new("#SPILL!");
    public static readonly ErrorValue Calc = new("#CALC!");

    /// <summary>
    /// Excel's #FIELD! error: raised for linked-data-type field access (e.g. <c>=A1.Price</c>)
    /// when the referenced cell isn't a Rich Data Type record or doesn't expose that field.
    /// FreeX doesn't model linked data types, so any "&lt;cellref&gt;.&lt;field&gt;" reference
    /// surfaces this instead of being misrouted through named-range lookup to #NAME?.
    /// ERROR.TYPE(#FIELD!) is 13 (see BuiltInFunctions.ArrayInfo.ErrorTypeScalar).
    /// </summary>
    public static readonly ErrorValue Field = new("#FIELD!");
}

/// <summary>
/// Represents a 2-D range of cell values passed to structured functions
/// such as VLOOKUP, INDEX, MATCH, SUMIF, etc.
/// Rows and columns are 0-based internally; exposed as 1-based via At().
/// </summary>
public sealed record RangeValue(ScalarValue[,] Cells, uint StartRow = 1, uint StartCol = 1) : ScalarValue
{
    public string? SheetName { get; init; }

    /// <summary>
    /// True only when this RangeValue was materialized directly from a genuine worksheet
    /// reference — a cell/range reference, a named range, INDIRECT, OFFSET, INDEX's reference
    /// form, and the like — as opposed to being synthesized by a function or operator
    /// (FILTER, SORT, SEQUENCE, MAP, arithmetic broadcast over a range, ROW/COLUMN, ...).
    /// Only a genuine reference's <see cref="StartRow"/>/<see cref="StartCol"/>/<see cref="SheetName"/>
    /// coordinates map to real cells whose hidden-row state and formula text are meaningful, so
    /// coordinate-sensitive consumers (SUBTOTAL / AGGREGATE hidden-row and nested-aggregate
    /// exclusion) must gate on this flag rather than guessing from the coordinates: a computed
    /// array defaults to StartRow=1/StartCol=1/SheetName=null, which is field-for-field identical
    /// to a genuine same-sheet A1-anchored reference and therefore indistinguishable by coordinate
    /// alone. See R25-aggregate-subtotal-deep-3.
    /// </summary>
    public bool IsSheetReference { get; init; }

    public int RowCount => Cells.GetLength(0);
    public int ColCount => Cells.GetLength(1);

    /// <summary>Get a value by 1-based row and column index.</summary>
    public ScalarValue At(int row1, int col1) => Cells[row1 - 1, col1 - 1];

    /// <summary>Extract a flat column (1-based) as a list.</summary>
    public IReadOnlyList<ScalarValue> GetColumn(int col1)
    {
        var list = new List<ScalarValue>(RowCount);
        for (int r = 0; r < RowCount; r++)
            list.Add(Cells[r, col1 - 1]);
        return list;
    }

    /// <summary>Extract a flat row (1-based) as a list.</summary>
    public IReadOnlyList<ScalarValue> GetRow(int row1)
    {
        var list = new List<ScalarValue>(ColCount);
        for (int c = 0; c < ColCount; c++)
            list.Add(Cells[row1 - 1, c]);
        return list;
    }

    /// <summary>All values in row-major order.</summary>
    public IReadOnlyList<ScalarValue> Flatten()
    {
        var list = new List<ScalarValue>(RowCount * ColCount);
        for (int r = 0; r < RowCount; r++)
            for (int c = 0; c < ColCount; c++)
                list.Add(Cells[r, c]);
        return list;
    }
}
