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
        ["FILTER"]   = (Filter, 2, 3),
        ["SORT"]     = (Sort, 1, 4),
        ["UNIQUE"]   = (Unique, 1, 3),

        // ── Subtotal ─────────────────────────────────────────────────────────
        ["SUBTOTAL"] = (Subtotal, 2, 255),
    };

    private static readonly HashSet<string> VolatileFunctions = ["NOW", "TODAY", "RAND", "RANDBETWEEN", "INDIRECT"];

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
        BlankValue => false,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to boolean")
    };

    private static string ToText(ScalarValue v) => v switch
    {
        DirectTextLiteralValue t => t.Value,
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    private static bool TryDirectTextNumber(DirectTextLiteralValue value, out double number) =>
        double.TryParse(value.Value, System.Globalization.CultureInfo.InvariantCulture, out number);

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
                total += value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        return new NumberValue(total);
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
                total += value;
                count++;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : new NumberValue(total / count);
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
                if (min is null || value < min) min = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (min is null || val < min) min = val;
        }
        return min.HasValue ? new NumberValue(min.Value) : new NumberValue(0);
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
                if (max is null || value > max) max = value;
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            var val = ToNumber(arg);
            if (max is null || val > max) max = val;
        }
        return max.HasValue ? new NumberValue(max.Value) : new NumberValue(0);
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
            return new NumberValue(Math.Round(number, digits, MidpointRounding.AwayFromZero));

        double factor = Math.Pow(10, -digits);
        return new NumberValue(Math.Round(number / factor, 0, MidpointRounding.AwayFromZero) * factor);
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
        return new NumberValue(Math.Abs(ToNumber(args[0])));
    }

    private static ScalarValue Concat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            sb.Append(ToText(arg));
        }
        return new TextValue(sb.ToString());
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
        var count = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return new TextValue(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args.Count > 1 && args[1] is ErrorValue countError) return countError;
        var text  = ToText(args[0]);
        var count = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
        if (count < 0) return ErrorValue.Value;
        count = Math.Min(count, text.Length);
        return new TextValue(count == 0 ? "" : text[^count..]);
    }

    private static ScalarValue Now(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Now);

    private static ScalarValue Today(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        DateTimeValue.FromDateTime(DateTime.Today);

    private static ScalarValue Rand(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new NumberValue(Random.Shared.NextDouble());

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
            // Exact match
            for (int r = 1; r <= table.RowCount; r++)
            {
                if (ScalarEquals(table.At(r, 1), lookupValue))
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
            for (int c = 1; c <= table.ColCount; c++)
            {
                if (ScalarEquals(table.At(1, c), lookupValue))
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

        if (rowNum < 1 || rowNum > table.RowCount) return ErrorValue.Ref;
        if (colNum < 1 || colNum > table.ColCount) return ErrorValue.Ref;

        return table.At(rowNum, colNum);
    }

    private static ScalarValue Match(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue table) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var lookupValue = args[0];
        int matchType = args.Count > 2 ? (int)ToNumber(args[2]) : 1;

        // Flatten to 1-D (single row or column expected)
        var flat = table.Flatten();

        if (matchType == 0)
        {
            // Exact match
            for (int i = 0; i < flat.Count; i++)
                if (ScalarEquals(flat[i], lookupValue))
                    return new NumberValue(i + 1);
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

    private static ScalarValue Sumif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
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
                if (sv is NumberValue nv) total += nv.Value;
                else if (sv is BlankValue) { /* skip */ }
            }
        }
        return new NumberValue(total);
    }

    private static ScalarValue Countif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
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
        if (args[0] is not RangeValue rangeArg) return ErrorValue.Value;
        var criteria = args[1];
        if (criteria is ErrorValue criteriaError) return criteriaError;
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
                if (sv is NumberValue nv) { total += nv.Value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return new NumberValue(total / count);
    }

    /// <summary>
    /// Test a cell value against an Excel criteria string or value.
    /// Supports: number (exact), text (exact, case-insensitive),
    /// operator strings ">5", ">=5", "<5", "<=5", "<>5", "=text",
    /// and simple wildcard strings using * and ?.
    /// </summary>
    private static bool MatchesCriteria(ScalarValue cellValue, ScalarValue criteria)
    {
        if (criteria is NumberValue cn)
            return cellValue is NumberValue cvn && cvn.Value == cn.Value;

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
                       cellValue is NumberValue nv ? nv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) :
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
            if (cellValue is not NumberValue cv) return false;
            return op switch
            {
                ">"  => cv.Value > rhsNum,
                ">=" => cv.Value >= rhsNum,
                "<"  => cv.Value < rhsNum,
                "<=" => cv.Value <= rhsNum,
                "="  => cv.Value == rhsNum,
                "<>" => cv.Value != rhsNum,
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
        if (val is NumberValue nv)
            return new TextValue(FormatNumberInline(nv.Value, fmt));
        return new TextValue(ToText(val));
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
        return new TextValue(text);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new TextValue(ToText(args[0]).ToUpperInvariant());
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new TextValue(ToText(args[0]).ToLowerInvariant());
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
        return new TextValue(sb.ToString());
    }

    private static ScalarValue Substitute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue oldTextError) return oldTextError;
        if (args[2] is ErrorValue newTextError) return newTextError;
        var text    = ToText(args[0]);
        var oldText = ToText(args[1]);
        var newText = ToText(args[2]);

        if (oldText.Length == 0) return new TextValue(text);

        if (args.Count > 3)
        {
            // Replace the Nth occurrence only
            if (args[3] is ErrorValue e3) return e3;
            int instanceNum = (int)ToNumber(args[3]);
            if (instanceNum < 1) return ErrorValue.Value;
            int count = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                int idx = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                count++;
                if (count == instanceNum)
                    return new TextValue(text[..idx] + newText + text[(idx + oldText.Length)..]);
                pos = idx + oldText.Length;
            }
            return new TextValue(text); // instance not found
        }
        else
        {
            return new TextValue(text.Replace(oldText, newText, StringComparison.Ordinal));
        }
    }

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
        return new TextValue(text.Substring(start, actualLen));
    }

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue repeatError) return repeatError;
        var text  = ToText(args[0]);
        int times = (int)ToNumber(args[1]);
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
        int year  = (int)ToNumber(args[0]);
        int month = (int)ToNumber(args[1]);
        int day   = (int)ToNumber(args[2]);
        if (year >= 0 && year < 1900)
            year += 1900;
        try
        {
            var dt = new DateTime(year, 1, 1)
                .AddMonths(month - 1)
                .AddDays(day - 1);
            return new NumberValue(dt.ToOADate());
        }
        catch { return ErrorValue.Value; }
    }

    private static DateTime OADateToDateTime(ScalarValue v) =>
        DateTime.FromOADate(ToNumber(v));

    private static ScalarValue Year(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Year);
    }

    private static ScalarValue Month(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Month);
    }

    private static ScalarValue Day(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Day);
    }

    private static ScalarValue Hour(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Hour);
    }

    private static ScalarValue Minute(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Minute);
    }

    private static ScalarValue Second(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(OADateToDateTime(args[0]).Second);
    }

    private static ScalarValue Weekday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args.Count > 1 && args[1] is ErrorValue returnTypeError) return returnTypeError;
        var dt = OADateToDateTime(args[0]);
        int returnType = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
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
        var dt     = OADateToDateTime(args[0]);
        int months = (int)ToNumber(args[1]);
        try
        {
            var result = dt.AddMonths(months);
            return new NumberValue(result.ToOADate());
        }
        catch { return ErrorValue.Value; }
    }

    private static ScalarValue Datedif(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        var start = OADateToDateTime(args[0]);
        var end   = OADateToDateTime(args[1]);
        var unit  = ToText(args[2]).ToUpperInvariant();

        return unit switch
        {
            "D" => new NumberValue((end - start).Days),
            "M" => new NumberValue(MonthDiff(start, end)),
            "Y" => new NumberValue(YearDiff(start, end)),
            _   => ErrorValue.Value
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

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Math
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Mod(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var d = ToNumber(args[1]);
        if (d == 0) return ErrorValue.DivByZero;
        return new NumberValue(n - d * Math.Floor(n / d));
    }

    private static ScalarValue Power(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        return new NumberValue(Math.Pow(ToNumber(args[0]), ToNumber(args[1])));
    }

    private static ScalarValue Sqrt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (n < 0) return ErrorValue.Num;
        return new NumberValue(Math.Sqrt(n));
    }

    private static ScalarValue IntFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(Math.Floor(ToNumber(args[0])));
    }

    private static ScalarValue Ceiling(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        return new NumberValue(Math.Ceiling(n / sig) * sig);
    }

    private static ScalarValue Floor(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var n = ToNumber(args[0]);
        var sig = ToNumber(args[1]);
        if (sig == 0) return new NumberValue(0);
        return new NumberValue(Math.Floor(n / sig) * sig);
    }

    private static ScalarValue Randbetween(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double db = ToNumber(args[0]);
        double dt = ToNumber(args[1]);
        if (!double.IsFinite(db) || !double.IsFinite(dt)) return ErrorValue.Num;
        long bottom = (long)Math.Truncate(db);
        long top    = (long)Math.Truncate(dt);
        if (bottom > top) return ErrorValue.Num;
        // Random.Shared.NextInt64 requires [minValue, maxValue) so add 1 safely
        long range = top - bottom + 1;
        return new NumberValue(bottom + (long)(Random.Shared.NextDouble() * range));
    }

    private static ScalarValue Sign(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        return new NumberValue(n > 0 ? 1 : n < 0 ? -1 : 0);
    }

    private static ScalarValue Log(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        var n    = ToNumber(args[0]);
        var base_ = args.Count > 1 ? ToNumber(args[1]) : 10.0;
        if (n <= 0 || base_ <= 0 || base_ == 1) return ErrorValue.Num;
        return new NumberValue(Math.Log(n) / Math.Log(base_));
    }

    private static ScalarValue Ln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var n = ToNumber(args[0]);
        if (n <= 0) return ErrorValue.Num;
        return new NumberValue(Math.Log(n));
    }

    private static ScalarValue Exp(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(Math.Exp(ToNumber(args[0])));
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
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        int k = (int)ToNumber(args[1]);
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderByDescending(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        int k = (int)ToNumber(args[1]);
        var (values, err) = CollectRangeNumbers(range);
        if (err is not null) return err;
        var nums = values!.OrderBy(x => x).ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        var number = ToNumber(args[0]);
        int order  = args.Count > 2 ? (int)ToNumber(args[2]) : 0;

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
        return new NumberValue(Math.Sqrt(variance));
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (numsOrNull, err) = CollectNumbers(args);
        if (err is not null) return err;
        var nums = numsOrNull!;
        if (nums.Count == 0) return ErrorValue.Value;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return new NumberValue(nums[mid]);
        return new NumberValue((nums[mid - 1] + nums[mid]) / 2.0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Multi-criteria aggregation
    // ═══════════════════════════════════════════════════════════════════

    // SUMIFS(sum_range, criteria_range1, criteria1, [criteria_range2, criteria2, ...])
    private static ScalarValue Sumifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue sumRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var sumFlat = sumRange.Flatten();
        int len = sumFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
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
                if (sumFlat[i] is NumberValue nv) total += nv.Value;
            }
        }
        return new NumberValue(total);
    }

    // COUNTIFS(criteria_range1, criteria1, ...)
    private static ScalarValue Countifs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args.Count < 2 || args.Count % 2 != 0) return ErrorValue.Value;
        int pairCount = args.Count / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[p * 2] is not RangeValue cr) return ErrorValue.Value;
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
        if (args[0] is not RangeValue avgRange) return ErrorValue.Value;
        if (args.Count < 3 || (args.Count - 1) % 2 != 0) return ErrorValue.Value;
        var avgFlat = avgRange.Flatten();
        int len = avgFlat.Count;
        int pairCount = (args.Count - 1) / 2;
        var pairs = new (IReadOnlyList<ScalarValue> Flat, ScalarValue Criteria)[pairCount];
        for (int p = 0; p < pairCount; p++)
        {
            if (args[1 + p * 2] is not RangeValue cr) return ErrorValue.Value;
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
                if (avgFlat[i] is NumberValue nv) { total += nv.Value; count++; }
            }
        }
        if (count == 0) return ErrorValue.DivByZero;
        return new NumberValue(total / count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Modern lookup: XLOOKUP
    // ═══════════════════════════════════════════════════════════════════

    // XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])
    private static ScalarValue Xlookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue lookupArr) return ErrorValue.Value;
        if (args[2] is not RangeValue returnArr) return ErrorValue.Value;

        var lookupValue = args[0];
        var lookupFlat = lookupArr.Flatten();
        var returnFlat = returnArr.Flatten();

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
                    return i < returnFlat.Count ? returnFlat[i] : ErrorValue.NA;
            return ifNotFound;
        }
        else if (matchMode == 2)
        {
            string pattern = ToText(lookupValue);
            foreach (int i in indices)
                if (lookupFlat[i] is TextValue tv && WildcardMatch(tv.Value, pattern, ignoreCase: true))
                    return i < returnFlat.Count ? returnFlat[i] : ErrorValue.NA;
            return ifNotFound;
        }
        else if (matchMode == -1)
        {
            // Exact or next smaller
            int best = -1;
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) <= 0)
                    best = i;
            return best >= 0 && best < returnFlat.Count ? returnFlat[best] : ifNotFound;
        }
        else
        {
            // Exact or next larger: return first element >= lookupValue
            foreach (int i in indices)
                if (CompareScalar(lookupFlat[i], lookupValue) >= 0)
                    return i < returnFlat.Count ? returnFlat[i] : ifNotFound;
            return ifNotFound;
        }
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
        return new TextValue(string.Join(delimiter, parts));
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
        int code = (int)ToNumber(args[0]);
        if (code <= 0 || code > 255) return ErrorValue.Value;
        return new TextValue(((char)code).ToString());
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Count: COUNTBLANK
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Countblank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        int count = range.Flatten().Count(v => v is BlankValue);
        return new NumberValue(count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 5 – Misc: CHOOSE, SUMPRODUCT, ROUNDDOWN, ROUNDUP, TRUNC
    // ═══════════════════════════════════════════════════════════════════

    // CHOOSE(index, val1, val2, ...)
    private static ScalarValue Choose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        int idx = (int)ToNumber(args[0]);
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
                product *= v is NumberValue n ? n.Value : 0;
            }
            total += product;
        }
        return new NumberValue(total);
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
        return new NumberValue((n >= 0 ? Math.Floor(n * factor) : Math.Ceiling(n * factor)) / factor);
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
        return new NumberValue((n >= 0 ? Math.Ceiling(n * factor) : Math.Floor(n * factor)) / factor);
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
        return new NumberValue(Math.Truncate(n * factor) / factor);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper: compare two ScalarValues (returns <0, 0, >0)
    // ═══════════════════════════════════════════════════════════════════

    private static int CompareScalar(ScalarValue a, ScalarValue b)
    {
        if (a is NumberValue na && b is NumberValue nb)
            return na.Value.CompareTo(nb.Value);
        if (a is TextValue ta && b is TextValue tb)
            return string.Compare(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        // Mixed: numbers < text
        return (a is NumberValue ? 0 : 1) - (b is NumberValue ? 0 : 1);
    }

    internal static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is NumberValue na && b is NumberValue nb)
            return na.Value == nb.Value;
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
        return new NumberValue(Math.Sin(ToNumber(args[0])));
    }

    private static ScalarValue Cos(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(Math.Cos(ToNumber(args[0])));
    }

    private static ScalarValue Tan(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(Math.Tan(ToNumber(args[0])));
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
        return new NumberValue(Math.Atan(ToNumber(args[0])));
    }

    // ATAN2(x_num, y_num) – matches Excel argument order (x first, then y)
    private static ScalarValue Atan2Func(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double x = ToNumber(args[0]);
        double y = ToNumber(args[1]);
        if (x == 0 && y == 0) return ErrorValue.DivByZero;
        return new NumberValue(Math.Atan2(y, x));
    }

    private static ScalarValue Degrees(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(ToNumber(args[0]) * 180.0 / Math.PI);
    }

    private static ScalarValue Radians(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return new NumberValue(ToNumber(args[0]) * Math.PI / 180.0);
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
                result *= value;
            }
            else if (a is NumberValue or BoolValue) result *= ToNumber(a);
        }
        return new NumberValue(result);
    }

    private static ScalarValue Quotient(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        double d = ToNumber(args[1]);
        if (d == 0) return ErrorValue.DivByZero;
        return new NumberValue(Math.Truncate(ToNumber(args[0]) / d));
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
        if (m == 0) return new NumberValue(0);
        if (n != 0 && (n < 0) != (m < 0)) return ErrorValue.Num;
        return new NumberValue(Math.Round(n / m, MidpointRounding.AwayFromZero) * m);
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
        return new NumberValue(Math.Round(result));
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
        return new NumberValue(result);
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
        if (TimeSpan.TryParse(text, out var ts) && ts.Days == 0)
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
        var dt = DateTime.FromOADate(ToNumber(args[0]));
        int months = (int)ToNumber(args[1]);
        var target = dt.AddMonths(months + 1);
        var eomonth = new DateTime(target.Year, target.Month, 1).AddDays(-1);
        return new NumberValue(eomonth.ToOADate());
    }

    private static ScalarValue Weeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var dt = DateTime.FromOADate(ToNumber(args[0]));
        int returnType = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 1;
        DayOfWeek firstDay = returnType == 2 ? DayOfWeek.Monday : DayOfWeek.Sunday;
        var jan1 = new DateTime(dt.Year, 1, 1);
        int jan1Dow = ((int)jan1.DayOfWeek - (int)firstDay + 7) % 7;
        int dayOfYear = (dt - jan1).Days;
        return new NumberValue((dayOfYear + jan1Dow) / 7 + 1);
    }

    private static ScalarValue Isoweeknum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var dt = DateTime.FromOADate(ToNumber(args[0]));
        return new NumberValue(System.Globalization.ISOWeek.GetWeekOfYear(dt));
    }

    private static ScalarValue Workday(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var current = DateTime.FromOADate(ToNumber(args[0]));
        int days = (int)ToNumber(args[1]);
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (v is NumberValue nv)
                    holidays.Add(DateTime.FromOADate(nv.Value).Date);
        int sign = days < 0 ? -1 : 1;
        int remaining = Math.Abs(days);
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
        var startDt = DateTime.FromOADate(ToNumber(args[0])).Date;
        var endDt   = DateTime.FromOADate(ToNumber(args[1])).Date;
        var holidays = new HashSet<DateTime>();
        if (args.Count > 2 && args[2] is RangeValue hRange)
            foreach (var v in hRange.Flatten())
                if (v is NumberValue nv)
                    holidays.Add(DateTime.FromOADate(nv.Value).Date);
        int sign = startDt <= endDt ? 1 : -1;
        var lo = startDt <= endDt ? startDt : endDt;
        var hi = startDt <= endDt ? endDt   : startDt;
        int count = 0;
        for (var d = lo; d <= hi; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Saturday &&
                d.DayOfWeek != DayOfWeek.Sunday &&
                !holidays.Contains(d))
                count++;
        return new NumberValue(sign * count);
    }

    private static ScalarValue Days(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var endDt   = DateTime.FromOADate(ToNumber(args[0]));
        var startDt = DateTime.FromOADate(ToNumber(args[1]));
        return new NumberValue((endDt - startDt).Days);
    }

    private static ScalarValue Days360(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var startDt = DateTime.FromOADate(ToNumber(args[0])).Date;
        var endDt   = DateTime.FromOADate(ToNumber(args[1])).Date;
        bool european = args.Count > 2 && args[2] is not BlankValue && ToNumber(args[2]) != 0;
        double days = european ? Days30E360(startDt, endDt) : Days30US360(startDt, endDt);
        return new NumberValue(Math.Truncate(days));
    }

    private static ScalarValue Yearfrac(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        var startDt = DateTime.FromOADate(ToNumber(args[0])).Date;
        var endDt   = DateTime.FromOADate(ToNumber(args[1])).Date;
        int basis = args.Count > 2 && args[2] is not BlankValue ? (int)ToNumber(args[2]) : 0;
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
        return new NumberValue(list.Sum(x => (x - mean) * (x - mean)) / (list.Count - 1));
    }

    private static ScalarValue VarP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (list, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (list!.Count == 0) return ErrorValue.DivByZero;
        double mean = list.Average();
        return new NumberValue(list.Sum(x => (x - mean) * (x - mean)) / list.Count);
    }

    private static ScalarValue StdevP(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var r = VarP(args, ctx);
        return r is NumberValue nv ? new NumberValue(Math.Sqrt(nv.Value)) : r;
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
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (k < 0 || k > 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        double rank = k * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return new NumberValue(sorted[^1]);
        return new NumberValue(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue PercentileExc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        double k = ToNumber(args[1]);
        if (k <= 0 || k >= 1) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return ErrorValue.Num;
        double rank = k * (n + 1) - 1;
        if (rank < 0 || rank >= n) return ErrorValue.Num;
        int lo = (int)rank;
        if (lo >= n - 1) return new NumberValue(sorted[n - 1]);
        return new NumberValue(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
    }

    private static ScalarValue QuartileInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        int quart = (int)ToNumber(args[1]);
        if (quart < 0 || quart > 4) return ErrorValue.Num;
        var (nums, err) = CollectRangeNumbers(rv);
        if (err is not null) return err;
        var sorted = nums!.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return ErrorValue.Num;
        if (quart == 0) return new NumberValue(sorted[0]);
        if (quart == 4) return new NumberValue(sorted[^1]);
        double rank = (quart / 4.0) * (sorted.Count - 1);
        int lo = (int)rank;
        if (lo >= sorted.Count - 1) return new NumberValue(sorted[^1]);
        return new NumberValue(sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]));
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
        return new NumberValue(Math.Exp(logSum / nums.Count));
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
        return new NumberValue(nums.Count / recSum);
    }

    private static ScalarValue Avedev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var (nums, err) = CollectNumbers(args);
        if (err is not null) return err;
        if (nums!.Count == 0) return ErrorValue.DivByZero;
        double mean = nums.Average();
        return new NumberValue(nums.Average(x => Math.Abs(x - mean)));
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
            if (freq[key] == maxFreq) return new NumberValue(key);
        return ErrorValue.NA;
    }

    private static ScalarValue PercentrankInc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue rv) return ErrorValue.Value;
        if (args[1] is ErrorValue e) return e;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double x = ToNumber(args[1]);
        int sig = args.Count > 2 && args[2] is not BlankValue ? (int)ToNumber(args[2]) : 3;
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
        return new NumberValue(Math.Floor(pctRank * factor) / factor);
    }

    private static ScalarValue Correl(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue rv1) return ErrorValue.Value;
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
        return new NumberValue(cov / Math.Sqrt(varX * varY));
    }

    private static ScalarValue Forecast(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is not RangeValue knownY) return ErrorValue.Value;
        if (args[2] is not RangeValue knownX) return ErrorValue.Value;
        double x    = ToNumber(args[0]);
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
        return new NumberValue(a + b * x);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4a  –  Financial
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue Pmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        int    nper = (int)ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return new NumberValue(-(pv + fv) / nper);
        double rn  = Math.Pow(1 + rate, nper);
        double pmt = -(pv * rn + fv) * rate / ((1 + rate * type) * (rn - 1));
        return new NumberValue(pmt);
    }

    private static ScalarValue Pv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        int    nper = (int)ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return new NumberValue(-pmt * nper - fv);
        double rn = Math.Pow(1 + rate, nper);
        double pv = (-pmt * (1 + rate * type) * (rn - 1) / rate - fv) / rn;
        return new NumberValue(pv);
    }

    private static ScalarValue Fv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        int    nper = (int)ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double pv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (Math.Abs(rate) < 1e-10)
            return new NumberValue(-pv - pmt * nper);
        double rn = Math.Pow(1 + rate, nper);
        return new NumberValue(-pv * rn - pmt * (1 + rate * type) * (rn - 1) / rate);
    }

    private static ScalarValue Nper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        double pmt  = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (Math.Abs(rate) < 1e-10)
        {
            if (Math.Abs(pmt) < 1e-10) return ErrorValue.DivByZero;
            return new NumberValue(-(pv + fv) / pmt);
        }
        double pmtAdj = pmt * (1 + rate * type);
        double ratio  = (pmtAdj - fv * rate) / (pmtAdj + pv * rate);
        if (ratio <= 0) return ErrorValue.Num;
        return new NumberValue(Math.Log(ratio) / Math.Log(1 + rate));
    }

    private static ScalarValue Rate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        int    nper  = (int)ToNumber(args[0]);
        double pmt   = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double fv    = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double guess = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0.1;
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
        var (values, err) = CollectNumbers(args, start: 1);
        if (err is not null) return err;

        double result = 0;
        for (int i = 0; i < values!.Count; i++)
            result += values[i] / Math.Pow(1 + rate, i + 1);
        return new NumberValue(result);
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
        if (life == 0) return ErrorValue.DivByZero;
        return new NumberValue((cost - salvage) / life);
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
        return new TextValue(text[..start] + newText + text[end..]);
    }

    private static ScalarValue Concatenate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            if (a is ErrorValue e) return e;
            sb.Append(ToText(a));
        }
        return new TextValue(sb.ToString());
    }

    private static ScalarValue TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        return args[0] is TextValue t ? t : new TextValue("");
    }

    private static ScalarValue Fixed(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        double n = ToNumber(args[0]);
        int dec = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 2;
        bool noCommas = args.Count > 2 && args[2] is not BlankValue && ToBool(args[2]);
        return new TextValue(FormatRoundedNumber(n, dec, useCommas: !noCommas));
    }

    private static ScalarValue Clean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var sb = new System.Text.StringBuilder();
        foreach (char c in ToText(args[0]))
            if (c >= 32) sb.Append(c);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue Dollar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        double n = ToNumber(args[0]);
        int dec = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 2;
        return new TextValue("$" + FormatRoundedNumber(n, dec, useCommas: true));
    }

    private static string FormatRoundedNumber(double value, int decimals, bool useCommas)
    {
        double rounded = RoundWithExcelDigits(value, decimals);
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
        var refText = ToText(args[0]).Trim();
        string? sheetName = null;
        int bangIdx = refText.IndexOf('!');
        if (bangIdx >= 0)
        {
            sheetName = refText[..bangIdx].Trim('\'');
            refText   = refText[(bangIdx + 1)..];
        }
        if (!TryParseA1Ref(refText, out uint row, out uint col))
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
        return row > 0 && col > 0;
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
        string? sheetText = args.Count > 4 && args[4] is not BlankValue ? ToText(args[4]) : null;
        string colLetter = CellAddress.NumberToColumnName((uint)colNum);
        bool colAbs = absNum is 1 or 3;
        bool rowAbs = absNum is 1 or 2;
        string addr = $"{(colAbs ? "$" : "")}{colLetter}{(rowAbs ? "$" : "")}{rowNum}";
        if (!string.IsNullOrEmpty(sheetText))
            addr = $"'{sheetText}'!{addr}";
        return new TextValue(addr);
    }

    private static ScalarValue Lookup(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is not RangeValue lookupVec) return ErrorValue.Value;
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
        int rows   = (int)ToNumber(args[0]);
        int cols   = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) : 1;
        double start = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 1;
        double step  = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 1;
        if (rows < 1 || cols < 1) return ErrorValue.Value;
        if ((long)rows * cols > 1_000_000) return ErrorValue.Value;
        var cells = new ScalarValue[rows, cols];
        double val = start;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                cells[r, c] = new NumberValue(val);
                val += step;
            }
        return new RangeValue(cells);
    }

    private static ScalarValue Filter(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args[1] is not RangeValue include) return ErrorValue.Value;
        var ifEmpty = args.Count > 2 ? args[2] : new TextValue("");

        var includeFlat = include.Flatten();
        var matchedRows = new List<int>();
        int rowLimit = Math.Min(includeFlat.Count, arr.RowCount);
        for (int i = 0; i < rowLimit; i++)
        {
            var v = includeFlat[i];
            if (v is ErrorValue e) return e;
            bool matched = v is BoolValue { Value: true }
                        || (v is NumberValue nv && nv.Value != 0);
            if (matched) matchedRows.Add(i);
        }

        if (matchedRows.Count == 0)
        {
            if (ifEmpty is RangeValue rvEmpty) return rvEmpty;
            return new RangeValue(new ScalarValue[1, 1] { { ifEmpty } });
        }

        var result = new ScalarValue[matchedRows.Count, arr.ColCount];
        for (int ri = 0; ri < matchedRows.Count; ri++)
            for (int c = 0; c < arr.ColCount; c++)
                result[ri, c] = arr.Cells[matchedRows[ri], c];
        return new RangeValue(result);
    }

    private static ScalarValue Sort(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue arr) return ErrorValue.Value;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;
        if (args.Count > 3 && args[3] is ErrorValue e3) return e3;
        int sortIdx   = args.Count > 1 && args[1] is not BlankValue ? (int)ToNumber(args[1]) - 1 : 0;
        if (sortIdx < 0) return ErrorValue.Value;
        int sortOrder = args.Count > 2 && args[2] is not BlankValue ? (int)ToNumber(args[2]) : 1;
        bool byCol    = args.Count > 3 && args[3] is not BlankValue && ToBool(args[3]);

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

    private static ScalarValue Unique(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
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
        int funcNum = (int)ToNumber(args[0]);
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
                        if (cell is NumberValue nv) nums.Add(nv.Value);
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
            1  => new NumberValue(nums.Count == 0 ? 0 : nums.Average()),
            2  => new NumberValue(nums.Count),
            3  => new NumberValue(countaCount),
            4  => new NumberValue(nums.Count == 0 ? 0 : nums.Max()),
            5  => new NumberValue(nums.Count == 0 ? 0 : nums.Min()),
            6  => new NumberValue(nums.Count == 0 ? 0 : nums.Aggregate(1.0, (acc, x) => acc * x)),
            7  => nums.Count < 2 ? ErrorValue.DivByZero : new NumberValue(SubtotalStdDevS(nums)),
            8  => nums.Count == 0 ? new NumberValue(0) : new NumberValue(SubtotalStdDevP(nums)),
            9  => new NumberValue(nums.Sum()),
            10 => nums.Count < 2 ? ErrorValue.DivByZero : new NumberValue(SubtotalVarS(nums)),
            11 => nums.Count == 0 ? new NumberValue(0) : new NumberValue(SubtotalVarP(nums)),
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
