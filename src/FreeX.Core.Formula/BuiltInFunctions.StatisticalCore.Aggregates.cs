using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
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
            // R93-AREAS-union-value-model: a union reference argument (e.g. the
            // "(A1:A2,B1:B2)" in SUM((A1:A2,B1:B2))) evaluates to a UnionValue rather than a
            // RangeValue -- sum every cell across every area, same rules as a plain range
            // (text/blanks ignored, an error cell short-circuits the whole SUM).
            if (arg is UnionValue union)
            {
                foreach (var area in union.Areas)
                {
                    foreach (var cell in area.Flatten())
                    {
                        if (cell is ErrorValue cellErr) return cellErr;
                        if (cell is BlankValue or TextValue) continue;
                        total += ToNumber(cell);
                    }
                }
                continue;
            }
            if (arg is BlankValue or TextValue) continue; // SUM ignores text and blanks in ranges
            total += ToNumber(arg);
        }
        // Match the 15-significant-digit rounding applied after every +,-,*,/,^ binary
        // arithmetic result (FormulaEvaluator.Operators.cs) so that SUM(range) stays
        // interchangeable with the textually-expanded chain of + over the same cells,
        // e.g. SUM(A1:A30) == A1+A2+...+A30 when every cell holds the same rounded value.
        return NumberResult(FormulaEvaluator.RoundTo15SignificantDigits(total));
    }

    private static ScalarValue PercentOf(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var subset = PercentOfSum(args[0]);
        if (subset.Error is not null) return subset.Error;

        var total = PercentOfSum(args[1]);
        if (total.Error is not null) return total.Error;
        if (total.Sum == 0) return ErrorValue.DivByZero;

        return NumberResult(subset.Sum / total.Sum);
    }

    private static (double Sum, ErrorValue? Error) PercentOfSum(ScalarValue value)
    {
        if (value is ErrorValue e) return (0, e);
        if (value is RangeValue range)
        {
            double total = 0;
            foreach (var cell in range.Flatten())
            {
                if (cell is ErrorValue cellError) return (0, cellError);
                if (TryCellNumber(cell, out var number)) total += number;
            }

            return (total, null);
        }

        return (ToNumber(value), null);
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
            // R93-AREAS-union-value-model: see the identical Sum() case above.
            if (arg is UnionValue union)
            {
                foreach (var area in union.Areas)
                {
                    foreach (var cell in area.Flatten())
                    {
                        if (cell is ErrorValue cellErr) return cellErr;
                        if (cell is BlankValue or TextValue) continue;
                        total += ToNumber(cell);
                        count++;
                    }
                }
                continue;
            }
            if (arg is BlankValue or TextValue) continue;
            total += ToNumber(arg);
            count++;
        }
        return count == 0 ? ErrorValue.DivByZero : NumberResult(total / count);
    }

    private static ScalarValue AverageA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double total = 0;
        int count = 0;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
            {
                total += value;
                count++;
            }
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

    private static ScalarValue MinA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? min = null;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
                if (min is null || value < min) min = value;
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

    private static ScalarValue MaxA(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        double? max = null;
        foreach (var arg in args)
        {
            var (values, error) = CollectAValues(arg);
            if (error is not null) return error;
            foreach (var value in values)
                if (max is null || value > max) max = value;
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
            // A bare (non-reference) erroring argument propagates, e.g. =COUNTA(NA()); but an
            // error arriving through a cell/range reference is wrapped in ReferencedScalarValue
            // (COUNTA is a ReferenceProvenanceAggregate) and is still counted as non-blank,
            // matching Excel and mirroring Count()'s direct-vs-range-sourced error asymmetry above.
            if (arg is ErrorValue err) return err;
            var value = arg is ReferencedScalarValue referenced ? referenced.Value : arg;
            if (value is not BlankValue)
                count++;
        }
        return new NumberValue(count);
    }

}
