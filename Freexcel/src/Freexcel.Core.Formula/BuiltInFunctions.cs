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
        ["SUM"] = (Sum, 1, 255),
        ["AVERAGE"] = (Average, 1, 255),
        ["MIN"] = (Min, 1, 255),
        ["MAX"] = (Max, 1, 255),
        ["COUNT"] = (Count, 1, 255),
        ["COUNTA"] = (CountA, 1, 255),
        ["IF"] = (If, 2, 3),
        ["AND"] = (And, 1, 255),
        ["OR"] = (Or, 1, 255),
        ["NOT"] = (Not, 1, 1),
        ["ROUND"] = (Round, 2, 2),
        ["ABS"] = (Abs, 1, 1),
        ["CONCAT"] = (Concat, 1, 255),
        ["LEN"] = (Len, 1, 1),
        ["LEFT"] = (Left, 1, 2),
        ["RIGHT"] = (Right, 1, 2),
        ["NOW"] = (Now, 0, 0),
        ["TODAY"] = (Today, 0, 0),
        ["RAND"] = (Rand, 0, 0),
    };

    private static readonly HashSet<string> VolatileFunctions = ["NOW", "TODAY", "RAND"];

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
}
