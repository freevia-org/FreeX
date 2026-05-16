using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

/// <summary>
/// Registry of built-in spreadsheet functions.
/// Phase 1 provides the 16 most essential functions per the build plan.
/// </summary>
public static class BuiltInFunctions
{
    /// <summary>
    /// Delegate type for a built-in function.
    /// Receives evaluated arguments and a context for resolving ranges.
    /// </summary>
    public delegate ScalarValue FormulaFunction(IReadOnlyList<ScalarValue> args, IEvalContext ctx);

    private static readonly Dictionary<string, (FormulaFunction Func, int MinArgs, int MaxArgs)> Functions = new()
    {
        // ── Existing Phase-1 functions ──────────────────────────────────────
        ["SUM"]         = (Sum, 1, 255),
        ["AVERAGE"]     = (Average, 1, 255),
        ["MIN"]         = (Min, 1, 255),
        ["MAX"]         = (Max, 1, 255),
        ["COUNT"]       = (Count, 1, 255),
        ["COUNTA"]      = (CountA, 1, 255),
        ["IF"]          = (If, 2, 3),
        ["AND"]         = (And, 1, 255),
        ["OR"]          = (Or, 1, 255),
        ["NOT"]         = (Not, 1, 1),
        ["ROUND"]       = (Round, 2, 2),
        ["ABS"]         = (Abs, 1, 1),
        ["CONCAT"]      = (Concat, 1, 255),
        ["LEN"]         = (Len, 1, 1),
        ["LEFT"]        = (Left, 1, 2),
        ["RIGHT"]       = (Right, 1, 2),
        ["NOW"]         = (Now, 0, 0),
        ["TODAY"]       = (Today, 0, 0),
        ["RAND"]        = (Rand, 0, 0),

        // ── Phase 4.2: Error handling ────────────────────────────────────────
        ["IFERROR"]     = (IfError, 2, 2),
        ["IFNA"]        = (IfNa, 2, 2),
        ["NA"]          = (NaFunc, 0, 0),

        // ── Phase 4.2: Lookup ────────────────────────────────────────────────
        ["VLOOKUP"]     = (Vlookup, 3, 4),
        ["HLOOKUP"]     = (Hlookup, 3, 4),
        ["INDEX"]       = (Index, 2, 3),
        ["MATCH"]       = (Match, 2, 3),
        ["XMATCH"]      = (Xmatch, 2, 4),

        // ── Phase 4.2: Conditional aggregation ──────────────────────────────
        ["SUMIF"]       = (Sumif, 2, 3),
        ["COUNTIF"]     = (Countif, 2, 2),
        ["AVERAGEIF"]   = (Averageif, 2, 3),

        // ── Phase 4.2: Text ──────────────────────────────────────────────────
        ["TEXT"]        = (TextFunc, 2, 2),
        ["TRIM"]        = (Trim, 1, 1),
        ["UPPER"]       = (Upper, 1, 1),
        ["LOWER"]       = (Lower, 1, 1),
        ["PROPER"]      = (Proper, 1, 1),
        ["SUBSTITUTE"]  = (Substitute, 3, 4),
        ["FIND"]        = (Find, 2, 3),
        ["SEARCH"]      = (Search, 2, 3),
        ["MID"]         = (Mid, 3, 3),
        ["REPT"]        = (Rept, 2, 2),
        ["VALUE"]       = (ValueFunc, 1, 1),

        // ── Phase 4.2: Date & time ───────────────────────────────────────────
        ["DATE"]        = (Date, 3, 3),
        ["YEAR"]        = (Year, 1, 1),
        ["MONTH"]       = (Month, 1, 1),
        ["DAY"]         = (Day, 1, 1),
        ["HOUR"]        = (Hour, 1, 1),
        ["MINUTE"]      = (Minute, 1, 1),
        ["SECOND"]      = (Second, 1, 1),
        ["WEEKDAY"]     = (Weekday, 1, 2),
        ["EDATE"]       = (Edate, 2, 2),
        ["DATEDIF"]     = (Datedif, 3, 3),

        // ── Phase 4.2: Math ──────────────────────────────────────────────────
        ["MOD"]         = (Mod, 2, 2),
        ["POWER"]       = (Power, 2, 2),
        ["SQRT"]        = (Sqrt, 1, 1),
        ["INT"]         = (IntFunc, 1, 1),
        ["CEILING"]     = (Ceiling, 2, 2),
        ["FLOOR"]       = (Floor, 2, 2),
        ["RANDBETWEEN"] = (Randbetween, 2, 2),
        ["SIGN"]        = (Sign, 1, 1),
        ["LOG"]         = (Log, 1, 2),
        ["LN"]          = (Ln, 1, 1),
        ["EXP"]         = (Exp, 1, 1),
        ["PI"]          = (Pi, 0, 0),
        ["FACT"]        = (Fact, 1, 1),

        // ── Phase 4.2: Statistical ───────────────────────────────────────────
        ["LARGE"]       = (Large, 2, 2),
        ["SMALL"]       = (Small, 2, 2),
        ["RANK"]        = (Rank, 2, 3),
        ["STDEV"]       = (Stdev, 1, 255),
        ["STDEV.S"]     = (Stdev, 1, 255),
        ["MEDIAN"]      = (Median, 1, 255),

        // ── Phase 5: Additional commonly-used functions ──────────────────────

        // Multi-criteria aggregation
        ["SUMIFS"]      = (Sumifs, 3, 255),
        ["COUNTIFS"]    = (Countifs, 2, 255),
        ["AVERAGEIFS"]  = (Averageifs2, 3, 255),

        // Modern lookup
        ["XLOOKUP"]     = (Xlookup, 3, 6),

        // Multi-condition logic
        ["IFS"]         = (Ifs, 2, 255),
        ["SWITCH"]      = (Switch, 3, 255),

        // IS functions
        ["ISBLANK"]     = (Isblank, 1, 1),
        ["ISNUMBER"]    = (Isnumber, 1, 1),
        ["ISTEXT"]      = (Istext, 1, 1),
        ["ISERROR"]     = (Iserror, 1, 1),
        ["ISNA"]        = (Isna, 1, 1),
        ["ISLOGICAL"]   = (Islogical, 1, 1),

        // Reference helpers
        ["ROW"]         = (Row, 0, 1),
        ["COLUMN"]      = (Column, 0, 1),
        ["ROWS"]        = (Rows, 1, 1),
        ["COLUMNS"]     = (Columns, 1, 1),

        // Text
        ["TEXTJOIN"]    = (Textjoin, 3, 255),

        // Count
        ["COUNTBLANK"]  = (Countblank, 1, 1),

        // Misc
        ["CHOOSE"]      = (Choose, 2, 255),
        ["SUMPRODUCT"]  = (Sumproduct, 1, 255),
        ["ROUNDDOWN"]   = (Rounddown, 2, 2),
        ["ROUNDUP"]     = (Roundup, 2, 2),
        ["TRUNC"]       = (Trunc, 1, 2),
        ["EXACT"]       = (Exact, 2, 2),
        ["CODE"]        = (Code, 1, 1),
        ["CHAR"]        = (Char, 1, 1),

        // ── Phase 4a: Math / Trig ────────────────────────────────────────────
        ["SIN"]      = (Sin, 1, 1),
        ["COS"]      = (Cos, 1, 1),
        ["TAN"]      = (Tan, 1, 1),
        ["ASIN"]     = (Asin, 1, 1),
        ["ACOS"]     = (Acos, 1, 1),
        ["ATAN"]     = (Atan, 1, 1),
        ["ATAN2"]    = (Atan2Func, 2, 2),
        ["DEGREES"]  = (Degrees, 1, 1),
        ["RADIANS"]  = (Radians, 1, 1),
        ["PRODUCT"]  = (Product, 1, 255),
        ["QUOTIENT"] = (Quotient, 2, 2),
        ["GCD"]      = (Gcd, 1, 255),
        ["LCM"]      = (Lcm, 1, 255),
        ["MROUND"]   = (Mround, 2, 2),
        ["COMBIN"]   = (Combin, 2, 2),
        ["PERMUT"]   = (Permut, 2, 2),
        ["ODD"]      = (Odd, 1, 1),
        ["EVEN"]     = (Even, 1, 1),

        // ── Phase 4a: Date / Time ────────────────────────────────────────────
        ["TIME"]         = (TimeFunc, 3, 3),
        ["TIMEVALUE"]    = (Timevalue, 1, 1),
        ["DATEVALUE"]    = (Datevalue, 1, 1),
        ["EOMONTH"]      = (Eomonth, 2, 2),
        ["WEEKNUM"]      = (Weeknum, 1, 2),
        ["ISOWEEKNUM"]   = (Isoweeknum, 1, 1),
        ["WORKDAY"]      = (Workday, 2, 3),
        ["NETWORKDAYS"]  = (Networkdays, 2, 3),
        ["DAYS"]         = (Days, 2, 2),
        ["DAYS360"]      = (Days360, 2, 3),
        ["YEARFRAC"]     = (Yearfrac, 2, 3),

        // ── Phase 4a: Statistical ────────────────────────────────────────────
        ["VAR"]              = (VarS, 1, 255),
        ["VAR.S"]            = (VarS, 1, 255),
        ["VAR.P"]            = (VarP, 1, 255),
        ["STDEV.P"]          = (StdevP, 1, 255),
        ["PERCENTILE"]       = (PercentileInc, 2, 2),
        ["PERCENTILE.INC"]   = (PercentileInc, 2, 2),
        ["PERCENTILE.EXC"]   = (PercentileExc, 2, 2),
        ["QUARTILE"]         = (QuartileInc, 2, 2),
        ["QUARTILE.INC"]     = (QuartileInc, 2, 2),
        ["GEOMEAN"]          = (Geomean, 1, 255),
        ["HARMEAN"]          = (Harmean, 1, 255),
        ["AVEDEV"]           = (Avedev, 1, 255),
        ["PERCENTRANK"]      = (PercentrankInc, 2, 3),
        ["PERCENTRANK.INC"]  = (PercentrankInc, 2, 3),
        ["MODE"]             = (ModeSngl, 1, 255),
        ["MODE.SNGL"]        = (ModeSngl, 1, 255),
        ["CORREL"]           = (Correl, 2, 2),
        ["FORECAST"]         = (Forecast, 3, 3),
        ["FORECAST.LINEAR"]  = (Forecast, 3, 3),

        // ── Phase 4a: Financial ──────────────────────────────────────────────
        ["PMT"]  = (Pmt, 3, 5),
        ["PV"]   = (Pv, 3, 5),
        ["FV"]   = (Fv, 3, 5),
        ["NPER"] = (Nper, 3, 5),
        ["RATE"] = (Rate, 3, 6),
        ["NPV"]  = (Npv, 2, 255),
        ["IRR"]  = (Irr, 1, 2),
        ["SLN"]  = (Sln, 3, 3),

        // ── Phase 4a: Logical / Text ─────────────────────────────────────────
        ["XOR"]         = (Xor, 1, 255),
        ["TRUE"]        = (TrueFunc, 0, 0),
        ["FALSE"]       = (FalseFunc, 0, 0),
        ["ISEVEN"]      = (Iseven, 1, 1),
        ["ISODD"]       = (Isodd, 1, 1),
        ["REPLACE"]     = (Replace, 4, 4),
        ["CONCATENATE"] = (Concatenate, 1, 255),
        ["T"]           = (TFunc, 1, 1),
        ["FIXED"]       = (Fixed, 1, 3),
        ["CLEAN"]       = (Clean, 1, 1),
        ["DOLLAR"]      = (Dollar, 1, 2),

        // ── Phase 4a: Reference ──────────────────────────────────────────────
        ["INDIRECT"] = (Indirect, 1, 2),
        ["ADDRESS"]  = (Address, 2, 5),
        ["LOOKUP"]   = (Lookup, 2, 3),
        ["N"]        = (NFunc, 1, 1),

        // ── Phase 4b: Dynamic arrays ─────────────────────────────────────────
        ["SEQUENCE"] = (Sequence, 1, 4),
        ["RANDARRAY"] = (RandArray, 0, 5),
        ["FILTER"]   = (Filter, 2, 3),
        ["SORT"]     = (Sort, 1, 4),
        ["SORTBY"]   = (SortBy, 2, 255),
        ["TAKE"]     = (Take, 2, 3),
        ["DROP"]     = (Drop, 2, 3),
        ["CHOOSEROWS"] = (ChooseRows, 2, 255),
        ["CHOOSECOLS"] = (ChooseCols, 2, 255),
        ["VSTACK"]   = (VStack, 1, 255),
        ["HSTACK"]   = (HStack, 1, 255),
        ["TOROW"]    = (ToRow, 1, 3),
        ["TOCOL"]    = (ToCol, 1, 3),
        ["WRAPROWS"] = (WrapRows, 2, 3),
        ["WRAPCOLS"] = (WrapCols, 2, 3),
        ["EXPAND"]   = (Expand, 2, 4),
        ["UNIQUE"]   = (Unique, 1, 3),

        // ── Subtotal ─────────────────────────────────────────────────────────
        ["SUBTOTAL"] = (Subtotal, 2, 255),
    };

    private static readonly HashSet<string> VolatileFunctions = ["NOW", "TODAY", "RAND", "RANDBETWEEN", "RANDARRAY", "INDIRECT"];

    /// <summary>True if the function recalculates on every pass regardless of input changes.</summary>
    public static bool IsVolatile(string name) => VolatileFunctions.Contains(name);

