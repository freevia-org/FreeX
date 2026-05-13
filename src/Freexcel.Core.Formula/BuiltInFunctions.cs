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
    };

    private static readonly HashSet<string> VolatileFunctions = ["NOW", "TODAY", "RAND", "RANDBETWEEN"];

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
        TextValue t when double.TryParse(t.Value, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to number")
    };

    private static bool ToBool(ScalarValue v) => v switch
    {
        BoolValue b => b.Value,
        NumberValue n => n.Value != 0.0,
        BlankValue => false,
        _ => throw new FormulaEvalException("#VALUE!", $"Cannot convert {v} to boolean")
    };

    private static string ToText(ScalarValue v) => v switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    // ── Function implementations ──

    private static ScalarValue Sum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
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
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (!ToBool(arg)) return new BoolValue(false);
        }
        return new BoolValue(true);
    }

    private static ScalarValue Or(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (ToBool(arg)) return new BoolValue(true);
        }
        return new BoolValue(false);
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
        var digits = (int)ToNumber(args[1]);
        return new NumberValue(Math.Round(number, Math.Max(0, digits), MidpointRounding.AwayFromZero));
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
        var text = ToText(args[0]);
        var count = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
        count = Math.Min(count, text.Length);
        return new TextValue(text[..count]);
    }

    private static ScalarValue Right(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var text = ToText(args[0]);
        var count = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
        count = Math.Min(count, text.Length);
        return new TextValue(text[^count..]);
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
        int colIndex = (int)ToNumber(args[2]);
        bool rangeLookup = args.Count < 4 || ToBool(args[3]); // default TRUE

        if (colIndex < 1 || colIndex > table.ColCount) return ErrorValue.Ref;

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
        int rowIndex = (int)ToNumber(args[2]);
        bool rangeLookup = args.Count < 4 || ToBool(args[3]);

        if (rowIndex < 1 || rowIndex > table.RowCount) return ErrorValue.Ref;

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

        int rowNum = (int)ToNumber(args[1]);
        int colNum = args.Count > 2 ? (int)ToNumber(args[2]) : 1;

        if (rowNum < 1 || rowNum > table.RowCount) return ErrorValue.Ref;
        if (colNum < 1 || colNum > table.ColCount) return ErrorValue.Ref;

        return table.At(rowNum, colNum);
    }

    private static ScalarValue Match(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue table) return ErrorValue.Value;

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
            // Descending approximate: smallest value >= lookupValue
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
        RangeValue? sumRange = args.Count > 2 ? args[2] as RangeValue : null;

        var rangeFlat = rangeArg.Flatten();
        IReadOnlyList<ScalarValue> sumFlat = sumRange is not null ? sumRange.Flatten() : rangeFlat;

        double total = 0;
        for (int i = 0; i < rangeFlat.Count; i++)
        {
            if (MatchesCriteria(rangeFlat[i], criteria))
            {
                var sv = i < sumFlat.Count ? sumFlat[i] : BlankValue.Instance;
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

    /// <summary>Simple Excel-style wildcard match (* = any chars, ? = any single char).</summary>
    private static bool WildcardMatch(string text, string pattern, bool ignoreCase)
    {
        // Convert pattern to regex
        var sb = new System.Text.StringBuilder("^");
        foreach (var ch in pattern)
        {
            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:  sb.Append(System.Text.RegularExpressions.Regex.Escape(ch.ToString())); break;
            }
        }
        sb.Append('$');
        var opts = ignoreCase
            ? System.Text.RegularExpressions.RegexOptions.IgnoreCase
            : System.Text.RegularExpressions.RegexOptions.None;
        return System.Text.RegularExpressions.Regex.IsMatch(text, sb.ToString(), opts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 4.2  –  Text functions
    // ═══════════════════════════════════════════════════════════════════

    private static ScalarValue TextFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var fmt = ToText(args[1]);
        // Simple inline formatter (avoids depending on Freexcel.Core.Calc)
        var val = args[0];
        if (val is NumberValue nv)
            return new TextValue(FormatNumberInline(nv.Value, fmt));
        return new TextValue(ToText(val));
    }

    private static string FormatNumberInline(double value, string fmt)
    {
        if (string.IsNullOrEmpty(fmt)) return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        try { return value.ToString(fmt, System.Globalization.CultureInfo.CurrentCulture); }
        catch { return value.ToString(System.Globalization.CultureInfo.CurrentCulture); }
    }

    private static ScalarValue Trim(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text = ToText(args[0]).Trim();
        // Collapse interior multiple spaces to single space
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
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
        var text    = ToText(args[0]);
        var oldText = ToText(args[1]);
        var newText = ToText(args[2]);

        if (oldText.Length == 0) return new TextValue(text);

        if (args.Count > 3)
        {
            // Replace the Nth occurrence only
            int instanceNum = (int)ToNumber(args[3]);
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
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum   = args.Count > 2 ? (int)ToNumber(args[2]) : 1;
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (startIdx >= withinText.Length) return ErrorValue.Value;
        int pos = withinText.IndexOf(findText, startIdx, StringComparison.Ordinal);
        if (pos < 0) return ErrorValue.Value;
        return new NumberValue(pos + 1);
    }

    private static ScalarValue Search(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var findText   = ToText(args[0]);
        var withinText = ToText(args[1]);
        int startNum   = args.Count > 2 ? (int)ToNumber(args[2]) : 1;
        if (startNum < 1) return ErrorValue.Value;
        int startIdx = startNum - 1;
        if (startIdx > withinText.Length) return ErrorValue.Value;

        // Convert Excel wildcard pattern to regex
        var sb = new System.Text.StringBuilder("(?i)");
        foreach (var ch in findText)
        {
            switch (ch)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:  sb.Append(System.Text.RegularExpressions.Regex.Escape(ch.ToString())); break;
            }
        }
        var match = System.Text.RegularExpressions.Regex.Match(
            withinText[startIdx..], sb.ToString());
        if (!match.Success) return ErrorValue.Value;
        return new NumberValue(startIdx + match.Index + 1);
    }

    private static ScalarValue Mid(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text    = ToText(args[0]);
        int start   = (int)ToNumber(args[1]) - 1; // 1-based → 0-based
        int numChars = (int)ToNumber(args[2]);
        if (start < 0 || numChars < 0) return ErrorValue.Value;
        if (start >= text.Length) return new TextValue("");
        int actualLen = Math.Min(numChars, text.Length - start);
        return new TextValue(text.Substring(start, actualLen));
    }

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        var text  = ToText(args[0]);
        int times = (int)ToNumber(args[1]);
        if (times < 0) return ErrorValue.Value;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < times; i++) sb.Append(text);
        return new TextValue(sb.ToString());
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is NumberValue nv) return nv;
        var text = ToText(args[0]);
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
        try
        {
            var dt = new DateTime(year, month, day);
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
        var dt = OADateToDateTime(args[0]);
        int returnType = args.Count > 1 ? (int)ToNumber(args[1]) : 1;
        int dow = (int)dt.DayOfWeek; // 0=Sunday...6=Saturday
        return returnType switch
        {
            2 => new NumberValue(dow == 0 ? 7 : dow),          // Mon=1..Sun=7
            3 => new NumberValue(dow == 0 ? 6 : dow - 1),      // Mon=0..Sun=6
            _ => new NumberValue(dow + 1)                       // Sun=1..Sat=7 (default)
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
        int bottom = (int)ToNumber(args[0]);
        int top    = (int)ToNumber(args[1]);
        if (bottom > top) return ErrorValue.Num;
        return new NumberValue(Random.Shared.Next(bottom, top + 1));
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
        if (n < 0) return ErrorValue.Num;
        int ni = (int)Math.Round(n);
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
        var nums = range.Flatten()
            .OfType<NumberValue>()
            .Select(nv => nv.Value)
            .OrderByDescending(x => x)
            .ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Small(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is not RangeValue range) return ErrorValue.Value;
        if (args[1] is ErrorValue e1) return e1;
        int k = (int)ToNumber(args[1]);
        var nums = range.Flatten()
            .OfType<NumberValue>()
            .Select(nv => nv.Value)
            .OrderBy(x => x)
            .ToList();
        if (k < 1 || k > nums.Count) return ErrorValue.Num;
        return new NumberValue(nums[k - 1]);
    }

    private static ScalarValue Rank(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is not RangeValue range) return ErrorValue.Value;
        var number = ToNumber(args[0]);
        int order  = args.Count > 2 ? (int)ToNumber(args[2]) : 0;

        var nums = range.Flatten()
            .OfType<NumberValue>()
            .Select(nv => nv.Value)
            .ToList();

        if (!nums.Contains(number)) return ErrorValue.NA;

        int rank;
        if (order == 0)
            rank = nums.Count(x => x > number) + 1;  // descending
        else
            rank = nums.Count(x => x < number) + 1;  // ascending

        return new NumberValue(rank);
    }

    private static ScalarValue Stdev(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var nums = new List<double>();
        foreach (var a in args)
        {
            if (a is ErrorValue err) return err;
            if (a is NumberValue nv) nums.Add(nv.Value);
            // blanks and text are ignored
        }
        if (nums.Count < 2) return ErrorValue.DivByZero;
        double mean = nums.Average();
        double variance = nums.Sum(x => (x - mean) * (x - mean)) / (nums.Count - 1);
        return new NumberValue(Math.Sqrt(variance));
    }

    private static ScalarValue Median(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var nums = new List<double>();
        foreach (var a in args)
        {
            if (a is ErrorValue err) return err;
            if (a is NumberValue nv) nums.Add(nv.Value);
        }
        if (nums.Count == 0) return ErrorValue.Value;
        nums.Sort();
        int mid = nums.Count / 2;
        if (nums.Count % 2 == 1)
            return new NumberValue(nums[mid]);
        return new NumberValue((nums[mid - 1] + nums[mid]) / 2.0);
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

    private static bool ScalarEquals(ScalarValue a, ScalarValue b)
    {
        if (a is NumberValue na && b is NumberValue nb)
            return na.Value == nb.Value;
        if (a is TextValue ta && b is TextValue tb)
            return string.Equals(ta.Value, tb.Value, StringComparison.OrdinalIgnoreCase);
        if (a is BoolValue ba && b is BoolValue bb)
            return ba.Value == bb.Value;
        return false;
    }
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
}
