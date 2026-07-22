using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Logical and conditional functions.

    private static ScalarValue If(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        var condition = ToBool(args[0]);
        if (condition)
            return args[1];
        return args.Count > 2 ? args[2] : new BoolValue(false);
    }

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

    private static ScalarValue And(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Excel evaluates every argument before combining them, so an error anywhere in the
        // argument list (including inside a referenced range) always wins, even when an earlier
        // argument already determines the boolean result (e.g. AND(FALSE, 1/0) is #DIV/0!, not
        // FALSE). Scan for that first, before any short-circuiting on a determining value.
        if (FirstLogicalArgError(args) is { } firstError) return firstError;

        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out _))
                {
                    hadUsableValue = true;
                    if (!value) return new BoolValue(false);
                }
                continue;
            }
            if (!TryDirectLogicalBool(arg, out var direct)) return ErrorValue.Value;
            hadUsableValue = true;
            if (!direct) return new BoolValue(false);
        }
        return hadUsableValue ? new BoolValue(true) : ErrorValue.Value;
    }

    private static ScalarValue Or(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // See And() above: an error anywhere in the argument list must propagate even when an
        // earlier argument already determines the boolean result (e.g. OR(TRUE, 1/0) is #DIV/0!,
        // not TRUE).
        if (FirstLogicalArgError(args) is { } firstError) return firstError;

        bool hadUsableValue = false;
        foreach (var arg in args)
        {
            if (arg is ReferencedScalarValue referenced)
            {
                if (TryReferencedBool(referenced, out bool value, out _))
                {
                    hadUsableValue = true;
                    if (value) return new BoolValue(true);
                }
                continue;
            }
            if (!TryDirectLogicalBool(arg, out var direct)) return ErrorValue.Value;
            hadUsableValue = true;
            if (direct) return new BoolValue(true);
        }
        return hadUsableValue ? new BoolValue(false) : ErrorValue.Value;
    }

    // Scans every flattened argument (including range members wrapped in ReferencedScalarValue)
    // for an error and returns the first one found, in argument order.
    private static ErrorValue? FirstLogicalArgError(IReadOnlyList<ScalarValue> args)
    {
        foreach (var arg in args)
        {
            if (arg is ErrorValue e) return e;
            if (arg is ReferencedScalarValue { Value: ErrorValue re }) return re;
        }
        return null;
    }

    private static ScalarValue Not(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, NotScalar);
        return NotScalar(args[0]);
    }

    private static ScalarValue NotScalar(ScalarValue value)
    {
        if (value is ErrorValue err) return err;
        // A blank argument (e.g. NOT() on an empty cell) coerces to FALSE, same as ToBool did.
        // TryDirectLogicalBool has no blank case (AND/OR/XOR handle blank cells separately via
        // ReferencedScalarValue), so preserve that behavior explicitly before delegating.
        if (value is BlankValue) return new BoolValue(true);
        // Route through the same direct-logical coercion AND/OR/XOR use, so a numeric-text
        // argument (e.g. "1"/"0") coerces to its numeric value instead of erroring, while
        // genuinely non-numeric text still yields #VALUE! (ToBool has no numeric-text case).
        if (!TryDirectLogicalBool(value, out var direct)) return ErrorValue.Value;
        return new BoolValue(!direct);
    }

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
            if (a is BlankValue) continue; // blank = FALSE, skip (no effect on XOR)
            // Route direct scalars through the same coercion AND/OR use, so a direct numeric-text
            // literal (e.g. "1"/"0") coerces to its numeric value instead of erroring, while
            // genuinely non-numeric text still yields #VALUE!.
            if (!TryDirectLogicalBool(a, out var direct)) return ErrorValue.Value;
            hadUsableValue = true;
            result ^= direct;
        }
        return hadUsableValue ? new BoolValue(result) : ErrorValue.Value;
    }

    private static ScalarValue TrueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(true);

    private static ScalarValue FalseFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(false);

    private static ScalarValue IsOmitted(IReadOnlyList<ScalarValue> args, IEvalContext ctx) =>
        new BoolValue(args[0] is OmittedLambdaArgumentValue);
}
