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
        double result = Math.Pow(baseVal, exp);
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
        double result = dividend / divisor;
        return double.IsFinite(result) ? NumberValueFor(result) : ErrorValue.Num;
    }

    private static ScalarValue ConcatOp(ScalarValue left, ScalarValue right)
        => ElementwiseOp(left, right, (l, r) => new TextValue(ValueToString(l) + ValueToString(r)));

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
            if (!CanBroadcast(lr.RowCount, rr.RowCount) || !CanBroadcast(lr.ColCount, rr.ColCount))
                return ErrorValue.Value;

            var rowCount = Math.Max(lr.RowCount, rr.RowCount);
            var colCount = Math.Max(lr.ColCount, rr.ColCount);
            var cells = new ScalarValue[rowCount, colCount];
            for (var row = 0; row < rowCount; row++)
                for (var col = 0; col < colCount; col++)
                    cells[row, col] = scalarOp(
                        lr.Cells[lr.RowCount == 1 ? 0 : row, lr.ColCount == 1 ? 0 : col],
                        rr.Cells[rr.RowCount == 1 ? 0 : row, rr.ColCount == 1 ? 0 : col]);
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

    private static bool CanBroadcast(int left, int right) => left == right || left == 1 || right == 1;

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
        // Numbers and dates compare as numbers (dates are OADate serial numbers)
        bool lNum = left is NumberValue or DateTimeValue;
        bool rNum = right is NumberValue or DateTimeValue;
        if (lNum && rNum)
        {
            double lv = left is DateTimeValue ld ? ld.Value : ((NumberValue)left).Value;
            double rv = right is DateTimeValue rd ? rd.Value : ((NumberValue)right).Value;
            return lv.CompareTo(rv);
        }
        if (left is TextValue lt && right is TextValue rt)
            return string.Compare(lt.Value, rt.Value, StringComparison.OrdinalIgnoreCase);
        if (left is BoolValue lb && right is BoolValue rb)
            return lb.Value.CompareTo(rb.Value);

        // Mixed types: numbers/dates < text < booleans (Excel convention)
        return TypeOrder(left).CompareTo(TypeOrder(right));
    }

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
            UnaryOperator.Negate => NegateOp(operand),
            UnaryOperator.Percent => PercentOp(operand),
            _ => throw new FormulaEvalException("#VALUE!", $"Unknown unary operator: {node.Operator}")
        };
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
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        BlankValue => "",
        ErrorValue e => e.Code,
        _ => v.ToString() ?? ""
    };

    // ── LET / LAMBDA evaluation ────────────────────────────────────────────

}
