using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public sealed partial class FormulaEvaluator
{
    private ScalarValue EvaluateBinaryOp(BinaryOpNode node, IEvalContext context)
    {
        if (IsArithmeticOperator(node.Operator) &&
            TryEvaluateNumericScalar(node, context, out var numericResult, out var numericError) != NumericScalarEvaluationState.Unsupported)
        {
            return numericError is not null ? numericError : NumberValueFor(numericResult);
        }

        var left = EvaluateArrayOperand(node.Left, context);
        var right = EvaluateArrayOperand(node.Right, context);

        // Propagate errors
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;

        return node.Operator switch
        {
            BinaryOperator.Add => ArithOp(left, right, ArithmeticKind.Add),
            BinaryOperator.Subtract => ArithOp(left, right, ArithmeticKind.Subtract),
            BinaryOperator.Multiply => ArithOp(left, right, ArithmeticKind.Multiply),
            BinaryOperator.Divide => DivideOp(left, right),
            BinaryOperator.Power => PowerOp(left, right),
            BinaryOperator.Concatenate => ConcatOp(left, right),
            BinaryOperator.Equal => CompareOpEqual(left, right),
            BinaryOperator.NotEqual => CompareOpNotEqual(left, right),
            BinaryOperator.LessThan => CompareOpLessThan(left, right),
            BinaryOperator.GreaterThan => CompareOpGreaterThan(left, right),
            BinaryOperator.LessOrEqual => CompareOpLessOrEqual(left, right),
            BinaryOperator.GreaterOrEqual => CompareOpGreaterOrEqual(left, right),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown operator: {node.Operator}")
        };
    }

    private static bool IsArithmeticOperator(BinaryOperator op) =>
        op is BinaryOperator.Add
            or BinaryOperator.Subtract
            or BinaryOperator.Multiply
            or BinaryOperator.Divide
            or BinaryOperator.Power;

    private static NumericScalarEvaluationState TryEvaluateNumericScalar(
        FormulaNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        value = 0;
        error = null;

        switch (node)
        {
            case NumberNode number:
                value = number.Value;
                return NumericScalarEvaluationState.Value;
            case BooleanNode boolean:
                value = boolean.Value ? 1 : 0;
                return NumericScalarEvaluationState.Value;
            case StringNode text:
                if (ExcelTextNumberParser.TryParse(text.Value, out value))
                    return NumericScalarEvaluationState.Value;

                return NumericScalarEvaluationState.Unsupported;
            case ErrorNode errorNode:
                error = errorNode.Error;
                return NumericScalarEvaluationState.Error;
            case CellRefNode cell:
                return TryGetNumericCellValue(cell, context, out value, out error);
            case UnaryOpNode unary:
                return TryEvaluateNumericUnaryScalar(unary, context, out value, out error);
            case BinaryOpNode binary when IsArithmeticOperator(binary.Operator):
                return TryEvaluateNumericBinaryScalar(binary, context, out value, out error);
            default:
                return NumericScalarEvaluationState.Unsupported;
        }
    }

    private static NumericScalarEvaluationState TryGetNumericCellValue(
        CellRefNode cell,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        var scalar = cell.SheetName is not null
            ? context.GetCellValue(cell.SheetName, cell.Row, cell.ColumnNumber)
            : context.GetCellValue(cell.Row, cell.ColumnNumber);

        if (scalar is ErrorValue cellError)
        {
            value = 0;
            error = cellError;
            return NumericScalarEvaluationState.Error;
        }

        if (TryCoerceToNumberValue(scalar, out value))
        {
            error = null;
            return NumericScalarEvaluationState.Value;
        }

        error = null;
        return NumericScalarEvaluationState.Unsupported;
    }

    private static NumericScalarEvaluationState TryEvaluateNumericUnaryScalar(
        UnaryOpNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        var operandState = TryEvaluateNumericScalar(node.Operand, context, out value, out error);
        if (operandState != NumericScalarEvaluationState.Value)
            return operandState;

        switch (node.Operator)
        {
            case UnaryOperator.Negate:
                value = -value;
                return NumericScalarEvaluationState.Value;
            case UnaryOperator.Percent:
                value /= 100.0;
                return NumericScalarEvaluationState.Value;
            default:
                return NumericScalarEvaluationState.Unsupported;
        }
    }

    private static NumericScalarEvaluationState TryEvaluateNumericBinaryScalar(
        BinaryOpNode node,
        IEvalContext context,
        out double value,
        out ErrorValue? error)
    {
        value = 0;
        var leftState = TryEvaluateNumericScalar(node.Left, context, out var left, out var leftError);
        var rightState = TryEvaluateNumericScalar(node.Right, context, out var right, out var rightError);

        if (leftState == NumericScalarEvaluationState.Unsupported ||
            rightState == NumericScalarEvaluationState.Unsupported)
        {
            error = null;
            return NumericScalarEvaluationState.Unsupported;
        }

        if (leftState == NumericScalarEvaluationState.Error)
        {
            error = leftError;
            return NumericScalarEvaluationState.Error;
        }

        if (rightState == NumericScalarEvaluationState.Error)
        {
            error = rightError;
            return NumericScalarEvaluationState.Error;
        }

        if (node.Operator == BinaryOperator.Divide && right == 0)
        {
            error = ErrorValue.DivByZero;
            return NumericScalarEvaluationState.Error;
        }

        if (node.Operator == BinaryOperator.Power && left == 0 && right <= 0)
        {
            error = right == 0 ? ErrorValue.Num : ErrorValue.DivByZero;
            return NumericScalarEvaluationState.Error;
        }

        value = node.Operator switch
        {
            BinaryOperator.Add => left + right,
            BinaryOperator.Subtract => left - right,
            BinaryOperator.Multiply => left * right,
            BinaryOperator.Divide => left / right,
            BinaryOperator.Power => Math.Pow(left, right),
            _ => 0
        };
        value = RoundTo15SignificantDigits(value);

        if (double.IsFinite(value))
        {
            error = null;
            return NumericScalarEvaluationState.Value;
        }

        error = ErrorValue.Num;
        return NumericScalarEvaluationState.Error;
    }

    private enum NumericScalarEvaluationState
    {
        Unsupported,
        Value,
        Error
    }


    private static ScalarValue PowerOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, PowerScalarOp);

    private static ScalarValue PowerScalarOp(ScalarValue left, ScalarValue right)
    {
        if (left is NumberValue leftNumber && right is NumberValue rightNumber)
            return PowerNumberValues(leftNumber.Value, rightNumber.Value);

        if (!TryCoerceToNumberValue(left, out var baseVal)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var exp)) return NumericCoercionError(right);
        return PowerNumberValues(baseVal, exp);
    }

    private static ScalarValue PowerNumberValues(double baseVal, double exp)
    {
        if (baseVal == 0 && exp <= 0) return exp == 0 ? ErrorValue.Num : ErrorValue.DivByZero;
        double result = RoundTo15SignificantDigits(Math.Pow(baseVal, exp));
        return double.IsFinite(result) ? NumberValueFor(result) : ErrorValue.Num;
    }

    private static ScalarValue ArithOp(ScalarValue left, ScalarValue right, ArithmeticKind kind)
    {
        if (left is not RangeValue && right is not RangeValue)
            return ArithScalarOp(left, right, kind);

        return kind switch
        {
            ArithmeticKind.Add => ElementwiseOp(left, right, AddScalarOp),
            ArithmeticKind.Subtract => ElementwiseOp(left, right, SubtractScalarOp),
            _ => ElementwiseOp(left, right, MultiplyScalarOp)
        };
    }

    private static ScalarValue AddScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Add);

    private static ScalarValue SubtractScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Subtract);

    private static ScalarValue MultiplyScalarOp(ScalarValue left, ScalarValue right) =>
        ArithScalarOp(left, right, ArithmeticKind.Multiply);

    private static ScalarValue ArithScalarOp(ScalarValue left, ScalarValue right, ArithmeticKind kind)
    {
        if (left is NumberValue leftNumberValue && right is NumberValue rightNumberValue)
            return ArithNumberValues(leftNumberValue.Value, rightNumberValue.Value, kind);

        if (!TryCoerceToNumberValue(left, out var leftNumber)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var rightNumber)) return NumericCoercionError(right);
        return ArithNumberValues(leftNumber, rightNumber, kind);
    }

    private static ScalarValue ArithNumberValues(double leftNumber, double rightNumber, ArithmeticKind kind)
    {
        double result = kind switch
        {
            ArithmeticKind.Add => leftNumber + rightNumber,
            ArithmeticKind.Subtract => leftNumber - rightNumber,
            _ => leftNumber * rightNumber
        };
        result = RoundTo15SignificantDigits(result);
        return double.IsFinite(result) ? NumberValueFor(result) : ErrorValue.Num;
    }

    private static ScalarValue DivideOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, DivideScalarOp);

    private static ScalarValue DivideScalarOp(ScalarValue left, ScalarValue right)
    {
        if (left is NumberValue leftNumber && right is NumberValue rightNumber)
            return DivideNumberValues(leftNumber.Value, rightNumber.Value);

        if (!TryCoerceToNumberValue(left, out var dividend)) return NumericCoercionError(left);
        if (!TryCoerceToNumberValue(right, out var divisor)) return NumericCoercionError(right);
        return DivideNumberValues(dividend, divisor);
    }

    private static ScalarValue DivideNumberValues(double dividend, double divisor)
    {
        if (divisor == 0) return ErrorValue.DivByZero;
        double result = RoundTo15SignificantDigits(dividend / divisor);
        return double.IsFinite(result) ? NumberValueFor(result) : ErrorValue.Num;
    }

    private static ScalarValue ConcatOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, ConcatScalarOp);

    private static ScalarValue ConcatScalarOp(ScalarValue left, ScalarValue right)
    {
        // Per-element error propagation: when concatenating over arrays (e.g. A1:A3&"x"),
        // an error value inside the range must propagate as that error, not be stringified
        // to its error code text (matching every other elementwise operator's behavior).
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var text = ValueToString(left) + ValueToString(right);
        // Excel caps any cell's text content (including formula results) at 32,767 characters --
        // CONCAT/CONCATENATE/TEXTJOIN/REPT already enforce this via TextResult()/ExceedsExcelTextLimit();
        // the `&` operator is semantically identical to CONCATENATE and must match (R119).
        return BuiltInFunctions.ExceedsExcelTextLimit(text) ? ErrorValue.Value : new TextValue(text);
    }

    private static ScalarValue ElementwiseOp(
        ScalarValue left,
        ScalarValue right,
        Func<ScalarValue, ScalarValue, ScalarValue> scalarOp)
    {
        var leftRange = left as RangeValue;
        var rightRange = right as RangeValue;
        if (leftRange is null && rightRange is null)
            return scalarOp(left, right);

        if (leftRange is RangeValue lr && rightRange is RangeValue rr)
        {
            // P77: Excel does not abort a whole elementwise operation just because the two
            // operand arrays have mismatched (non-1, non-equal) dimensions on an axis — it
            // expands the result to the bounding (Max) shape per axis and pads whichever operand
            // runs out of rows/columns with #N/A for the uncovered cells, so e.g. {A1:A2}+{B1:B3}
            // (2x1 + 3x1) yields a 3x1 spill {A1+B1; A2+B2; #N/A} rather than one scalar #VALUE!.
            var rowCount = Math.Max(lr.RowCount, rr.RowCount);
            var colCount = Math.Max(lr.ColCount, rr.ColCount);

            var cells = new ScalarValue[rowCount, colCount];
            for (var row = 0; row < rowCount; row++)
            {
                // A dimension of 1 always broadcasts (index 0 for every row/col); otherwise the
                // operand only covers this row/col if it actually extends that far.
                var leftRowInBounds = lr.RowCount == 1 || row < lr.RowCount;
                var rightRowInBounds = rr.RowCount == 1 || row < rr.RowCount;

                for (var col = 0; col < colCount; col++)
                {
                    var leftColInBounds = lr.ColCount == 1 || col < lr.ColCount;
                    var rightColInBounds = rr.ColCount == 1 || col < rr.ColCount;

                    cells[row, col] = leftRowInBounds && leftColInBounds && rightRowInBounds && rightColInBounds
                        ? scalarOp(
                            lr.Cells[lr.RowCount == 1 ? 0 : row, lr.ColCount == 1 ? 0 : col],
                            rr.Cells[rr.RowCount == 1 ? 0 : row, rr.ColCount == 1 ? 0 : col])
                        : ErrorValue.NA;
                }
            }

            return new RangeValue(cells, lr.StartRow, lr.StartCol) { SheetName = lr.SheetName };
        }

        var range = leftRange ?? rightRange!;
        var scalar = leftRange is null ? left : right;
        var scalarOnLeft = leftRange is null;
        var result = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
        {
            for (var col = 0; col < range.ColCount; col++)
            {
                var rangeValue = range.Cells[row, col];
                result[row, col] = scalarOnLeft
                    ? scalarOp(scalar, rangeValue)
                    : scalarOp(rangeValue, scalar);
            }
        }

        return new RangeValue(result, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }

    private enum ArithmeticKind
    {
        Add,
        Subtract,
        Multiply
    }

    private static ScalarValue CompareOpEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpEqual);
    }

    private static ScalarValue CompareScalarOpEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp == 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpNotEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpNotEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpNotEqual);
    }

    private static ScalarValue CompareScalarOpNotEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp != 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpLessThan(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpLessThan(left, right);

        return ElementwiseOp(left, right, CompareScalarOpLessThan);
    }

    private static ScalarValue CompareScalarOpLessThan(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp < 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpGreaterThan(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpGreaterThan(left, right);

        return ElementwiseOp(left, right, CompareScalarOpGreaterThan);
    }

    private static ScalarValue CompareScalarOpGreaterThan(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp > 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpLessOrEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpLessOrEqual);
    }

    private static ScalarValue CompareScalarOpLessOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp <= 0 ? TrueValue : FalseValue;
    }

    private static ScalarValue CompareOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is not RangeValue && right is not RangeValue)
            return CompareScalarOpGreaterOrEqual(left, right);

        return ElementwiseOp(left, right, CompareScalarOpGreaterOrEqual);
    }

    private static ScalarValue CompareScalarOpGreaterOrEqual(ScalarValue left, ScalarValue right)
    {
        if (left is ErrorValue errL) return errL;
        if (right is ErrorValue errR) return errR;
        var cmp = CompareValues(left, right);
        return cmp >= 0 ? TrueValue : FalseValue;
    }

    private static int CompareValues(ScalarValue left, ScalarValue right)
    {
        // Blank coercion: Excel coerces a blank operand to match the other operand's type class
        // so that =A1=0, =A1="", and =A1=FALSE all return TRUE when A1 is empty.
        bool lBlank = left is BlankValue;
        bool rBlank = right is BlankValue;
        if (lBlank && !rBlank)
            return CompareValues(CoerceBlankTo(right), right);
        if (rBlank && !lBlank)
            return CompareValues(left, CoerceBlankTo(left));
        // blank vs blank falls through — both will be BlankValue and TypeOrder gives 0==0.

        // Numbers and dates compare as numbers (dates are OADate serial numbers)
        bool lNum = left is NumberValue or DateTimeValue;
        bool rNum = right is NumberValue or DateTimeValue;
        if (lNum && rNum)
        {
            double lv = left is DateTimeValue ld ? ld.Value : ((NumberValue)left).Value;
            double rv = right is DateTimeValue rd ? rd.Value : ((NumberValue)right).Value;
            // Excel rounds numeric values to 15 significant digits before comparison.
            // This matches Excel's well-known behavior where, for example,
            // SUMPRODUCT(1/COUNTIFS(A,A)) = 6 even though raw double gives 5.999999999999998.
            lv = RoundToExcel15SigDigits(lv);
            rv = RoundToExcel15SigDigits(rv);
            return lv.CompareTo(rv);
        }
        if (left is TextValue lt && right is TextValue rt)
            return string.Compare(lt.Value, rt.Value, StringComparison.OrdinalIgnoreCase);
        if (left is BoolValue lb && right is BoolValue rb)
            return lb.Value.CompareTo(rb.Value);

        // Mixed types: numbers/dates < text < booleans (Excel convention)
        return TypeOrder(left).CompareTo(TypeOrder(right));
    }

    /// <summary>
    /// Round a double to 15 significant digits using O(1) scaled arithmetic — no string
    /// formatting, no parsing, no allocation. This is the hot path used on every +,-,*,/,^
    /// binary arithmetic result (see ArithNumberValues/DivideNumberValues/PowerNumberValues/
    /// TryEvaluateNumericBinaryScalar), so it must stay allocation-free: a prior attempt at
    /// this fix (round 75) rounded via value.ToString("G15") + double.TryParse on every
    /// arithmetic op and was reverted for the resulting perf regression.
    ///
    /// This mirrors Excel's own documented behavior of storing/computing with 15 significant
    /// decimal digits of precision, which is why e.g. =0.1+0.2-0.3 evaluates to exactly 0 in
    /// Excel (not the raw IEEE-754 double's ~5.5e-17).
    ///
    /// Algorithm: find the decimal exponent of the value (floor(log10(|value|))), which gives
    /// the number of decimal places needed to retain exactly 15 significant digits. When that
    /// count is in the [0,15] range supported natively by Math.Round(double,int,MidpointRounding)
    /// we call it directly; otherwise (very large or very small magnitudes) we scale by a power
    /// of 10, round to the nearest integer, and unscale. A value whose scale factor would over/
    /// underflow double range (only reachable at ~1e295+ tiny magnitudes) is returned unrounded,
    /// since IEEE-754 double precision itself is already the limiting factor there.
    /// </summary>
    internal static double RoundTo15SignificantDigits(double value)
    {
        if (value == 0 || !double.IsFinite(value)) return value;

        double absValue = Math.Abs(value);
        int exponent = (int)Math.Floor(Math.Log10(absValue));
        int decimals = 14 - exponent;

        double result;
        if (decimals >= 0 && decimals <= 15)
        {
            // Common case: magnitudes from ~1e-15 up through ~1e14. Math.Round(double,int,
            // MidpointRounding) natively supports 0-15 decimal digits.
            result = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        }
        else if (decimals < 0)
        {
            // Large magnitude (>= ~1e15): scale down so the 15 significant digits land at the
            // integer position, round to the nearest integer, then scale back up.
            double scale = Math.Pow(10, -decimals);
            result = Math.Round(value / scale, MidpointRounding.AwayFromZero) * scale;
        }
        else
        {
            // Tiny magnitude (< ~1e-15): scale up so the 15 significant digits land at the
            // integer position, round to the nearest integer, then scale back down.
            double scale = Math.Pow(10, decimals);
            if (!double.IsFinite(scale) || scale == 0)
                return value; // magnitude too extreme to scale losslessly; already at double's limit.
            result = Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
        }

        // Guard: if scaling pushed the result to overflow/underflow (only possible at the
        // extreme edges of double range), fall back to the original unrounded value rather
        // than propagate NaN/Infinity from this helper.
        return double.IsFinite(result) ? result : value;
    }

    /// <summary>
    /// Round a double to 15 significant digits, matching Excel's numeric comparison behavior.
    /// Excel rounds numbers to 15 significant digits before comparing, so that results of
    /// floating-point arithmetic that are very close to an exact value compare as equal.
    /// For example: SUMPRODUCT(1/COUNTIFS(A,A)) yields ~5.999999999999998 in raw double,
    /// but rounded to 15 sig digits = 6, matching Excel's answer.
    /// </summary>
    private static double RoundToExcel15SigDigits(double value)
    {
        if (!double.IsFinite(value) || value == 0) return value;
        // Use G15 format round-trip: equivalent to what Excel does internally.
        // G15 outputs the shortest representation with at most 15 significant digits,
        // then parsing back gives the nearest double to that 15-sig-digit decimal.
        if (double.TryParse(
                value.ToString("G15", System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double rounded))
            return rounded;
        return value;
    }

    /// <summary>
    /// Returns the zero/empty/false value of the same type class as <paramref name="other"/>,
    /// used to coerce a blank operand before a comparison.
    /// Number/DateTime → 0, Text → "", Bool → FALSE, anything else → blank (unchanged).
    /// </summary>
    private static ScalarValue CoerceBlankTo(ScalarValue other) => other switch
    {
        NumberValue or DateTimeValue => CachedIntegerNumberValues[0],
        TextValue => EmptyTextValue,
        BoolValue => FalseValue,
        _ => BlankValue.Instance
    };

    private static int TypeOrder(ScalarValue v) => v switch
    {
        BlankValue => 0,
        NumberValue or DateTimeValue => 1,
        TextValue => 2,
        BoolValue => 3,
        _ => 4
    };

    private ScalarValue EvaluateUnaryOp(UnaryOpNode node, IEvalContext context)
    {
        var operand = EvaluateArrayOperand(node.Operand, context);
        if (operand is ErrorValue err) return err;

        return node.Operator switch
        {
            UnaryOperator.ImplicitIntersection => ImplicitIntersectionOp(node.Operand, operand, context),
            UnaryOperator.Negate => NegateOp(operand),
            UnaryOperator.Percent => PercentOp(operand),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown unary operator: {node.Operator}")
        };
    }

    /// <summary>
    /// Back-compat overload used by SINGLE() (FormulaEvaluator.Functions.cs's EvaluateSingle), which
    /// only has the already-evaluated scalar in hand, not the operand AST node — so it can't use the
    /// AST-based reference-vs-computed-array whitelist that the AST-aware @ overload below uses.
    /// Instead it falls back to the RangeValue.IsSheetReference provenance flag (set only when the
    /// range was actually rooted to worksheet cells, e.g. FormulaEvaluator.References.cs): when the
    /// argument is a computed/dynamic array (IsSheetReference == false, e.g. SINGLE(SEQUENCE(3))),
    /// Excel returns the array's top-left element regardless of the formula cell's own position —
    /// there's no "formula cell row/col" to intersect against. Only a genuine worksheet reference
    /// falls through to the pre-existing positional intersection.
    /// </summary>
    private static ScalarValue ImplicitIntersectionOp(ScalarValue value, IEvalContext context)
    {
        if (value is not RangeValue range)
            return value;

        if (!range.IsSheetReference)
            return range.RowCount > 0 && range.ColCount > 0 ? range.Cells[0, 0] : ErrorValue.Value;

        if (context.CurrentCellAddress is not { } currentCell)
            return ErrorValue.Value;

        return ResolveImplicitIntersection(range, currentCell);
    }

    /// <summary>
    /// The @ (implicit intersection) operator. Excel's rule: applied to a genuine REFERENCE
    /// expression (a bare range, full row/column, named range, or structured/table reference), @
    /// positionally intersects that reference against the formula cell's own row/col. Applied to
    /// anything else — a computed/dynamic-array result such as a function call (=@SEQUENCE(3)), an
    /// arithmetic expression, or an array constant — Excel instead returns the array's TOP-LEFT
    /// element; there is no "formula cell row/col" to intersect against because the array isn't
    /// anchored to worksheet cells.
    ///
    /// <paramref name="operandNode"/> is the *unevaluated* AST node so this can distinguish the two
    /// cases syntactically. The reference-node whitelist below intentionally mirrors the one already
    /// used by <see cref="EvaluateSpilling"/> (FormulaEvaluator.cs) to decide which top-level nodes
    /// are "reference-like" versus already-materialized-array — reusing a proven categorization
    /// rather than introducing a new (and previously bug-prone — see prior "RangeValue.SheetName is
    /// null" attempts, which also matched same-sheet bare references) runtime signal.
    /// </summary>
    private static ScalarValue ImplicitIntersectionOp(FormulaNode operandNode, ScalarValue value, IEvalContext context)
    {
        if (value is not RangeValue range)
            return value;

        // ANCHORARRAY(ref) / ANCHORARRAY(ref, end) is the internal shape the parser produces for the
        // '#' spill-anchor operator (A1# / A1#:B5, see Parser.cs's WrapSpillAnchor) — a genuine
        // worksheet reference to a spill range, not a computed array, so it belongs in this whitelist
        // alongside the other reference node kinds.
        //
        // NamedRangeNode and the OFFSET/INDEX/INDIRECT/CHOOSE function calls are special-cased
        // separately below rather than folded into this static whitelist: a bare name (=@MySeq, or
        // a LAMBDA parameter such as =LAMBDA(x,@x)(...)) is ambiguous from AST shape alone — it can
        // resolve to a genuine worksheet reference (a name bound to =Sheet1!$A$1:$A$3, or a LAMBDA
        // parameter bound to such a range) or to a computed array (a name bound to =SEQUENCE(3), or
        // a LAMBDA parameter bound to a computed-array argument). Likewise OFFSET/INDEX/INDIRECT
        // return a genuine worksheet reference when called in their reference-returning shape (e.g.
        // =@OFFSET($A$1,1,0,3,1), =@INDEX(A1:A3,0)) but the underlying RangeValue provenance is the
        // only way to tell that apart from, say, INDEX's array-constant form; CHOOSE similarly just
        // forwards whichever branch it selects, which may itself be a worksheet reference or a
        // computed array. Only the resolved RangeValue's IsSheetReference provenance flag (already
        // computed by EvaluateArrayOperand into `range` above) can tell the two apart, mirroring the
        // FunctionCallNode ANCHORARRAY-vs-plain-computed-array distinction made here already — and
        // matching the sibling ImplicitIntersectionOp(ScalarValue,...) overload used by SINGLE(),
        // which branches on this same flag without needing the AST shape at all.
        bool isReferenceLikeOperand = operandNode switch
        {
            RangeRefNode or FullColumnRangeRefNode or FullRowRangeRefNode
                or StructuredReferenceNode or StructuredCurrentRowReferenceNode
                or FunctionCallNode { FunctionName: "ANCHORARRAY" } => true,
            NamedRangeNode => range.IsSheetReference,
            FunctionCallNode { FunctionName: "OFFSET" or "INDEX" or "INDIRECT" or "CHOOSE" } => range.IsSheetReference,
            _ => false,
        };

        if (!isReferenceLikeOperand)
        {
            // Computed/dynamic-array result: Excel returns the top-left element, regardless of the
            // formula cell's own position.
            return range.RowCount > 0 && range.ColCount > 0 ? range.Cells[0, 0] : ErrorValue.Value;
        }

        if (context.CurrentCellAddress is not { } currentCell)
            return ErrorValue.Value;

        return ResolveImplicitIntersection(range, currentCell);
    }

    private static ScalarValue ResolveImplicitIntersection(RangeValue range, CellAddress currentCell)
    {
        if (range.RowCount == 1 && range.ColCount == 1)
            return range.Cells[0, 0];

        var rowOffset = TryGetOffset(currentCell.Row, range.StartRow, range.RowCount);
        var colOffset = TryGetOffset(currentCell.Col, range.StartCol, range.ColCount);

        if (rowOffset is { } row && colOffset is { } col)
            return range.Cells[row, col];

        if (range.RowCount == 1 && colOffset is { } rowVectorCol)
            return range.Cells[0, rowVectorCol];

        if (range.ColCount == 1 && rowOffset is { } columnVectorRow)
            return range.Cells[columnVectorRow, 0];

        return ErrorValue.Value;
    }

    private static int? TryGetOffset(uint coordinate, uint start, int count)
    {
        var offset = (long)coordinate - start;
        return offset >= 0 && offset < count ? (int)offset : null;
    }

    private static ScalarValue NegateOp(ScalarValue v)
        => ElementwiseUnaryOp(v, NegateScalarOp);

    private static ScalarValue NegateScalarOp(ScalarValue v)
    {
        if (v is NumberValue numberValue)
            return NumberValueFor(-numberValue.Value);

        if (!TryCoerceToNumberValue(v, out var number)) return NumericCoercionError(v);
        return NumberValueFor(-number);
    }

    private static ScalarValue PercentOp(ScalarValue v)
        => ElementwiseUnaryOp(v, PercentScalarOp);

    private static ScalarValue PercentScalarOp(ScalarValue v)
    {
        if (v is NumberValue numberValue)
            return NumberValueFor(numberValue.Value / 100.0);

        if (!TryCoerceToNumberValue(v, out var number)) return NumericCoercionError(v);
        return NumberValueFor(number / 100.0);
    }

    private static ScalarValue ElementwiseUnaryOp(ScalarValue value, Func<ScalarValue, ScalarValue> scalarOp)
    {
        if (value is not RangeValue range)
            return scalarOp(value);

        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (var row = 0; row < range.RowCount; row++)
            for (var col = 0; col < range.ColCount; col++)
                cells[row, col] = scalarOp(range.Cells[row, col]);

        return new RangeValue(cells, range.StartRow, range.StartCol) { SheetName = range.SheetName };
    }


    private static ScalarValue CoerceToNumber(ScalarValue v) => v switch
    {
        ErrorValue e => e,
        NumberValue => v,
        BlankValue => NumberValueFor(0),
        BoolValue b => NumberValueFor(b.Value ? 1 : 0),
        TextValue t when ExcelTextNumberParser.TryParse(t.Value, out var d) =>
            NumberValueFor(d),
        TextValue => ErrorValue.Value,
        DateTimeValue dt => NumberValueFor(dt.Value),
        _ => ErrorValue.Value
    };

    private static bool TryCoerceToNumberValue(ScalarValue value, out double number)
    {
        if (value is NumberValue n)
        {
            number = n.Value;
            return true;
        }

        if (value is BoolValue b)
        {
            number = b.Value ? 1 : 0;
            return true;
        }

        if (value is BlankValue)
        {
            number = 0;
            return true;
        }

        if (value is DateTimeValue dt)
        {
            number = dt.Value;
            return true;
        }

        if (value is TextValue t && ExcelTextNumberParser.TryParse(t.Value, out var parsed))
        {
            number = parsed;
            return true;
        }

        number = 0;
        return false;
    }

    private static ErrorValue NumericCoercionError(ScalarValue value) =>
        value is ErrorValue error ? error : ErrorValue.Value;

    private static string ValueToString(ScalarValue v) => v switch
    {
        TextValue t => t.Value,
        NumberValue n => BuiltInFunctions.NumberToExcelText(n.Value),
        DateTimeValue dt => BuiltInFunctions.NumberToExcelText(dt.Value),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    // ── LET / LAMBDA evaluation ────────────────────────────────────────────

}