    /// <summary>Check if a function name is registered.</summary>
    public static bool Exists(string name) => Functions.ContainsKey(name);

    /// <summary>Get a function by name.</summary>
    public static (FormulaFunction Func, int MinArgs, int MaxArgs) Get(string name) => Functions[name];

    /// <summary>Validate argument count for a function.</summary>
    public static bool ValidateArgCount(string name, int count)
    {
        if (!Functions.TryGetValue(name, out var entry)) return false;
        return count >= entry.MinArgs && count <= entry.MaxArgs;
    }

    // ── Helper: coerce a ScalarValue to double ──

    private static double ToNumber(ScalarValue v) => v switch
    {
        NumberValue n => n.Value,
        DateTimeValue d => d.Value,
        BoolValue b => b.Value ? 1.0 : 0.0,
        BlankValue => 0.0,
        DirectTextLiteralValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        TextValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to number")
    };

    internal static bool ToBool(ScalarValue v) => v switch
    {
        BoolValue b => b.Value,
        NumberValue n => n.Value != 0.0,
        DateTimeValue d => d.Value != 0.0,
        BlankValue => false,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to boolean")
    };

    private static string ToText(ScalarValue v) => v switch
    {
        DirectTextLiteralValue t => t.Value,
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTimeValue d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    private static bool TryDirectTextNumber(DirectTextLiteralValue value, out double number) =>
        double.TryParse(value.Value, System.Globalization.CultureInfo.InvariantCulture, out number);

    private static bool TryCellNumber(ScalarValue value, out double number)
    {
        switch (value)
        {
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private static bool SameShape(RangeValue left, RangeValue right) =>
        left.RowCount == right.RowCount && left.ColCount == right.ColCount;

    private static bool TryReferencedNumber(ReferencedScalarValue value, out double number, out ErrorValue? error)
    {
        number = 0;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case NumberValue n:
                number = n.Value;
                return true;
            case DateTimeValue d:
                number = d.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReferencedBool(ReferencedScalarValue value, out bool boolean, out ErrorValue? error)
    {
        boolean = false;
        error = null;
        switch (value.Value)
        {
            case ErrorValue e:
                error = e;
                return false;
            case BoolValue b:
                boolean = b.Value;
                return true;
            case NumberValue n:
                boolean = n.Value != 0.0;
                return true;
            case DateTimeValue d:
                boolean = d.Value != 0.0;
                return true;
            default:
                return false;
        }
    }

    private static ErrorValue? FirstError(IReadOnlyList<ScalarValue> args)
    {
        foreach (var arg in args)
            if (arg is ErrorValue e) return e;
        return null;
    }

    // ── Function implementations ──

    private static ScalarValue Sum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) total += value;
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        return NumberResult(total);
    }

    private static ScalarValue Average(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    total += value;
                    count++;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                total += value;
                count++;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue Min(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (min is null || value < min) min = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (min is null || value < min) min = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (min is null || val < min) min = val;
        }
        return min.HasValue ? NumberResult(min.Value) : new NumberValue(0);
    }

    private static ScalarValue Max(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (max is null || value > max) max = value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                if (max is null || value > max) max = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (max is null || val > max) max = val;
        }
        return max.HasValue ? NumberResult(max.Value) : new NumberValue(0);
    }

    private static ScalarValue Count(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out _, out var refError)) count++;
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is DirectTextLiteralValue direct)
            {
                if (TryDirectTextNumber(direct, out _)) count++;
                continue;
            }
            if (arg is NumberValue or BoolValue or DateTimeValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue CountA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        int count = 0;
        foreach (var arg in args)
        {
            if (arg is not BlankValue)
                count++;
        }
        return new NumberValue(count);
    }

    private static ScalarValue If(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var condition = ToBool(args[0]);
        if (condition)
            return args[1];
        return args.Count > 2 ? args[2] : new BoolValue(false);
    }

    private static ScalarValue And(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    if (!value) return new BoolValue(false);
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is TextValue or BlankValue) return ErrorValue.Value;
            hadUsableValue = true;
            if (!ToBool(arg)) return new BoolValue(false);
        }
        return hadUsableValue ? new BoolValue(true) : ErrorValue.Value;
    }

    private static ScalarValue Or(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    if (value) return new BoolValue(true);
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (arg is TextValue or BlankValue) return ErrorValue.Value;
            hadUsableValue = true;
            if (ToBool(arg)) return new BoolValue(true);
        }
        return hadUsableValue ? new BoolValue(false) : ErrorValue.Value;
    }

    private static ScalarValue Not(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        return new BoolValue(!ToBool(args[0]));
    }

    private static ScalarValue Round(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err0) return err0;
        if (args[1] is ErrorValue err1) return err1;
        var number = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        if (digits < -15 || digits > 15) return ErrorValue.Num;
        if (digits >= 0)
            return NumberResult(Math.Round(number, digits, MidpointRounding.AwayFromZero));

        double factor = Math.Pow(10, -digits);
        return NumberResult(Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor);
    }

    private static ScalarValue NumberResult(double value) =>
        double.IsFinite(value) ? new NumberValue(value) : ErrorValue.Num;

    private static bool TryTruncateToLong(double value, out long result)
    {
        result = 0;
        if (!double.IsFinite(value) || value < long.MinValue || value >= 9223372036854775808.0)
            return false;
        result = (long)Math.Truncate(value);
        return true;
    }

    private static double RoundWithExcelDigits(double number, int digits)
    {
        if (digits >= 0)
            return Math.Round(number, digits, MidpointRounding.AwayFromZero);

        double factor = Math.Pow(10, -digits);
        return Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor;
    }

    private static ScalarValue Abs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Abs(n));
    }

    private static ScalarValue Concat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            sb.Append(ToText(arg));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Len(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        return new NumberValue(ToText(args[0]).Length);
    }

    private static ScalarValue Left(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount)) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return TextResult(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var rawCount = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawCount)) return ErrorValue.Value;
        var count = (int)rawCount;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return TextResult(count == 0 ? "" : text[^count..]);
    }

    private static ScalarValue Now(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Now);

    private static ScalarValue Today(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Today);

    private static ScalarValue Rand(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Random.Shared.NextDouble());

    private static ScalarValue RandArray(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        foreach (var arg in args)
            if (arg is ErrorValue e) return e;

        double rowsD = args.Count > 0 && args[0] is not BlankValue ? ToNumber(args[0]) : 1;
        double colsD = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double min = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        double max = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        bool wholeNumber = args.Count > 4 && args[4] is not BlankValue && ToBool(args[4]);

        if (!double.IsFinite(rowsD) || !double.IsFinite(colsD)) return ErrorValue.Value;
        int rows = (int)rowsD;
        int cols = (int)colsD;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        if (!double.IsFinite(min) || !double.IsFinite(max) || min > max) return ErrorValue.Value;

        if (wholeNumber)
        {
            if (!TryTruncateToLong(Math.Ceiling(min), out long bottom) ||
                !TryTruncateToLong(Math.Floor(max), out long top))
                return ErrorValue.Value;
            if (bottom > top) return ErrorValue.Value;

            long span;
            try { span = checked(top - bottom + 1); }
            catch (OverflowException) { return ErrorValue.Value; }
            var integers = new ScalarValue[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    integers[r, c] = new NumberValue(Random.Shared.NextInt64(bottom, bottom + span));
            return new RangeValue(integers);
        }

        double width = max - min;
        if (!double.IsFinite(width)) return ErrorValue.Value;
        var result = new ScalarValue[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var value = min + Random.Shared.NextDouble() * width;
                if (!double.IsFinite(value)) return ErrorValue.Value;
                result[r, c] = new NumberValue(value);
            }
        return new RangeValue(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Error handling
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue IfError(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue) return args[1];
        return args[0];
    }

    private static ScalarValue IfNa(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e && e.Code == "#N/A") return args[1];
        return args[0];
    }

    private static ScalarValue NaFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        ErrorValue.NA;

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Lookup
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Vlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawCol = ToNumber(args[2]);
        if (!double.IsFinite(rawCol)) return ErrorValue.Value;
        int colIndex = (int)rawCol;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || ToBool(args[3]); // default TRUE

        if (colIndex < 1 || colIndex > (int)table.ColCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            // Approximate match – table must be sorted ascending on first column
            // Return last row where first-col value <= lookupValue
            int bestRow = -1;
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestRow = r;
                else
                    break;
            }
            if (bestRow < 0) return ErrorValue.NA;
            return table.At(bestRow, colIndex);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup column
            for (int r = 1; r <= table.RowCount; r++)
            {
                var cv = table.At(r, 1);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(r, colIndex);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Hlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawRow = ToNumber(args[2]);
        if (!double.IsFinite(rawRow)) return ErrorValue.Value;
        int rowIndex = (int)rawRow;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        bool rangeLookup = args.Count < 4 || ToBool(args[3]);

        if (rowIndex < 1 || rowIndex > (int)table.RowCount) return ErrorValue.Ref;

        if (rangeLookup)
        {
            int bestCol = -1;
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (CompareScalar(cv, lookupValue) <= 0)
                    bestCol = c;
                else
                    break;
            }
            if (bestCol < 0) return ErrorValue.NA;
            return table.At(rowIndex, bestCol);
        }
        else
        {
            // Exact match — propagate errors encountered in the lookup row
            for (int c = 1; c <= table.ColCount; c++)
            {
                var cv = table.At(1, c);
                if (cv is ErrorValue ev) return ev;
                if (MatchExactValue(cv, lookupValue))
                    return table.At(rowIndex, c);
            }
            return ErrorValue.NA;
        }
    }

    private static ScalarValue Index(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue table) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        double rawRowNum = ToNumber(args[1]);
        if (!double.IsFinite(rawRowNum)) return ErrorValue.Value;
        int rowNum = (int)rawRowNum;
        double rawColNum = args.Count > 2 ? ToNumber(args[2]) : 1.0;
        if (!double.IsFinite(rawColNum)) return ErrorValue.Value;
        int colNum = (int)rawColNum;

        // For a 1-D range with a single index argument, the index selects along the
        // only dimension (column for a 1-row range, row for a 1-column range).
        if (args.Count == 2)
        {
            if (table.RowCount == 1) { colNum = rowNum; rowNum = 1; }
            else if (table.ColCount == 1) { /* rowNum already correct, colNum = 1 */ }
        }

        // Negative indices → #VALUE! (out-of-range positive → #REF! per Excel)
        if (rowNum < 0) return ErrorValue.Value;
        if (colNum < 0) return ErrorValue.Value;
        if (rowNum > table.RowCount) return ErrorValue.Ref;
        if (colNum > table.ColCount) return ErrorValue.Ref;

        if (rowNum == 0 && colNum == 0)
            return table;

        if (rowNum == 0)
        {
            var col = new ScalarValue[table.RowCount, 1];
            for (int r = 0; r < table.RowCount; r++)
                col[r, 0] = table.Cells[r, colNum - 1];
            return new RangeValue(col);
        }

        if (colNum == 0)
        {
            var row = new ScalarValue[1, table.ColCount];
            for (int c = 0; c < table.ColCount; c++)
                row[0, c] = table.Cells[rowNum - 1, c];
            return new RangeValue(row);
        }

        return table.At(rowNum, colNum);
    }

    private static ScalarValue Match(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        double rawMatchType = args.Count > 2 ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(rawMatchType)) return ErrorValue.NA;
        int matchType = (int)rawMatchType;
        if (matchType is not (-1 or 0 or 1)) return ErrorValue.NA;

        // Flatten to 1-D (single row or column expected)
        var flat = table.Flatten();

        if (matchType == 0)
        {
            // Exact match — propagate errors encountered in the lookup array
            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] is ErrorValue ev) return ev;
                if (MatchExactValue(flat[i], lookupValue))
                    return new NumberValue(i + 1);
            }
            return ErrorValue.NA;
        }
        else if (matchType == 1)
        {
            // Ascending approximate: largest value <= lookupValue
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (CompareScalar(flat[i], lookupValue) <= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
        else // matchType == -1
        {
            // Descending approximate: smallest value >= lookupValue.
            // Assumes the lookup vector is sorted descending, matching Excel's contract.
            int best = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                if (CompareScalar(flat[i], lookupValue) >= 0)
                    best = i;
                else
                    break;
            }
            if (best < 0) return ErrorValue.NA;
            return new NumberValue(best + 1);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Conditional aggregation
    // ═══════════════════════════════════════════════════════════════════

    private static bool MatchExactValue(ScalarValue candidate, ScalarValue lookupValue)
    {
        if (lookupValue is TextValue pattern && candidate is TextValue text)
            return WildcardMatch(text.Value, pattern.Value, ignoreCase: true);

        return ScalarEquals(candidate, lookupValue);
    }

    private static ScalarValue Xmatch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupArr) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (lookupArr.RowCount != 1 && lookupArr.ColCount != 1) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();
        int matchMode = args.Count > 2 && args[2] is not BlankValue ? (int)ToNumber(args[2]) : 0;
        int searchMode = args.Count > 3 && args[3] is not BlankValue ? (int)ToNumber(args[3]) : 1;
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return new NumberValue(i + 1);
            return ErrorValue.NA;
        }

        if (matchMode == -1)
        {
            int best = -1;
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) <= 0)
                    best = i;
            return best >= 0 ? new NumberValue(best + 1) : ErrorValue.NA;
        }

        foreach (int i in indices)
            if (CompareScalar(lookupFlat[i], lookupValue) >= 0)
                return new NumberValue(i + 1);
        return ErrorValue.NA;
    }

    private static ScalarValue Sumif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue sumRangeError) return sumRangeError;
        RangeValue? sumRange = args.Count > 2 ? args[2] as RangeValue : null;

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> sumFlat = sumRange is not null ? sumRange.Flatten() : rangeFlat;

        double total = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < sumFlat.Count ? sumFlat[i] : BlankValue.Instance;
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) total += value;
                else if (sv is BlankValue) { /* skip */ }
            }
        }
        return NumberResult(total);
    }

    private static ScalarValue Countif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;

        int count = 0;
        foreach (var v in rangeArg.Flatten())
            if (MatchesCriteria(v, criteria))
                count++;
        return new NumberValue(count);
    }

    private static ScalarValue Averageif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue rangeError) return rangeError;
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
        if (args.Count > 2 && args[2] is ErrorValue avgRangeError) return avgRangeError;
        RangeValue? avgRange = args.Count > 2 ? args[2] as RangeValue : null;

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> avgFlat = avgRange is not null ? avgRange.Flatten() : rangeFlat;

        double total = 0;
        int count = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < avgFlat.Count ? avgFlat[i] : BlankValue.Instance;
                if (sv is ErrorValue e) return e;
                if (TryCellNumber(sv, out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    /// <summary>
    /// Test a cell value against an Excel criteria string or value.
    /// Supports: number (exact), text (exact, case-insensitive),
    /// operator strings ">5", ">=5", "<5", "<=5", "<>5", "=text",
    /// and simple wildcard strings using * and ?.
    /// </summary>
    private static bool MatchesCriteria(ScalarValue cellValue, ScalarValue criteria)
    {
        if (criteria is BlankValue)
            criteria = new TextValue("");

        if (criteria is NumberValue cn)
            return TryCellNumber(cellValue, out double cellNumber) && cellNumber == cn.Value;

        if (criteria is DateTimeValue cdt)
            return TryCellNumber(cellValue, out double cellDateNum) && cellDateNum == cdt.Value;

        if (criteria is BoolValue cb)
            return cellValue is BoolValue cvb && cvb.Value == cb.Value;

        if (criteria is not TextValue ct) return false;
        var crit = ct.Value;

        // Operator prefix?
        if (crit.StartsWith(">=") || crit.StartsWith("<=") || crit.StartsWith("<>"))
        {
            var op  = crit[..2];
            var rhs = crit[2..];
            return ApplyComparisonCriteria(cellValue, op, rhs);
        }
        if (crit.StartsWith(">") || crit.StartsWith("<") || crit.StartsWith("="))
        {
            var op  = crit[..1];
            var rhs = crit[1..];
            return ApplyComparisonCriteria(cellValue, op, rhs);
        }

        // Plain text (supports wildcards * and ?)
        var cellText = cellValue is TextValue tv ? tv.Value :
                       TryCellNumber(cellValue, out double numericValue) ? numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) :
                       cellValue is BoolValue bv ? (bv.Value ? "TRUE" : "FALSE") :
                       "";
        return WildcardMatch(cellText, crit, ignoreCase: true);
    }

    private static bool ApplyComparisonCriteria(ScalarValue cellValue, string op, string rhs)
    {
        // Try numeric comparison first
        if (double.TryParse(rhs, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rhsNum))
        {
            if (!TryCellNumber(cellValue, out double value)) return false;
            return op switch
            {
                ">"  => value > rhsNum,
                ">=" => value >= rhsNum,
                "<"  => value < rhsNum,
                "<=" => value <= rhsNum,
                "="  => value == rhsNum,
                "<>" => value != rhsNum,
                _    => false
            };
        }
        // Text comparison
        var cellText = cellValue is TextValue tv ? tv.Value : ToText(cellValue);
        int cmp = string.Compare(cellText, rhs, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            ">"  => cmp > 0,
            ">=" => cmp >= 0,
            "<"  => cmp < 0,
            "<=" => cmp <= 0,
            "="  => cmp == 0,
            "<>" => cmp != 0,
            _    => false
        };
    }

    private static readonly ConcurrentDictionary<(string Pattern, bool IgnoreCase), Regex> WildcardCache = new();

    private static string WildcardToRegexPattern(string pattern, bool anchored = true)
    {
        var sb = new System.Text.StringBuilder(anchored ? "^" : "");
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '~' && i + 1 < pattern.Length && pattern[i + 1] is '*' or '?' or '~')
            {
                sb.Append(Regex.Escape(pattern[++i].ToString()));
                continue;
            }

            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:  sb.Append(Regex.Escape(ch.ToString())); break;
            }
        }
        if (anchored) sb.Append('$');
        return sb.ToString();
    }

    /// <summary>Simple Excel-style wildcard match (* = any chars, ? = any single char).</summary>
    private static bool WildcardMatch(string text, string pattern, bool ignoreCase)
    {
        var regex = WildcardCache.GetOrAdd((pattern, ignoreCase), key =>
        {
            var opts = key.IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.Compiled : RegexOptions.Compiled;
            return new Regex(WildcardToRegexPattern(key.Pattern), opts);
        });
        return regex.IsMatch(text);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Text functions
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue TextFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue formatError) return formatError;
        var fmt = ToText(args[1]);
        // Simple inline formatter (avoids depending on Freexcel.Core.Calc)
        var val = args[0];
        if (TryCellNumber(val, out double value))
            return TextResult(FormatNumberInline(value, fmt));
        return TextResult(ToText(val));
    }

    private static string FormatNumberInline(double value, string fmt)
    {
        if (string.IsNullOrEmpty(fmt)) return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try { return value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    private static readonly Regex MultiSpaceRegex = new(@" {2,}", RegexOptions.Compiled);

    private static ScalarValue Trim(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = MultiSpaceRegex.Replace(ToText(args[0]).Trim(), " ");
        return TextResult(text);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToUpperInvariant());
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TextResult(ToText(args[0]).ToLowerInvariant());
    }

    private static ScalarValue Proper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (text.Length == 0) return new TextValue("");
        var sb = new System.Text.StringBuilder(text.Length);
        bool capitaliseNext = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || !char.IsLetter(ch)) { capitaliseNext = true; sb.Append(ch); }
            else if (capitaliseNext) { sb.Append(char.ToUpperInvariant(ch)); capitaliseNext = false; }
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Substitute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue oldTextError) return oldTextError;
        if (args[2] is ErrorValue newTextError) return newTextError;
        var text    = ToText(args[0]);
        var oldText = ToText(args[1]);
        var newText = ToText(args[2]);

        if (oldText.Length == 0) return TextResult(text);

        if (args.Count > 3)
        {
            // Replace the Nth occurrence only
            if (args[3] is ErrorValue e3) return e3;
            double rawInstanceNum = ToNumber(args[3]);
            if (!double.IsFinite(rawInstanceNum)) return ErrorValue.Value;
            int instanceNum = (int)rawInstanceNum;
            if (instanceNum < 1) return ErrorValue.Value;
            int count = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                int idx = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                if (count == instanceNum)
                    return TextResult(text[..idx] + newText + text[(idx + oldText.Length)..]);
                pos = idx + oldText.Length;
            }
            return TextResult(text); // instance not found
        }
        else
        {
            return TextResult(text.Replace(oldText, newText, StringComparison.Ordinal));
        }
    }

    private static ScalarValue TextResult(string text) =>
        text.Length > 32767 ? ErrorValue.Value : new TextValue(text);

    private static ScalarValue Find(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum   = args.Count > 2 ? (int)ToNumber(args[2]) : 1;
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (findText.Length == 0)
            return startIdx <= withinText.Length ? new NumberValue(startNum) : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        int pos = withinText.IndexOf(findText, startIdx, StringComparison.Ordinal);
        if (pos < 0) return ErrorValue.Value;
        return new NumberValue(pos + 1);
    }

    private static readonly ConcurrentDictionary<string, Regex> SearchCache = new();

    private static ScalarValue Search(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue withinError) return withinError;
        if (args.Count > 2 && args[2] is ErrorValue startError) return startError;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum   = args.Count > 2 ? (int)ToNumber(args[2]) : 1;
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (findText.Length == 0)
            return startIdx <= withinText.Length ? new NumberValue(startNum) : ErrorValue.Value;
        if (startIdx >= withinText.Length) return ErrorValue.Value;

        var regex = SearchCache.GetOrAdd(findText, pattern =>
        {
            return new Regex(WildcardToRegexPattern(pattern, anchored: false), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        var match = regex.Match(withinText, startIdx);
        if (!match.Success) return ErrorValue.Value;
        return new NumberValue(match.Index + 1);
    }

    private static ScalarValue Mid(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue startError) return startError;
        if (args[2] is ErrorValue lengthError) return lengthError;
        var text    = ToText(args[0]);
        double rawStart = ToNumber(args[1]);
        double rawLen   = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawLen)) return ErrorValue.Value;
        int start   = (int)rawStart - 1; // 1-based → 0-based
        int numChars = (int)rawLen;
        if (start < 0 || numChars < 0) return ErrorValue.Value;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return TextResult(text.Substring(start, actualLen));
    }

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue repeatError) return repeatError;
        var text  = ToText(args[0]);
        var timesD = ToNumber(args[1]);
        if (!double.IsFinite(timesD)) return ErrorValue.Value;
        int times = (int)timesD;
        if (times < 0) return ErrorValue.Value;
        if ((long)text.Length * times > 32767) return ErrorValue.Value;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < times; i++) sb.Append(text);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is NumberValue nv) return nv;
        var text = ToText(args[0]).Trim();
        if (text.EndsWith('%') &&
            double.TryParse(text[..^1].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return new NumberValue(pct / 100.0);
        if (double.TryParse(text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
            return new NumberValue(d);
        return ErrorValue.Value;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Date & time
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Date(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double rawYear = ToNumber(args[0]);
        double rawMonth = ToNumber(args[1]);
        double rawDay = ToNumber(args[2]);
        if (!double.IsFinite(rawYear) || !double.IsFinite(rawMonth) || !double.IsFinite(rawDay))
            return ErrorValue.Num;
        int year  = (int)rawYear;
        int month = (int)rawMonth;
        int day   = (int)rawDay;
        if (year >= 0 && year < 1900)
            year += 1900;
        if (year < 0 || year > 9999) return ErrorValue.Num;
        try
        {
            var dt = new DateTime(year, 1, 1)
                .AddMonths(month - 1)
                .AddDays(day - 1);
            if (dt.ToOADate() < 0) return ErrorValue.Num;
            return new NumberValue(dt.ToOADate());
        }
        catch { return ErrorValue.Num; }
    }

    // OADate range supported by DateTime.FromOADate: -657435.0 to 2958465.0
    private static bool TryOADateToDateTime(ScalarValue v, out DateTime dt)
    {
        dt = default;
        var num = ToNumber(v);
        if (!double.IsFinite(num) || num < -657435.0 || num > 2958465.0)
            return false;
        dt = DateTime.FromOADate(num);
        return true;
    }

    private static DateTime OADateToDateTime(ScalarValue v) =>
        DateTime.FromOADate(ToNumber(v));

    private static ScalarValue Year(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Year) : ErrorValue.Num;
    }

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Month) : ErrorValue.Num;
    }

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Day) : ErrorValue.Num;
    }

    private static ScalarValue Hour(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Hour) : ErrorValue.Num;
    }

    private static ScalarValue Minute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Minute) : ErrorValue.Num;
    }

    private static ScalarValue Second(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return TryOADateToDateTime(args[0], out var dt) ? new NumberValue(dt.Second) : ErrorValue.Num;
    }

    private static ScalarValue Weekday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue returnTypeError) return returnTypeError;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawReturnType = args.Count > 1 ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        int dow = (int)dt.DayOfWeek; // 0=Sunday...6=Saturday
        return returnType switch
        {
            1 => new NumberValue(dow + 1),                     // Sun=1..Sat=7
            2 or 11 => new NumberValue(dow == 0 ? 7 : dow),    // Mon=1..Sun=7
            3 => new NumberValue(dow == 0 ? 6 : dow - 1),      // Mon=0..Sun=6
            >= 12 and <= 17 => new NumberValue(((dow - (returnType - 10) + 7) % 7) + 1),
            _ => ErrorValue.Num
        };
    }

    private static ScalarValue Edate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths)) return ErrorValue.Num;
        int months = (int)rawMonths;
        try
        {
            var result = dt.AddMonths(months);
            return new NumberValue(result.ToOADate());
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Datedif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var start)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var end)) return ErrorValue.Num;
        if (end < start) return ErrorValue.Num;
        var unit  = ToText(args[2]).ToUpperInvariant();

        return unit switch
        {
            "D"  => new NumberValue((end - start).Days),
            "M"  => new NumberValue(MonthDiff(start, end)),
            "Y"  => new NumberValue(YearDiff(start, end)),
            "YM" => new NumberValue((int)MonthDiff(start, end) % 12),
            "YD" => DateDifYD(start, end),
            // Guard: DateTime.DaysInMonth(0, 12) throws when end.Year==1 && end.Month==1
            "MD" => end.Year == 1 && end.Month == 1 ? ErrorValue.Num
                  : DateDifMD(start, end),
            _    => ErrorValue.Value
        };
    }

    private static double MonthDiff(DateTime start, DateTime end)
    {
        int months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day) months--;
        return months;
    }

    private static double YearDiff(DateTime start, DateTime end)
    {
        int years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && end.Day < start.Day))
            years--;
        return years;
    }

    // DATEDIF helpers that can throw ArgumentOutOfRangeException when start is a
    // leap day (Feb 29) and end.Year is not a leap year — catch → #NUM!
    private static ScalarValue DateDifYD(DateTime start, DateTime end)
    {
        try
        {
            var anchor = new DateTime(end.Year, start.Month, start.Day);
            return new NumberValue((end - (anchor > end ? anchor.AddYears(-1) : anchor)).Days);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    private static ScalarValue DateDifMD(DateTime start, DateTime end)
    {
        try
        {
            if (end.Day >= start.Day)
                return new NumberValue(end.Day - start.Day);
            int prevYear  = end.Month == 1 ? end.Year - 1 : end.Year;
            int prevMonth = end.Month == 1 ? 12 : end.Month - 1;
            return new NumberValue(end.Day + DateTime.DaysInMonth(prevYear, prevMonth) - start.Day);
        }
        catch (ArgumentOutOfRangeException) { return ErrorValue.Num; }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Math
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Mod(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var d = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(n - d * Math.Floor(n / d));
    }

    private static ScalarValue Power(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var number = ToNumber(args[0]);
        var power = ToNumber(args[1]);
        if (number == 0 && power < 0) return ErrorValue.DivByZero;
        var result = Math.Pow(number, power);
        if (double.IsNaN(result)) return ErrorValue.Num;
        if (double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Sqrt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n) || n < 0) return ErrorValue.Num;
        return new NumberValue(Math.Sqrt(n));
    }

    private static ScalarValue IntFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Floor(n));
    }

    private static ScalarValue Ceiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (n > 0 && sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Ceiling(n / sig) * sig);
    }

    private static ScalarValue Floor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        if (!double.IsFinite(n) || !double.IsFinite(sig)) return ErrorValue.Num;
        if (n > 0 && sig < 0) return ErrorValue.Num;
        return NumberResult(Math.Floor(n / sig) * sig);
    }

    private static ScalarValue Randbetween(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double db = ToNumber(args[0]);
        double dt = ToNumber(args[1]);
        if (!double.IsFinite(db) || !double.IsFinite(dt)) return ErrorValue.Num;
        if (!TryTruncateToLong(db, out long bottom) || !TryTruncateToLong(dt, out long top))
            return ErrorValue.Num;
        if (bottom > top) return ErrorValue.Num;
        // NextInt64(min, max) is [min, max) — add 1 to make it inclusive
        long range;
        try { range = checked(top - bottom + 1); }
        catch (OverflowException) { return ErrorValue.Num; }
        return new NumberValue(Random.Shared.NextInt64(bottom, bottom + range));
    }

    private static ScalarValue Sign(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n > 0 ? 1 : n < 0 ? -1 : 0);
    }

    private static ScalarValue Log(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var n    = ToNumber(args[0]);
        var base_ = args.Count > 1 ? ToNumber(args[1]) : 10.0;
        if (!double.IsFinite(n) || !double.IsFinite(base_)) return ErrorValue.Num;
        if (n <= 0 || base_ <= 0 || base_ == 1) return ErrorValue.Num;
        return NumberResult(Math.Log(n) / Math.Log(base_));
    }

    private static ScalarValue Ln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        if (n <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(n));
    }

    private static ScalarValue Exp(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var result = Math.Exp(ToNumber(args[0]));
        if (double.IsNaN(result) || double.IsInfinity(result)) return ErrorValue.Num;
        return new NumberValue(result);
    }

    private static ScalarValue Pi(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Math.PI);

    private static ScalarValue Fact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n) || n < 0 || n > 170) return ErrorValue.Num; // Excel limit; 171! overflows double
        int ni = (int)Math.Truncate(n);
        double result = 1;
        for (int i = 2; i <= ni; i++) result *= i;
        return new NumberValue(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Large(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderByDescending(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        var kD = ToNumber(args[1]);
        if (!double.IsFinite(kD)) return ErrorValue.Num;
        int k = (int)kD;
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderBy(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        if (!double.IsFinite(number)) return ErrorValue.Num;
        double rawOrder = args.Count > 2 ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawOrder)) return ErrorValue.Num;
        int order  = (int)rawOrder;

        var (nums, err) = CollectRangeNumbers(range);
        if (err is not null) return err;

        if (!nums!.Contains(number)) return ErrorValue.NA;

        int rank;
        if (order == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double variance = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return NumberResult(Math.Sqrt(variance));
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count == 0) return ErrorValue.Num;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return NumberResult(nums[mid]);
        return NumberResult((nums[mid - 1] + nums[mid]) / 2.0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-criteria aggregation
    // ═══════════════════════════════════════════════════════════════════

    // SUMIFS(sum_range, criteria_range1, criteria1, [criteria_range2, criteria2, ...])
    private static ScalarValue Sumifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue sumRangeError) return sumRangeError;
        if (args[0] is not RangeValue sumRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var sumFlat = sumRange.Flatten();
        int len = sumFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(sumRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (sumFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(sumFlat[i], out double value)) total += value;
            }
        }
        return NumberResult(total);
    }

    // COUNTIFS(criteria_range1, criteria1, ...)
    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        RangeValue? firstRange = null;
        for (int p = 0; p < pairCount; p++)
        {
            if (args[p * 2] is ErrorValue rangeError) return rangeError;
            if (args[p * 2] is not RangeValue cr) return ErrorValue.Value;
            firstRange ??= cr;
            if (!SameShape(firstRange, cr)) return ErrorValue.Value;
            if (args[p * 2 + 1] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[p * 2 + 1]);
        }
        int len = pairs[0].Flat.Count;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include) count++;
        }
        return new NumberValue(count);
    }

    // AVERAGEIFS(avg_range, criteria_range1, criteria1, ...)
    private static ScalarValue Averageifs2(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue avgRangeError) return avgRangeError;
        if (args[0] is not RangeValue avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var avgFlat = avgRange.Flatten();
        int len = avgFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is ErrorValue rangeError) return rangeError;
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
            if (!SameShape(avgRange, cr)) return ErrorValue.Value;
            if (args[2 + p * 2] is ErrorValue criteriaError) return criteriaError;
            pairs[p] = (cr.Flatten(), args[2 + p * 2]);
        }
        double total = 0;
        int count = 0;
        for (int i = 0; i < len; i++)
        {
            bool include = true;
            foreach (var (cf, criteria) in pairs)
            {
                if (!MatchesCriteria(i < cf.Count ? cf[i] : BlankValue.Instance, criteria))
                    { include = false; break; }
            }
            if (include)
            {
                if (avgFlat[i] is ErrorValue e) return e;
                if (TryCellNumber(avgFlat[i], out double value)) { total += value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return NumberResult(total / count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Modern lookup: XLOOKUP
    // ═══════════════════════════════════════════════════════════════════

    // XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])
    private static ScalarValue Xlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupArr) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;
        if (args[2] is not RangeValue returnArr) return ErrorValue.Value;
        var lookupIsVertical = lookupArr.ColCount == 1;
        var lookupIsHorizontal = lookupArr.RowCount == 1;
        if (!lookupIsVertical && !lookupIsHorizontal) return ErrorValue.Value;
        if (lookupIsVertical && returnArr.RowCount != lookupArr.RowCount) return ErrorValue.Value;
        if (lookupIsHorizontal && returnArr.ColCount != lookupArr.ColCount) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();

        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        ScalarValue ifNotFound = args.Count > 3 ? args[3] : ErrorValue.NA;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        if (args.Count > 5 && args[5] is ErrorValue e5) return e5;
        int matchMode = args.Count > 4 ? (int)ToNumber(args[4]) : 0; // 0=exact
        int searchMode = args.Count > 5 ? (int)ToNumber(args[5]) : 1; // 1=first-to-last
        if (matchMode is not (-1 or 0 or 1 or 2)) return ErrorValue.Value;
        if (searchMode is not (-2 or -1 or 1 or 2)) return ErrorValue.Value;

        var indices = Enumerable.Range(0, lookupFlat.Count).ToList();
        if (searchMode is -1 or -2) indices.Reverse();

        if (matchMode == 0)
        {
            // Exact match
            foreach (int i in indices)
                if (ScalarEquals(lookupFlat[i], lookupValue))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
        else if (matchMode == -1)
        {
            // Exact or next smaller
            int best = -1;
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) <= 0)
                    best = i;
            return best >= 0 ? XlookupReturnAt(returnArr, best, lookupIsVertical) : ifNotFound;
        }
        else
        {
            // Exact or next larger: return first element >= lookupValue
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) >= 0)
                    return XlookupReturnAt(returnArr, i, lookupIsVertical);
            return ifNotFound;
        }
    }

    private static ScalarValue XlookupReturnAt(RangeValue returnArr, int index, bool lookupIsVertical)
    {
        if (lookupIsVertical)
        {
            if (returnArr.ColCount == 1) return returnArr.Cells[index, 0];
            var row = new ScalarValue[1, returnArr.ColCount];
            for (int c = 0; c < returnArr.ColCount; c++)
                row[0, c] = returnArr.Cells[index, c];
            return new RangeValue(row);
        }

        if (returnArr.RowCount == 1) return returnArr.Cells[0, index];
        var col = new ScalarValue[returnArr.RowCount, 1];
        for (int r = 0; r < returnArr.RowCount; r++)
            col[r, 0] = returnArr.Cells[r, index];
        return new RangeValue(col);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-condition logic: IFS, SWITCH
    // ═══════════════════════════════════════════════════════════════════

    // IFS(condition1, value1, [condition2, value2, ...])
    private static ScalarValue Ifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count % 2 != 0) return ErrorValue.Value;
        for (int i = 0; i < args.Count - 1; i += 2)
        {
            if (args[i] is ErrorValue e) return e;
            if (ToBool(args[i])) return args[i + 1];
        }
        return ErrorValue.NA;
    }

    // SWITCH(expr, val1, result1, [val2, result2, ...], [default])
    private static ScalarValue Switch(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var expr = args[0];
        // args: expr, val1, result1, val2, result2, ..., [default]
        bool hasDefault = (args.Count - 1) % 2 == 1;
        int pairCount = (args.Count - 1) / 2;
        for (int i = 0; i < pairCount; i++)
        {
            if (ScalarEquals(expr, args[1 + i * 2]))
                return args[1 + i * 2 + 1];
        }
        return hasDefault ? args[^1] : ErrorValue.NA;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – IS functions
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Isblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is BlankValue);

    private static ScalarValue Isnumber(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is NumberValue or DateTimeValue);

    private static ScalarValue Istext(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is TextValue);

    private static ScalarValue Iserror(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is ErrorValue);

    private static ScalarValue Isna(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is ErrorValue e2 && e2.Code == "#N/A");

    private static ScalarValue Islogical(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is BoolValue);

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Reference helpers: ROW, COLUMN, ROWS, COLUMNS
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Row(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ErrorValue.Value; // no cell reference available without context
        if (args[0] is RangeValue rv) return new NumberValue(rv.StartRow);
        return ErrorValue.Value;
    }

    private static ScalarValue Column(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count == 0) return ErrorValue.Value;
        if (args[0] is RangeValue rv) return new NumberValue(rv.StartCol);
        return ErrorValue.Value;
    }

    private static ScalarValue Rows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue rv) return new NumberValue(rv.RowCount);
        return new NumberValue(1);
    }

    private static ScalarValue Columns(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue rv) return new NumberValue(rv.ColCount);
        return new NumberValue(1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Text: TEXTJOIN, EXACT, CODE, CHAR
    // ═══════════════════════════════════════════════════════════════════

    // TEXTJOIN(delimiter, ignore_empty, text1, [text2, ...])
    private static ScalarValue Textjoin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 3) return ErrorValue.Value;
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var delimiter = ToText(args[0]);
        bool ignoreEmpty = ToBool(args[1]);
        var parts = new List<string>();
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e) return e;
            var t = ToText(args[i]);
            if (ignoreEmpty && t.Length == 0) continue;
            parts.Add(t);
        }
        var result = string.Join(delimiter, parts);
        return result.Length > 32767 ? ErrorValue.Value : new TextValue(result);
    }

    private static ScalarValue Exact(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return new BoolValue(string.Equals(ToText(args[0]), ToText(args[1]), StringComparison.Ordinal));
    }

    private static ScalarValue Code(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (text.Length == 0) return ErrorValue.Value;
        return new NumberValue(text[0]);
    }

    private static ScalarValue Char(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int code = (int)n;
        if (code <= 0 || code > 255) return ErrorValue.Value;
        return new TextValue(((char)code).ToString());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Count: COUNTBLANK
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Countblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        int count = range.Flatten().Count(v => v is BlankValue || v is TextValue { Value.Length: 0 });
        return new NumberValue(count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Misc: CHOOSE, SUMPRODUCT, ROUNDDOWN, ROUNDUP, TRUNC
    // ═══════════════════════════════════════════════════════════════════

    // CHOOSE(index, val1, val2, ...)
    private static ScalarValue Choose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int idx = (int)n;
        if (idx < 1 || idx >= args.Count) return ErrorValue.Value;
        return args[idx];
    }

    // SUMPRODUCT(array1, [array2, ...])
    private static ScalarValue Sumproduct(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var arrays = new List<IReadOnlyList<ScalarValue>>();
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is RangeValue rv) arrays.Add(rv.Flatten());
            else if (a is NumberValue nv) arrays.Add([nv]);
            else arrays.Add([a]);
        }
        if (arrays.Count == 0) return new NumberValue(0);
        int len = arrays[0].Count;
        for (int k = 1; k < arrays.Count; k++)
            if (arrays[k].Count != len) return ErrorValue.Value;
        double total = 0;
        for (int i = 0; i < len; i++)
        {
            double product = 1;
            for (int k = 0; k < arrays.Count; k++)
            {
                var v = arrays[k][i];
                if (v is ErrorValue ev) return ev;
                product *= TryCellNumber(v, out double value) ? value : 0;
                if (!double.IsFinite(product)) return ErrorValue.Num;
            }
            total += product;
            if (!double.IsFinite(total)) return ErrorValue.Num;
        }
        return NumberResult(total);
    }

    private static ScalarValue Rounddown(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        double factor = Math.Pow(10, digits);
        return NumberResult((n >= 0 ? Math.Floor(n * factor) : Math.Ceiling(n * factor)) / factor);
    }

    private static ScalarValue Roundup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var rawDigits = ToNumber(args[1]);
        if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
        int digits = (int)Math.Truncate(rawDigits);
        double factor = Math.Pow(10, digits);
        return NumberResult((n >= 0 ? Math.Ceiling(n * factor) : Math.Floor(n * factor)) / factor);
    }

    private static ScalarValue Trunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        int digits = 0;
        if (args.Count > 1)
        {
            if (args[1] is ErrorValue e1) return e1;
            var rawDigits = ToNumber(args[1]);
            if (!double.IsFinite(rawDigits)) return ErrorValue.Num;
            digits = (int)Math.Truncate(rawDigits);
        }
        double factor = Math.Pow(10, digits);
        return NumberResult(Math.Truncate(n * factor) / factor);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper: compare two ScalarValues (returns <0, 0, >0)
    // ═══════════════════════════════════════════════════════════════════

    private static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        var aIsNumber = TryCellNumber(a, out double aNumber);
        var bIsNumber = TryCellNumber(b, out double bNumber);
        if (aIsNumber && bIsNumber)
            return aNumber.CompareTo(bNumber);
        if (a is TextValue ta && b is TextValue tb)
            return string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        // Mixed: numbers < text
        return (aIsNumber ? 0 : 1) - (bIsNumber ? 0 : 1);
    }

    internal static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is BlankValue && b is BlankValue) return true;
        // Blank coerces to 0 against numbers/dates, "" against text
        if (a is BlankValue) a = b is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (b is BlankValue) b = a is TextValue ? new TextValue("") : (ScalarValue)new NumberValue(0);
        if (TryCellNumber(a, out double aNumber) && TryCellNumber(b, out double bNumber))
            return aNumber == bNumber;
        if (a is TextValue ta && b is TextValue tb)
            return string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value == bb.Value;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Math / Trig
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Sin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Sin(n));
    }

    private static ScalarValue Cos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Cos(n));
    }

    private static ScalarValue Tan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Tan(n));
    }

    private static ScalarValue Asin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Asin(n));
    }

    private static ScalarValue Acos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (n < -1 || n > 1) return ErrorValue.Num;
        return new NumberValue(Math.Acos(n));
    }

    private static ScalarValue Atan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(Math.Atan(n));
    }

    // ATAN2(x_num, y_num) – matches Excel argument order (x first, then y)
    private static ScalarValue Atan2Func(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double y = ToNumber(args[1]);
        if (!double.IsFinite(x) || !double.IsFinite(y)) return ErrorValue.Num;
        if (x == 0 && y == 0) return ErrorValue.DivByZero;
        return new NumberValue(Math.Atan2(y, x));
    }

    private static ScalarValue Degrees(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n * 180.0 / Math.PI);
    }

    private static ScalarValue Radians(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (!double.IsFinite(n)) return ErrorValue.Num;
        return new NumberValue(n * Math.PI / 180.0);
    }

    private static ScalarValue Product(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double result = 1.0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) result *= value;
                else if (refError is not null) return refError;
                continue;
            }
            if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return ErrorValue.Value;
                if (!double.IsFinite(value)) return ErrorValue.Num;
                result *= value;
            }
            else if (a is NumberValue or BoolValue or DateTimeValue) result *= ToNumber(a);
        }
        return NumberResult(result);
    }

    private static ScalarValue Quotient(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        double d = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(d)) return ErrorValue.Num;
        if (d == 0) return ErrorValue.DivByZero;
        return NumberResult(Math.Truncate(n / d));
    }

    private static ScalarValue Gcd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 0;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    result = GcdCalc(result, (long)value);
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            result = GcdCalc(result, n);
        }
        return new NumberValue(result);
    }

    private static long GcdCalc(long a, long b)
    {
        while (b != 0) { long t = b; b = a % b; a = t; }
        return a;
    }

    private static ScalarValue Lcm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        long result = 1;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError))
                {
                    if (!double.IsFinite(value) || value < 0 || value > long.MaxValue) return ErrorValue.Num;
                    long referencedNumber = (long)value;
                    if (referencedNumber == 0) return new NumberValue(0);
                    long referencedGcd = GcdCalc(result, referencedNumber);
                    if (result / referencedGcd > long.MaxValue / referencedNumber) return ErrorValue.Num;
                    result = result / referencedGcd * referencedNumber;
                }
                else if (refError is not null) return refError;
                continue;
            }
            double d = ToNumber(a);
            if (!double.IsFinite(d) || d < 0 || d > long.MaxValue) return ErrorValue.Num;
            long n = (long)d;
            if (n == 0) return new NumberValue(0);
            long g = GcdCalc(result, n);
            // Check overflow before multiplying
            if (result / g > long.MaxValue / n) return ErrorValue.Num;
            result = result / g * n;
        }
        return new NumberValue(result);
    }

    private static ScalarValue Mround(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        double m = ToNumber(args[1]);
        if (!double.IsFinite(n) || !double.IsFinite(m)) return ErrorValue.Num;
        if (m == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (m < 0)) return ErrorValue.Num;
        return NumberResult(Math.Round(n / m, MidpointRounding.AwayFromZero) * m);
    }

    private static ScalarValue Combin(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double dn = ToNumber(args[0]); double dk = ToNumber(args[1]);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn; int k = (int)dk;
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        if (k > n - k) k = n - k;
        double result = 1;
        for (int i = 0; i < k; i++)
            result = result * (n - i) / (i + 1);
        return NumberResult(Math.Round(result));
    }

    private static ScalarValue Permut(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double dn = ToNumber(args[0]); double dk = ToNumber(args[1]);
        if (!double.IsFinite(dn) || !double.IsFinite(dk)) return ErrorValue.Num;
        if (dn < 0 || dn > int.MaxValue || dk < 0 || dk > int.MaxValue) return ErrorValue.Num;
        int n = (int)dn; int k = (int)dk;
        if (n < 0 || k < 0 || k > n) return ErrorValue.Num;
        double result = 1;
        for (int i = 0; i < k; i++)
            result *= (n - i);
        return NumberResult(result);
    }

    private static ScalarValue Odd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (!double.IsFinite(n) || Math.Abs(n) > int.MaxValue) return ErrorValue.Num;
        if (n == 0) return new NumberValue(1);
        int sign = n > 0 ? 1 : -1;
        int abs = (int)Math.Ceiling(Math.Abs(n));
        if (abs % 2 == 0) abs++;
        return new NumberValue(sign * abs);
    }

    private static ScalarValue Even(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double n = ToNumber(args[0]);
        if (!double.IsFinite(n) || Math.Abs(n) > int.MaxValue) return ErrorValue.Num;
        if (n == 0) return new NumberValue(0);
        int sign = n > 0 ? 1 : -1;
        int abs = (int)Math.Ceiling(Math.Abs(n));
        if (abs % 2 != 0) abs++;
        return new NumberValue(sign * abs);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Date / Time
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue TimeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double h = ToNumber(args[0]), m = ToNumber(args[1]), s = ToNumber(args[2]);
        if (!double.IsFinite(h) || !double.IsFinite(m) || !double.IsFinite(s)) return ErrorValue.Num;
        if (h < 0 || m < 0 || s < 0) return ErrorValue.Num;
        if (h > 32767 || m > 32767 || s > 32767) return ErrorValue.Num;
        double frac = (h * 3600 + m * 60 + s) / 86400.0;
        return new NumberValue(frac - Math.Floor(frac));
    }

    private static ScalarValue Timevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts.Days == 0)
            return new NumberValue(ts.TotalDays);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(dt.TimeOfDay.TotalDays);
        return ErrorValue.Value;
    }

    private static ScalarValue Datevalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]);
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return new NumberValue(Math.Floor(dt.ToOADate()));
        return ErrorValue.Value;
    }

    private static ScalarValue Eomonth(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawMonths = ToNumber(args[1]);
        if (!double.IsFinite(rawMonths)) return ErrorValue.Num;
        int months = (int)rawMonths;
        try
        {
            var target = dt.AddMonths(months + 1);
            var eomonth = new DateTime(target.Year, target.Month, 1).AddDays(-1);
            return new NumberValue(eomonth.ToOADate());
        }
        catch { return ErrorValue.Num; }
    }

    private static ScalarValue Weeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        double rawReturnType = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        if (!double.IsFinite(rawReturnType)) return ErrorValue.Num;
        int returnType = (int)rawReturnType;
        if (returnType == 21)
            return new NumberValue(System.Globalization.ISOWeek.GetWeekOfYear(dt));

        DayOfWeek firstDay = returnType switch
        {
            1 or 17 => DayOfWeek.Sunday,
            2 or 11 => DayOfWeek.Monday,
            12 => DayOfWeek.Tuesday,
            13 => DayOfWeek.Wednesday,
            14 => DayOfWeek.Thursday,
            15 => DayOfWeek.Friday,
            16 => DayOfWeek.Saturday,
            _ => (DayOfWeek)(-1)
        };
        if ((int)firstDay < 0) return ErrorValue.Num;
        var jan1 = new DateTime(dt.Year, 1, 1);
        int jan1Dow = ((int)jan1.DayOfWeek - (int)firstDay + 7) % 7;
        int dayOfYear = (dt - jan1).Days;
        return new NumberValue((dayOfYear + jan1Dow) / 7 + 1);
    }

    private static ScalarValue Isoweeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (!TryOADateToDateTime(args[0], out var dt)) return ErrorValue.Num;
        return new NumberValue(System.Globalization.ISOWeek.GetWeekOfYear(dt));
    }

    private static ScalarValue Workday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var current)) return ErrorValue.Num;
        double rawDays = ToNumber(args[1]);
        if (!double.IsFinite(rawDays)) return ErrorValue.Num;
        if (rawDays < int.MinValue + 1 || rawDays > int.MaxValue) return ErrorValue.Num;
        int days = (int)rawDays;
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (TryCellNumber(v, out double holidaySerial))
                    holidays.Add(DateTime.FromOADate(holidaySerial).Date);
        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
        // Skip full weeks when there are no holidays — 5 workdays = 7 calendar days
        if (remaining > 5 && holidays.Count == 0)
        {
            int fullWeeks = (remaining - 1) / 5; // keep ≥5 left so day-of-week boundary is handled correctly
            current = current.AddDays(sign * fullWeeks * 7);
            remaining -= fullWeeks * 5;
        }
        while (remaining > 0)
        {
            current = current.AddDays(sign);
            if (current.DayOfWeek != DayOfWeek.Saturday &&
                current.DayOfWeek != DayOfWeek.Sunday &&
                !holidays.Contains(current.Date))
                remaining--;
        }
        return new NumberValue(current.ToOADate());
    }

    private static ScalarValue Networkdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (TryCellNumber(v, out double holidaySerial))
                    holidays.Add(DateTime.FromOADate(holidaySerial).Date);
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt   : startDt;
        int count = CountWeekdaysInclusive(lo, hi);
        foreach (var h in holidays)
            if (h >= lo && h <= hi && h.DayOfWeek != DayOfWeek.Saturday && h.DayOfWeek != DayOfWeek.Sunday)
                count--;
        return new NumberValue(sign * count);
    }

    private static int CountWeekdaysInclusive(DateTime lo, DateTime hi)
    {
        int totalDays = (int)(hi - lo).TotalDays + 1;
        int fullWeeks = totalDays / 7;
        int count = fullWeeks * 5;
        int startDow = (int)lo.DayOfWeek; // 0=Sun, 1=Mon, …, 6=Sat
        for (int i = 0; i < totalDays % 7; i++)
        {
            int dow = (startDow + i) % 7;
            if (dow != 0 && dow != 6) count++;
        }
        return count;
    }

    private static ScalarValue Days(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var endDt))   return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var startDt)) return ErrorValue.Num;
        return new NumberValue((endDt - startDt).Days);
    }

    private static ScalarValue Days360(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        bool european = args.Count > 2 && args[2] is not BlankValue && ToNumber(args[2]) != 0;
        double days = european ? Days30E360(startDt, endDt) : Days30US360(startDt, endDt);
        return new NumberValue(Math.Truncate(days));
    }

    private static ScalarValue Yearfrac(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (!TryOADateToDateTime(args[0], out var startRaw)) return ErrorValue.Num;
        if (!TryOADateToDateTime(args[1], out var endRaw))   return ErrorValue.Num;
        var startDt = startRaw.Date;
        var endDt   = endRaw.Date;
        double rawBasis = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0;
        if (!double.IsFinite(rawBasis)) return ErrorValue.Num;
        int basis = (int)rawBasis;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        double totalDays = (endDt - startDt).TotalDays;
        double result = basis switch
        {
            1 => totalDays / ActualActualDenominator(startDt, endDt),
            2 => totalDays / 360.0,
            3 => totalDays / 365.0,
            4 => Days30E360(startDt, endDt) / 360.0,
            _ => Days30US360(startDt, endDt) / 360.0
        };
        return new NumberValue(result);
    }

    private static double ActualActualDenominator(DateTime start, DateTime end)
    {
        if (start.Year == end.Year)
            return DateTime.IsLeapYear(start.Year) ? 366.0 : 365.0;
        double total = 0;
        for (int y = start.Year; y <= end.Year; y++)
            total += DateTime.IsLeapYear(y) ? 366.0 : 365.0;
        return total / (end.Year - start.Year + 1);
    }

    private static double Days30US360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31 && dd1 == 30) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    private static double Days30E360(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31) dd2 = 30;
        return 360.0 * (y2 - y1) + 30.0 * (m2 - m1) + (dd2 - dd1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Statistical
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue VarS(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count < 2) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return NumberResult(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? NumberResult(Math.Sqrt(nv.Value)) : r;
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectNumbers(IReadOnlyList<ScalarValue> args, int start = 0)
    {
        var list = new List<double>();
        for (int i = start; i < args.Count; i++)
        {
            var a = args[i];
            if (a is ErrorValue e) return (null, e);
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedNumber(referenced, out double value, out var refError)) list.Add(value);
                else if (refError is not null) return (null, refError);
            }
            else if (a is NumberValue nv) list.Add(nv.Value);
            else if (a is BoolValue bv) list.Add(bv.Value ? 1.0 : 0.0);
            else if (a is DateTimeValue dt) list.Add(dt.Value);
            else if (a is DirectTextLiteralValue direct)
            {
                if (!TryDirectTextNumber(direct, out double value)) return (null, ErrorValue.Value);
                list.Add(value);
            }
        }
        return (list, null);
    }

    private static (List<double>? Nums, ErrorValue? Error) CollectRangeNumbers(RangeValue range)
    {
        var list = new List<double>();
        foreach (var value in range.Flatten())
        {
            if (value is ErrorValue e) return (null, e);
            if (value is NumberValue n) list.Add(n.Value);
            else if (value is DateTimeValue d) list.Add(d.Value);
        }
        return (list, null);
    }

    private static ScalarValue PercentileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        double rank = k * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue PercentileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (!double.IsFinite(k)) return ErrorValue.Num;
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return ErrorValue.Num;
        double rank = k * (n + 1) - 1;
        if (rank < 0 || rank >= n) return ErrorValue.Num;
        int lo = (int)rank;
        if (lo >= n - 1) return NumberResult(sorted[n - 1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double rawQuart = ToNumber(args[1]);
        if (!double.IsFinite(rawQuart)) return ErrorValue.Num;
        int quart = (int)rawQuart;
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return NumberResult(sorted[0]);
        if (quart == 4) return NumberResult(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return NumberResult(sorted[^1]);
        return NumberResult(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue Geomean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        var logSum = 0.0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            logSum += Math.Log(value);
        }
        return NumberResult(Math.Exp(logSum / nums.Count));
    }

    private static ScalarValue Harmean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.Num;

        double recSum = 0;
        foreach (var value in nums)
        {
            if (value <= 0) return ErrorValue.Num;
            recSum += 1.0 / value;
        }
        return NumberResult(nums.Count / recSum);
    }

    private static ScalarValue Avedev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return NumberResult(nums.Average(x => Math.Abs(x - mean)));
    }

    private static ScalarValue ModeSngl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;

        var freq = new Dictionary<double, int>();
        foreach (var value in nums!)
            freq[value] = freq.GetValueOrDefault(value) + 1;

        if (freq.Count == 0) return ErrorValue.NA;
        int maxFreq = freq.Values.Max();
        if (maxFreq < 2) return ErrorValue.NA;
        // Preserve first-occurrence order for tie-breaking (matches Excel)
        foreach (var key in freq.Keys)
            if (freq[key] == maxFreq) return NumberResult(key);
        return ErrorValue.NA;
    }

    private static ScalarValue PercentrankInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[1]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        double rawSig = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 3;
        if (!double.IsFinite(rawSig)) return ErrorValue.Num;
        int sig = (int)rawSig;
        if (sig < 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(v => v).ToList();
        int n = sorted.Count;
        if (n == 0 || x < sorted[0] || x > sorted[^1]) return ErrorValue.NA;
        int below = sorted.Count(v => v < x);
        int equal = sorted.Count(v => v == x);
        if (equal == 0) return ErrorValue.NA;
        double pctRank = n == 1 ? 0.0 : (double)below / (n - 1);
        double factor = Math.Pow(10, sig);
        if (!double.IsFinite(factor)) return ErrorValue.Num;
        return NumberResult(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[0] is not RangeValue rv1) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue rv2) return ErrorValue.Value;
        var (xs, xErr) = CollectRangeNumbers(rv1);
        if (xErr is not null) return xErr;
        var (ys, yErr) = CollectRangeNumbers(rv2);
        if (yErr is not null) return yErr;
        int n = Math.Min(xs!.Count, ys!.Count);
        if (n < 2) return ErrorValue.DivByZero;
        double xMean = xs.Take(n).Average();
        double yMean = ys.Take(n).Average();
        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean, dy = ys[i] - yMean;
            cov  += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }
        if (varX == 0 || varY == 0) return ErrorValue.DivByZero;
        return NumberResult(cov / Math.Sqrt(varX * varY));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue knownY) return ErrorValue.Value;
        if (args[2] is ErrorValue e2) return e2;
        if (args[2] is not RangeValue knownX) return ErrorValue.Value;
        double x    = ToNumber(args[0]);
        if (!double.IsFinite(x)) return ErrorValue.Num;
        var (ys, yErr) = CollectRangeNumbers(knownY);
        if (yErr is not null) return yErr;
        var (xs, xErr) = CollectRangeNumbers(knownX);
        if (xErr is not null) return xErr;
        int n = Math.Min(xs!.Count, ys!.Count);
        if (n < 2) return ErrorValue.NA;
        double xMean = xs.Take(n).Average();
        double yMean = ys.Take(n).Average();
        double sXX = 0, sXY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = xs[i] - xMean;
            sXX += dx * dx;
            sXY += dx * (ys[i] - yMean);
        }
        if (sXX == 0) return ErrorValue.DivByZero;
        double b = sXY / sXX;
        double a = yMean - b * xMean;
        return NumberResult(a + b * x);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Financial
    // ═══════════════════════════════════════════════════════════════════

    private static bool IsValidPaymentType(double type) =>
        double.IsFinite(type) && (type == 0 || type == 1);

    private static ScalarValue Pmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue < int.MinValue || nperValue > int.MaxValue) return ErrorValue.Num;
        int nper = (int)nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-(pv + fv) / nper);
        double rn  = Math.Pow(1 + rate, nper);
        double pmt = -(pv * rn + fv) * rate / ((1 + rate * type) * (rn - 1));
        return NumberResult(pmt);
    }

    private static ScalarValue Pv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue < int.MinValue || nperValue > int.MaxValue) return ErrorValue.Num;
        int nper = (int)nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pmt * nper - fv);
        double rn = Math.Pow(1 + rate, nper);
        double pv = (-pmt * (1 + rate * type) * (rn - 1) / rate - fv) / rn;
        return NumberResult(pv);
    }

    private static ScalarValue Fv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double pv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue < int.MinValue || nperValue > int.MaxValue) return ErrorValue.Num;
        int nper = (int)nperValue;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pv - pmt * nper);
        double rn = Math.Pow(1 + rate, nper);
        return NumberResult(-pv * rn - pmt * (1 + rate * type) * (rn - 1) / rate);
    }

    private static ScalarValue Nper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double pmt  = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (!double.IsFinite(rate) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (Math.Abs(rate) < 1e-10)
        {
            if (Math.Abs(pmt) < 1e-10) return ErrorValue.DivByZero;
            return NumberResult(-(pv + fv) / pmt);
        }
        double pmtAdj = pmt * (1 + rate * type);
        double ratio  = (pmtAdj - fv * rate) / (pmtAdj + pv * rate);
        if (ratio <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(ratio) / Math.Log(1 + rate));
    }

    private static ScalarValue Rate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nperValue = ToNumber(args[0]);
        double pmt   = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double fv    = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double guess = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0.1;
        if (!double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) || !double.IsFinite(guess))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue < int.MinValue || nperValue > int.MaxValue) return ErrorValue.Num;
        int nper = (int)nperValue;
        double r = guess;
        for (int i = 0; i < 100; i++)
        {
            double rn   = Math.Pow(1 + r, nper);
            double rn1  = nper * Math.Pow(1 + r, nper - 1);
            double f, df;
            if (Math.Abs(r) < 1e-10)
            {
                f  = pv + pmt * nper + fv;
                df = pv * nper + pmt * nper * (nper - 1) / 2.0;
            }
            else
            {
                f  = pv * rn + pmt * (1 + r * type) * (rn - 1) / r + fv;
                df = pv * rn1
                   + pmt * type * (rn - 1) / r
                   + pmt * (1 + r * type) * (rn1 * r - (rn - 1)) / (r * r);
            }
            if (Math.Abs(df) < 1e-15) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Npv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double rate   = ToNumber(args[0]);
        if (!double.IsFinite(rate)) return ErrorValue.Num;
        var (values, err) = CollectNumbers(args, start: 1);
        if (err is not null) return err;

        double result = 0;
        for (int i = 0; i < values!.Count; i++)
            result += values[i] / Math.Pow(1 + rate, i + 1);
        return NumberResult(result);
    }

    private static ScalarValue Irr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        if (args[0] is not RangeValue valRange) return ErrorValue.Value;
        double guess = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 0.1;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cashflows = values!;
        if (cashflows.Count == 0) return ErrorValue.Value;
        double r = guess;
        for (int iter = 0; iter < 100; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cashflows.Count; i++)
            {
                double denom = Math.Pow(1 + r, i);
                f  += cashflows[i] / denom;
                if (i > 0) df -= i * cashflows[i] / (denom * (1 + r));
            }
            if (Math.Abs(f) < 1e-10) break;
            if (Math.Abs(df) < 1e-15) return ErrorValue.Num;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Sln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life))
            return ErrorValue.Num;
        if (life == 0) return ErrorValue.DivByZero;
        return NumberResult((cost - salvage) / life);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Logical / Text
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Xor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        bool result = false;
        bool hadUsableValue = false;
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            if (a is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out var refError))
                {
                    hadUsableValue = true;
                    result ^= value;
                }
                else if (refError is not null) return refError;
                continue;
            }
            if (a is TextValue or BlankValue) return ErrorValue.Value;
            hadUsableValue = true;
            result ^= ToBool(a);
        }
        return hadUsableValue ? new BoolValue(result) : ErrorValue.Value;
    }

    private static ScalarValue TrueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(true);

    private static ScalarValue FalseFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(false);

    private static ScalarValue Iseven(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double d = Math.Truncate(ToNumber(args[0]));
        if (!double.IsFinite(d) || d > long.MaxValue || d < long.MinValue) return ErrorValue.Num;
        return new BoolValue((long)d % 2 == 0);
    }

    private static ScalarValue Isodd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double d = Math.Truncate(ToNumber(args[0]));
        if (!double.IsFinite(d) || d > long.MaxValue || d < long.MinValue) return ErrorValue.Num;
        return new BoolValue((long)d % 2 != 0);
    }

    private static ScalarValue Replace(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        if (args[3] is ErrorValue e3) return e3;

        var text = ToText(args[0]);
        double rawStart = ToNumber(args[1]);
        double rawNumChars = ToNumber(args[2]);
        if (!double.IsFinite(rawStart) || !double.IsFinite(rawNumChars)) return ErrorValue.Value;

        int startNum = (int)rawStart;
        int numChars = (int)rawNumChars;
        if (startNum < 1 || numChars < 0) return ErrorValue.Value;

        int start = Math.Min(startNum - 1, text.Length);
        var newText = ToText(args[3]);
        int end = Math.Min(start + numChars, text.Length);
        return TextResult(text[..start] + newText + text[end..]);
    }

    private static ScalarValue Concatenate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            sb.Append(ToText(a));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return args[0] is TextValue t ? TextResult(t.Value) : new TextValue("");
    }

    private static ScalarValue Fixed(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double n = ToNumber(args[0]);
        int dec = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 2;
        bool noCommas = args.Count > 2 && args[2] is not BlankValue && ToBool(args[2]);
        return TextResult(FormatRoundedNumber(n, dec, useCommas: !noCommas));
    }

    private static ScalarValue Clean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var sb = new System.Text.StringBuilder();
        foreach (char c in ToText(args[0]))
            if (c >= 32) sb.Append(c);
        return TextResult(sb.ToString());
    }

    private static ScalarValue Dollar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        int dec = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 2;
        return TextResult("$" + FormatRoundedNumber(n, dec, useCommas: true));
    }

    private static string FormatRoundedNumber(double value, int decimals, bool useCommas)
    {
        if (!double.IsFinite(value)) throw new FormulaEvalException("#NUM!", "Invalid number");
        if (decimals > 32767) throw new FormulaEvalException("#VALUE!", "Formatted text exceeds Excel cell text limit");

        double rounded = decimals is >= 0 and <= 15 ? RoundWithExcelDigits(value, decimals) : value;
        if (decimals < 0) rounded = RoundWithExcelDigits(value, decimals);
        int displayDecimals = Math.Max(0, decimals);
        string format = (useCommas ? "N" : "F") + displayDecimals;
        return rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Reference
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Indirect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var refText = ToText(args[0]).Trim();
        bool useA1 = args.Count < 2 || args[1] is BlankValue || ToBool(args[1]);
        string? sheetName = null;
        int bangIdx = refText.IndexOf('!');
        if (bangIdx >= 0)
        {
            sheetName = refText[..bangIdx].Trim('\'');
            refText   = refText[(bangIdx + 1)..];
        }
        if (useA1
                ? !TryParseA1Ref(refText, out uint row, out uint col)
                : !TryParseR1C1Ref(refText, out row, out col))
            return ErrorValue.Ref;
        return sheetName is not null
            ? ctx.GetCellValue(sheetName, row, col)
            : ctx.GetCellValue(row, col);
    }

    private static bool TryParseA1Ref(string cellRef, out uint row, out uint col)
    {
        row = 0; col = 0;
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
        if (i == 0 || i >= cellRef.Length) return false;
        string colStr = cellRef[..i].ToUpperInvariant();
        if (!uint.TryParse(cellRef[i..], out row)) return false;
        col = CellAddress.ColumnNameToNumber(colStr);
        return row > 0 && row <= CellAddress.MaxRow && col > 0 && col <= CellAddress.MaxCol;
    }

    private static bool TryParseR1C1Ref(string cellRef, out uint row, out uint col)
    {
        row = 0; col = 0;
        var match = Regex.Match(cellRef, @"^R(\d+)C(\d+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        if (!uint.TryParse(match.Groups[1].Value, out row)) return false;
        if (!uint.TryParse(match.Groups[2].Value, out col)) return false;
        return row > 0 && row <= CellAddress.MaxRow && col > 0 && col <= CellAddress.MaxCol;
    }

    private static ScalarValue Address(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        if (args.Count > 4 && args[4] is ErrorValue e4) return e4;
        double dRow = ToNumber(args[0]); double dCol = ToNumber(args[1]);
        if (!double.IsFinite(dRow) || !double.IsFinite(dCol)) return ErrorValue.Num;
        int rowNum = (int)dRow; int colNum = (int)dCol;
        if (rowNum < 1 || rowNum > (int)CellAddress.MaxRow ||
            colNum < 1 || colNum > (int)CellAddress.MaxCol) return ErrorValue.Value;
        int absNum = args.Count > 2 && args[2] is not BlankValue ? (int)ToNumber(args[2]) : 1;
        if (absNum is not (1 or 2 or 3 or 4)) return ErrorValue.Value;
        bool useA1 = args.Count < 4 || args[3] is BlankValue || ToBool(args[3]);
        string? sheetText = args.Count > 4 && args[4] is not BlankValue ? ToText(args[4]) : null;
        string colLetter = CellAddress.NumberToColumnName((uint)colNum);
        bool colAbs = absNum is 1 or 3;
        bool rowAbs = absNum is 1 or 2;
        string addr = useA1
            ? $"{(colAbs ? "$" : "")}{colLetter}{(rowAbs ? "$" : "")}{rowNum}"
            : $"{(rowAbs ? $"R{rowNum}" : $"R[{rowNum}]")}{(colAbs ? $"C{colNum}" : $"C[{colNum}]")}";
        if (!string.IsNullOrEmpty(sheetText))
            addr = $"'{sheetText}'!{addr}";
        return new TextValue(addr);
    }

    private static ScalarValue Lookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue e1) return e1;
        if (args[1] is not RangeValue lookupVec) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var lookupFlat = lookupVec.Flatten();
        var resultFlat = args.Count > 2 && args[2] is RangeValue rv
            ? rv.Flatten()
            : lookupFlat;
        var lookupVal = args[0];
        int matchIdx = -1;
        for (int i = 0; i < lookupFlat.Count; i++)
            if (CompareScalar(lookupFlat[i], lookupVal) <= 0)
                matchIdx = i;
        if (matchIdx < 0) return ErrorValue.NA;
        return matchIdx < resultFlat.Count ? resultFlat[matchIdx] : ErrorValue.NA;
    }

    private static ScalarValue NFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        args[0] switch
        {
            NumberValue nv   => nv,
            DateTimeValue dt => new NumberValue(dt.Value),
            BoolValue bv     => new NumberValue(bv.Value ? 1 : 0),
            ErrorValue ev    => ev,
            _                => new NumberValue(0)
        };

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4b  –  Dynamic arrays
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Sequence(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        double rawRows = ToNumber(args[0]);
        double rawCols = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double start = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        double step  = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        if (!double.IsFinite(rawRows) || !double.IsFinite(rawCols)) return ErrorValue.Value;
        if (!double.IsFinite(start) || !double.IsFinite(step)) return ErrorValue.Num;
        int rows = (int)rawRows;
        int cols = (int)rawCols;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        var cells = new ScalarValue[rows, cols];
        double val = start;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (!double.IsFinite(val)) return ErrorValue.Num;
                cells[r, c] = new NumberValue(val);
                val += step;
            }
        return new RangeValue(cells);
    }

    private static ScalarValue Filter(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue includeError) return includeError;
        if (args[1] is not RangeValue include) return ErrorValue.Value;
        var ifEmpty = args.Count > 2 ? args[2] : new TextValue("");

        if (include.ColCount == 1 && include.RowCount == arr.RowCount)
            return FilterRows(arr, include, ifEmpty);

        if (include.RowCount == 1 && include.ColCount == arr.ColCount)
            return FilterColumns(arr, include, ifEmpty);

        return ErrorValue.Value;
    }

    private static ScalarValue FilterRows(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedRows = new List<int>();
        for (int i = 0; i < arr.RowCount; i++)
        {
            var v = include.Cells[i, 0];
            if (v is ErrorValue e) return e;
            if (IsFilterIncluded(v)) matchedRows.Add(i);
        }

        if (matchedRows.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[matchedRows.Count, arr.ColCount];
        for (int ri = 0; ri < matchedRows.Count; ri++)
            for (int c = 0; c < arr.ColCount; c++)
                result[ri, c] = arr.Cells[matchedRows[ri], c];
        return new RangeValue(result);
    }

    private static ScalarValue FilterColumns(RangeValue arr, RangeValue include, ScalarValue ifEmpty)
    {
        var matchedCols = new List<int>();
        for (int c = 0; c < arr.ColCount; c++)
        {
            var v = include.Cells[0, c];
            if (v is ErrorValue e) return e;
            if (IsFilterIncluded(v)) matchedCols.Add(c);
        }

        if (matchedCols.Count == 0)
            return FilterEmptyResult(ifEmpty);

        var result = new ScalarValue[arr.RowCount, matchedCols.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int ci = 0; ci < matchedCols.Count; ci++)
                result[r, ci] = arr.Cells[r, matchedCols[ci]];
        return new RangeValue(result);
    }

    private static bool IsFilterIncluded(ScalarValue value) =>
        value is BoolValue { Value: true } || (TryCellNumber(value, out double number) && number != 0);

    private static ScalarValue FilterEmptyResult(ScalarValue ifEmpty) =>
        ifEmpty is RangeValue rvEmpty
            ? rvEmpty
            : new RangeValue(new ScalarValue[1, 1] { { ifEmpty } });

    private static ScalarValue Sort(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        double sortIdxRaw   = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 1;
        double sortOrderRaw = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        if (!double.IsFinite(sortIdxRaw) || !double.IsFinite(sortOrderRaw)) return ErrorValue.Value;
        int sortIdx   = (int)sortIdxRaw - 1;
        if (sortIdx < 0) return ErrorValue.Value;
        int sortOrder = (int)sortOrderRaw;
        if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
        bool byCol    = args.Count > 3 && args[3] is not BlankValue && ToBool(args[3]);
        if (!byCol && sortIdx >= arr.ColCount) return ErrorValue.Value;
        if (byCol && sortIdx >= arr.RowCount) return ErrorValue.Value;

        if (!byCol)
        {
            var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
            rowIndices.Sort((a, b) =>
            {
                var va = sortIdx < arr.ColCount ? arr.Cells[a, sortIdx] : BlankValue.Instance;
                var vb = sortIdx < arr.ColCount ? arr.Cells[b, sortIdx] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[rowIndices[r], c];
            return new RangeValue(result);
        }
        else
        {
            var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
            colIndices.Sort((a, b) =>
            {
                var va = sortIdx < arr.RowCount ? arr.Cells[sortIdx, a] : BlankValue.Instance;
                var vb = sortIdx < arr.RowCount ? arr.Cells[sortIdx, b] : BlankValue.Instance;
                return sortOrder * CompareScalar(va, vb);
            });
            var result = new ScalarValue[arr.RowCount, arr.ColCount];
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, c] = arr.Cells[r, colIndices[c]];
            return new RangeValue(result);
        }
    }

    private static ScalarValue SortBy(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;

        var keys = new List<(RangeValue Range, int Order)>();
        bool? sortRows = null;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue keyError) return keyError;
            if (args[i] is not RangeValue byArray) return ErrorValue.Value;

            if (!TryGetSortByOrientation(arr, byArray, out bool keySortsRows)) return ErrorValue.Value;
            if (sortRows.HasValue && sortRows.Value != keySortsRows) return ErrorValue.Value;
            sortRows ??= keySortsRows;

            int sortOrder = 1;
            if (i + 1 < args.Count && args[i + 1] is not RangeValue)
            {
                if (args[i + 1] is ErrorValue orderError) return orderError;
                double orderRaw = ToNumber(args[i + 1]);
                if (!double.IsFinite(orderRaw)) return ErrorValue.Value;
                sortOrder = (int)orderRaw;
                if (sortOrder != 1 && sortOrder != -1) return ErrorValue.Value;
                i++;
            }

            keys.Add((byArray, sortOrder));
        }

        if (keys.Count == 0) return ErrorValue.Value;
        return sortRows.GetValueOrDefault(true)
            ? SortByRows(arr, keys)
            : SortByColumns(arr, keys);
    }

    private static bool TryGetSortByOrientation(RangeValue arr, RangeValue byArray, out bool sortRows)
    {
        if (byArray.RowCount == arr.RowCount && byArray.ColCount == 1)
        {
            sortRows = true;
            return true;
        }

        if (byArray.RowCount == 1 && byArray.ColCount == arr.ColCount)
        {
            sortRows = false;
            return true;
        }

        sortRows = true;
        return false;
    }

    private static ScalarValue SortByRows(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var rowIndices = Enumerable.Range(0, arr.RowCount).ToList();
        rowIndices.Sort((a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareScalar(key.Range.Cells[a, 0], key.Range.Cells[b, 0]);
                if (cmp != 0) return key.Order * cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndices[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue SortByColumns(RangeValue arr, IReadOnlyList<(RangeValue Range, int Order)> keys)
    {
        var colIndices = Enumerable.Range(0, arr.ColCount).ToList();
        colIndices.Sort((a, b) =>
        {
            foreach (var key in keys)
            {
                int cmp = CompareScalar(key.Range.Cells[0, a], key.Range.Cells[0, b]);
                if (cmp != 0) return key.Order * cmp;
            }

            return a.CompareTo(b);
        });

        var result = new ScalarValue[arr.RowCount, arr.ColCount];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, colIndices[c]];
        return new RangeValue(result);
    }

    private static ScalarValue Take(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetArraySliceCount(args[1], arr.RowCount, isTake: true, out int rowStart, out int rowCount))
            return ErrorValue.Value;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: true, out colStart, out colCount))
                return ErrorValue.Value;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static ScalarValue Drop(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetArraySliceCount(args[1], arr.RowCount, isTake: false, out int rowStart, out int rowCount))
            return ErrorValue.Value;

        int colStart = 0;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetArraySliceCount(args[2], arr.ColCount, isTake: false, out colStart, out colCount))
                return ErrorValue.Value;
        }

        return SliceRange(arr, rowStart, colStart, rowCount, colCount);
    }

    private static bool TryGetArraySliceCount(
        ScalarValue countValue,
        int dimensionLength,
        bool isTake,
        out int start,
        out int count)
    {
        double raw = ToNumber(countValue);
        if (!double.IsFinite(raw))
        {
            start = 0;
            count = 0;
            return false;
        }

        int requested = (int)raw;
        if (requested == 0)
        {
            start = 0;
            count = 0;
            return false;
        }

        if (isTake)
        {
            count = Math.Min(Math.Abs(requested), dimensionLength);
            start = requested > 0 ? 0 : dimensionLength - count;
            return count > 0;
        }

        if (Math.Abs(requested) >= dimensionLength)
        {
            start = 0;
            count = 0;
            return false;
        }

        if (requested > 0)
        {
            start = requested;
            count = dimensionLength - requested;
        }
        else
        {
            start = 0;
            count = dimensionLength + requested;
        }

        return count > 0;
    }

    private static RangeValue SliceRange(RangeValue arr, int rowStart, int colStart, int rowCount, int colCount)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = arr.Cells[rowStart + r, colStart + c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (!TryResolveChoiceIndexes(args, arr.RowCount, out var rowIndexes, out var error)) return error;

        var result = new ScalarValue[rowIndexes.Count, arr.ColCount];
        for (int r = 0; r < rowIndexes.Count; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[rowIndexes[r], c];
        return new RangeValue(result);
    }

    private static ScalarValue ChooseCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (!TryResolveChoiceIndexes(args, arr.ColCount, out var colIndexes, out var error)) return error;

        var result = new ScalarValue[arr.RowCount, colIndexes.Count];
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < colIndexes.Count; c++)
                result[r, c] = arr.Cells[r, colIndexes[c]];
        return new RangeValue(result);
    }

    private static bool TryResolveChoiceIndexes(
        IReadOnlyList<ScalarValue> args,
        int dimensionLength,
        out List<int> indexes,
        out ScalarValue error)
    {
        indexes = new List<int>();
        error = ErrorValue.Value;

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e)
            {
                error = e;
                return false;
            }

            double raw = ToNumber(args[i]);
            if (!double.IsFinite(raw)) return false;

            int requested = (int)raw;
            if (requested == 0) return false;

            int zeroBased = requested > 0
                ? requested - 1
                : dimensionLength + requested;
            if (zeroBased < 0 || zeroBased >= dimensionLength) return false;

            indexes.Add(zeroBased);
        }

        return indexes.Count > 0;
    }

    private static ScalarValue VStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        int rowCount = arrays.Sum(a => a.RowCount);
        int colCount = arrays.Max(a => a.ColCount);
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int rowOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[rowOffset + r, c] = arr.Cells[r, c];
            rowOffset += arr.RowCount;
        }

        return new RangeValue(result);
    }

    private static ScalarValue HStack(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCollectStackArrays(args, out var arrays, out var error)) return error;

        int rowCount = arrays.Max(a => a.RowCount);
        int colCount = arrays.Sum(a => a.ColCount);
        var result = CreateFilledRange(rowCount, colCount, ErrorValue.NA);

        int colOffset = 0;
        foreach (var arr in arrays)
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[r, colOffset + c] = arr.Cells[r, c];
            colOffset += arr.ColCount;
        }

        return new RangeValue(result);
    }

    private static bool TryCollectStackArrays(
        IReadOnlyList<ScalarValue> args,
        out List<RangeValue> arrays,
        out ScalarValue error)
    {
        arrays = new List<RangeValue>();
        error = ErrorValue.Value;

        foreach (var arg in args)
        {
            if (arg is ErrorValue e)
            {
                error = e;
                return false;
            }

            if (arg is not RangeValue arr) return false;
            arrays.Add(arr);
        }

        return arrays.Count > 0;
    }

    private static ScalarValue[,] CreateFilledRange(int rowCount, int colCount, ScalarValue value)
    {
        var result = new ScalarValue[rowCount, colCount];
        for (int r = 0; r < rowCount; r++)
            for (int c = 0; c < colCount; c++)
                result[r, c] = value;
        return result;
    }

    private static ScalarValue ToRow(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Value;

        var result = new ScalarValue[1, values.Count];
        for (int c = 0; c < values.Count; c++)
            result[0, c] = values[c];
        return new RangeValue(result);
    }

    private static ScalarValue ToCol(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryFlattenArray(args, out var values, out var error)) return error;
        if (values.Count == 0) return ErrorValue.Value;

        var result = new ScalarValue[values.Count, 1];
        for (int r = 0; r < values.Count; r++)
            result[r, 0] = values[r];
        return new RangeValue(result);
    }

    private static bool TryFlattenArray(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (args[0] is not RangeValue arr) return false;

        int ignore = 0;
        if (args.Count > 1 && args[1] is not BlankValue)
        {
            if (args[1] is ErrorValue ignoreError)
            {
                error = ignoreError;
                return false;
            }

            double rawIgnore = ToNumber(args[1]);
            if (!double.IsFinite(rawIgnore)) return false;
            ignore = (int)rawIgnore;
            if (ignore is < 0 or > 3) return false;
        }

        bool scanByColumn = false;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (args[2] is ErrorValue scanError)
            {
                error = scanError;
                return false;
            }

            scanByColumn = ToBool(args[2]);
        }

        bool ignoreBlanks = (ignore & 1) != 0;
        bool ignoreErrors = (ignore & 2) != 0;

        if (scanByColumn)
        {
            for (int c = 0; c < arr.ColCount; c++)
                for (int r = 0; r < arr.RowCount; r++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }
        else
        {
            for (int r = 0; r < arr.RowCount; r++)
                for (int c = 0; c < arr.ColCount; c++)
                    AddFlattenedValue(arr.Cells[r, c], ignoreBlanks, ignoreErrors, values);
        }

        return true;
    }

    private static void AddFlattenedValue(
        ScalarValue value,
        bool ignoreBlanks,
        bool ignoreErrors,
        List<ScalarValue> values)
    {
        if (ignoreBlanks && (value is BlankValue || value is TextValue { Value.Length: 0 })) return;
        if (ignoreErrors && value is ErrorValue) return;
        values.Add(value);
    }

    private static ScalarValue WrapRows(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int rowCount = (values.Count + wrapCount - 1) / wrapCount;
        var result = CreateFilledRange(rowCount, wrapCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i / wrapCount, i % wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static ScalarValue WrapCols(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetWrapArgs(args, out var values, out int wrapCount, out var padWith, out var error)) return error;

        int colCount = (values.Count + wrapCount - 1) / wrapCount;
        var result = CreateFilledRange(wrapCount, colCount, padWith);
        for (int i = 0; i < values.Count; i++)
            result[i % wrapCount, i / wrapCount] = values[i];
        return new RangeValue(result);
    }

    private static bool TryGetWrapArgs(
        IReadOnlyList<ScalarValue> args,
        out List<ScalarValue> values,
        out int wrapCount,
        out ScalarValue padWith,
        out ScalarValue error)
    {
        values = new List<ScalarValue>();
        wrapCount = 0;
        padWith = ErrorValue.NA;
        error = ErrorValue.Value;

        if (args[0] is ErrorValue arrayError)
        {
            error = arrayError;
            return false;
        }

        if (args[0] is not RangeValue arr) return false;
        if (!TryReadVector(arr, values)) return false;

        if (args[1] is ErrorValue countError)
        {
            error = countError;
            return false;
        }

        double rawWrapCount = ToNumber(args[1]);
        if (!double.IsFinite(rawWrapCount)) return false;
        wrapCount = (int)rawWrapCount;
        if (wrapCount < 1)
        {
            error = ErrorValue.Num;
            return false;
        }

        if (args.Count > 2) padWith = args[2];
        return values.Count > 0;
    }

    private static bool TryReadVector(RangeValue arr, List<ScalarValue> values)
    {
        if (arr.RowCount == 1)
        {
            for (int c = 0; c < arr.ColCount; c++)
                values.Add(arr.Cells[0, c]);
            return true;
        }

        if (arr.ColCount == 1)
        {
            for (int r = 0; r < arr.RowCount; r++)
                values.Add(arr.Cells[r, 0]);
            return true;
        }

        return false;
    }

    private static ScalarValue Expand(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is ErrorValue rowError) return rowError;
        if (args.Count > 2 && args[2] is ErrorValue colError) return colError;

        if (!TryGetExpandDimension(args[1], arr.RowCount, out int rowCount)) return ErrorValue.Value;
        int colCount = arr.ColCount;
        if (args.Count > 2 && args[2] is not BlankValue)
        {
            if (!TryGetExpandDimension(args[2], arr.ColCount, out colCount)) return ErrorValue.Value;
        }

        if (rowCount < arr.RowCount || colCount < arr.ColCount) return ErrorValue.Value;

        var padWith = args.Count > 3 ? args[3] : ErrorValue.NA;
        var result = CreateFilledRange(rowCount, colCount, padWith);
        for (int r = 0; r < arr.RowCount; r++)
            for (int c = 0; c < arr.ColCount; c++)
                result[r, c] = arr.Cells[r, c];
        return new RangeValue(result);
    }

    private static bool TryGetExpandDimension(ScalarValue value, int originalLength, out int dimension)
    {
        dimension = originalLength;
        if (value is BlankValue) return true;

        double raw = ToNumber(value);
        if (!double.IsFinite(raw)) return false;
        dimension = (int)raw;
        return dimension >= 1;
    }

    private static ScalarValue Unique(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue arrayError) return arrayError;
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        bool byCol       = args.Count > 1 && args[1] is not BlankValue && ToBool(args[1]);
        bool exactlyOnce = args.Count > 2 && args[2] is not BlankValue && ToBool(args[2]);

        if (!byCol)
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var rowOfKey  = new List<int>();

            var keySb = new System.Text.StringBuilder();
            for (int r = 0; r < arr.RowCount; r++)
            {
                keySb.Clear();
                for (int c = 0; c < arr.ColCount; c++)
                {
                    if (c > 0) keySb.Append('\0');
                    keySb.Append(ToText(arr.Cells[r, c]));
                }
                var key = keySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    rowOfKey.Add(r);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => rowOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.NA;
            var result = new ScalarValue[selected.Count, arr.ColCount];
            for (int ri = 0; ri < selected.Count; ri++)
                for (int c = 0; c < arr.ColCount; c++)
                    result[ri, c] = arr.Cells[selected[ri], c];
            return new RangeValue(result);
        }
        else
        {
            var keyOrder  = new List<string>();
            var keyIndex  = new Dictionary<string, int>();
            var keyCounts = new List<int>();
            var colOfKey  = new List<int>();

            var colKeySb = new System.Text.StringBuilder();
            for (int c = 0; c < arr.ColCount; c++)
            {
                colKeySb.Clear();
                for (int r = 0; r < arr.RowCount; r++)
                {
                    if (r > 0) colKeySb.Append('\0');
                    colKeySb.Append(ToText(arr.Cells[r, c]));
                }
                var key = colKeySb.ToString();
                if (keyIndex.TryGetValue(key, out int idx))
                {
                    keyCounts[idx]++;
                }
                else
                {
                    keyIndex[key] = keyOrder.Count;
                    keyOrder.Add(key);
                    keyCounts.Add(1);
                    colOfKey.Add(c);
                }
            }

            var selected = keyOrder
                .Select((k, i) => (key: k, idx: i))
                .Where(t => !exactlyOnce || keyCounts[t.idx] == 1)
                .Select(t => colOfKey[t.idx])
                .ToList();

            if (selected.Count == 0) return ErrorValue.NA;
            var result = new ScalarValue[arr.RowCount, selected.Count];
            for (int r = 0; r < arr.RowCount; r++)
                for (int ci = 0; ci < selected.Count; ci++)
                    result[r, ci] = arr.Cells[r, selected[ci]];
            return new RangeValue(result);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SUBTOTAL
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Subtotal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        var funcNumD = ToNumber(args[0]);
        if (!double.IsFinite(funcNumD)) return ErrorValue.Value;
        int funcNum = (int)funcNumD;
        bool skipHidden = funcNum >= 101;
        int baseFunc = funcNum > 100 ? funcNum - 100 : funcNum;

        // Collect all numeric values from remaining args, respecting hidden-row exclusion
        var nums = new List<double>();
        int countaCount = 0;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is ErrorValue ei) return ei;
            if (args[i] is RangeValue rv)
            {
                for (int r = 0; r < rv.RowCount; r++)
                {
                    uint absRow = rv.StartRow + (uint)r;
                    if (skipHidden && ctx.IsRowHidden(absRow)) continue;
                    for (int c = 0; c < rv.ColCount; c++)
                    {
                        var cell = rv.Cells[r, c];
                        if (cell is ErrorValue err) return err;
                        if (TryCellNumber(cell, out double value)) nums.Add(value);
                        if (cell is not BlankValue) countaCount++;
                    }
                }
            }
            else if (args[i] is NumberValue nv2)
            {
                nums.Add(nv2.Value);
                countaCount++;
            }
        }

        if (nums.Count == 0 && baseFunc != 2 && baseFunc != 3)
            return new NumberValue(0);

        return baseFunc switch
        {
            1  => NumberResult(nums.Count == 0 ? 0 : nums.Average()),
            2  => new NumberValue(nums.Count),
            3  => new NumberValue(countaCount),
            4  => NumberResult(nums.Count == 0 ? 0 : nums.Max()),
            5  => NumberResult(nums.Count == 0 ? 0 : nums.Min()),
            6  => NumberResult(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (acc, x) => acc * x)),
            7  => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalStdDevS(nums)),
            8  => nums.Count == 0 ? new NumberValue(0) : NumberResult(SubtotalStdDevP(nums)),
            9  => NumberResult(nums.Sum()),
            10 => nums.Count < 2 ? ErrorValue.DivByZero : NumberResult(SubtotalVarS(nums)),
            11 => nums.Count == 0 ? new NumberValue(0) : NumberResult(SubtotalVarP(nums)),
            _  => ErrorValue.Value
        };
    }

    private static double SubtotalVarS(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
    }

    private static double SubtotalVarP(List<double> nums)
    {
        double mean = nums.Average();
        return nums.Sum(x => (x - mean) * (x - mean)) / nums.Count;
    }

    private static double SubtotalStdDevS(List<double> nums) => Math.Sqrt(SubtotalVarS(nums));
    private static double SubtotalStdDevP(List<double> nums) => Math.Sqrt(SubtotalVarP(nums));
}

/// <summary>
/// Context interface provided to built-in functions during evaluation.
/// </summary>
public interface IEvalContext
{
    ScalarValue GetCellValue(uint row, uint col);
    ScalarValue GetCellValue(string sheetName, uint row, uint col);
    IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol);
    IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol);

    /// <summary>
    /// Try to resolve a named range to a GridRange.
    /// Returns null if the name is not defined.
    /// </summary>
    Model.GridRange? TryResolveNamedRange(string name);

    /// <summary>
    /// Returns the sheet name for the given SheetId, or null if not found.
    /// Used by the evaluator to expand cross-sheet named ranges.
    /// </summary>
    string? TryGetSheetName(Model.SheetId sheetId);

    /// <summary>Returns true if the row is hidden (filter, manual, or group collapse).</summary>
    bool IsRowHidden(uint row);
}
