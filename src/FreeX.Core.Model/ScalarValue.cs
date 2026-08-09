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
/// Represents a date/time value stored as an Excel serial number (double), exactly as the
/// workbook file itself stores it: serial 1 is 1900-01-01, and the fractional part is the
/// time of day.
/// </summary>
/// <remarks>
/// An Excel serial is NOT the same as .NET's OLE Automation date. Excel's 1900 calendar contains
/// a fictitious 1900-02-29 (serial 60) that never existed; .NET's OADate reserves no slot for it,
/// so every genuine date in 1900-01-01..1900-02-28 sits exactly one day LATER in OADate space
/// than its Excel serial (e.g. 1900-01-15 is OADate 16 but Excel serial 15). The conversions
/// below correct for that, keeping this type's <see cref="Value"/> in the same serial space that
/// the formula engine (FreeX.Core.Formula.ExcelDateSystem) and the number formatter already
/// assume — without it, a date typed or loaded in that window computes and renders one day late.
///
/// The correction deliberately covers only the genuine-date range (serial &gt;= 1). A serial below
/// 1 carries no date part at all — it is a pure time of day (see the time-only entries produced by
/// the text/HTML readers) — and stays on the plain OADate convention, whose 1899-12-30 zero point
/// is the sentinel those writers use to recognize a time-only value. Excel's own "day zero"
/// (displayed as 1/0/1900) is not representable as a .NET <see cref="DateTime"/> either way.
/// </remarks>
public sealed record DateTimeValue(double Value) : ScalarValue
{
    // Excel serial 60 is the phantom 1900-02-29; serials 1..59 are the genuine dates that OADate
    // shifts by a day. FakeLeapDayBoundary is the first date OADate and Excel agree on again.
    private const double FirstDateSerial = 1;
    private const double PhantomLeapDaySerial = 60;
    private static readonly DateTime FirstDate = new(1900, 1, 1);
    private static readonly DateTime FakeLeapDayBoundary = new(1900, 3, 1);

    // DateTime.FromOADate's representable range (year 100..9999).
    private const double MinOleAutomationSerial = -657434d;
    private const double MaxOleAutomationSerial = 2958465d;

    /// <summary>
    /// Converts the Excel serial to a <see cref="DateTime"/>, throwing
    /// <see cref="ArgumentOutOfRangeException"/> when the serial is outside the representable range.
    /// <para>
    /// The throwing behaviour is deliberate and load-bearing: the IO writers rely on it to recognise
    /// an out-of-range serial and persist it as a raw numeric/text cell so the original value survives
    /// a round trip (R68). Callers that merely display, format, or compare a date must therefore use
    /// <see cref="TryToDateTime"/> rather than defending with a clamp here, which would silently
    /// rewrite the saved value.
    /// </para>
    /// </summary>
    public DateTime ToDateTime() => DateTime.FromOADate(AdjustedSerial);

    /// <summary>
    /// Safe counterpart to <see cref="ToDateTime"/>: returns <see langword="false"/> instead of
    /// throwing when the serial cannot be represented as a <see cref="DateTime"/>.
    /// <para>
    /// Nothing clamps a serial at creation — date autofill extrapolates a series freely, Paste Special
    /// can do arithmetic on a date, and a loaded file may carry any double — so display-side callers
    /// (filter value text, pivot refresh, the accessibility checker) would otherwise crash the app on
    /// an ordinary action such as opening a filter dropdown.
    /// </para>
    /// </summary>
    public bool TryToDateTime(out DateTime value)
    {
        var serial = AdjustedSerial;
        if (!double.IsFinite(serial) || serial < MinOleAutomationSerial || serial > MaxOleAutomationSerial)
        {
            value = default;
            return false;
        }

        value = DateTime.FromOADate(serial);
        return true;
    }

    private double AdjustedSerial =>
        Value is >= FirstDateSerial and < PhantomLeapDaySerial ? Value + 1 : Value;

    public static DateTimeValue FromDateTime(DateTime dt) =>
        new(dt >= FirstDate && dt < FakeLeapDayBoundary ? dt.ToOADate() - 1 : dt.ToOADate());
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
    /// Sentinel returned by INDIRECT (see BuiltInFunctions.Lookup.Indirect.cs) when its string
    /// argument dynamically resolves back to the very cell currently being evaluated — e.g.
    /// A1=INDIRECT("A1")+1. That self-reference has no STATIC precedent edge in the dependency
    /// graph (INDIRECT's target is a runtime string, invisible to RecalcEngine's AST-walking
    /// CollectReferences), so Tarjan's SCC pass can never classify the cell as cyclic the way a
    /// direct A1=A1+1 self-loop is. RecalcEngine's per-cell evaluation loop watches for this exact
    /// instance and routes the cell through the same AddCyclicCell path a statically-detected cycle
    /// uses (seed to 0, record "#CIRCULAR!", track in CyclicCells) — see R86-calc-volatile-
    /// circular-5-2.
    ///
    /// Deliberately a SEPARATE instance from <see cref="Circular"/> (not merely the same Code) even
    /// though both carry "#CIRCULAR!": several IO adapters (DelimitedTextWorkbookReader,
    /// DifFileAdapter, PdfTableReader, PrnFileAdapter, SlkFileAdapter) map a literal "#CIRCULAR!"
    /// cached value straight to <see cref="Circular"/> when importing a file, and an ordinary
    /// formula that merely references such an imported cell (e.g. B1=A1, where A1 holds that
    /// imported literal) must keep propagating it like any other error — not be misclassified as a
    /// brand-new runtime circular reference of its own. Using a distinct instance here lets
    /// RecalcEngine's identity check (<c>ReferenceEquals</c>) tell the two apart; a record's
    /// value-based <c>==</c>/<c>Equals</c> would not.
    /// </summary>
    public static readonly ErrorValue RuntimeCircularSelfReference = new("#CIRCULAR!");

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
