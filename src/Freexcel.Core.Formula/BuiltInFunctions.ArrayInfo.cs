using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Array and information functions: TRANSPOSE, TYPE, ERROR.TYPE.

    private static ScalarValue Transpose(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue err) return err;
        if (args[0] is not RangeValue rv) return args[0]; // scalar passes through
        int rows = rv.RowCount;
        int cols = rv.ColCount;
        var result = new ScalarValue[cols, rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[c, r] = rv.Cells[r, c];
        return new RangeValue(result);
    }

    private static ScalarValue TypeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        return args[0] switch
        {
            ErrorValue => new NumberValue(16),
            RangeValue => new NumberValue(64),
            BoolValue  => new NumberValue(4),
            TextValue or DirectTextLiteralValue => new NumberValue(2),
            NumberValue or DateTimeValue => new NumberValue(1),
            BlankValue => new NumberValue(1),
            _ => new NumberValue(1)
        };
    }

    private static ScalarValue ErrorTypeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is RangeValue range) return ErrorTypeRange(range);
        return ErrorTypeScalar(args[0]);
    }

    private static RangeValue ErrorTypeRange(RangeValue range)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                cells[r, c] = ErrorTypeScalar(range.Cells[r, c]);
        return new RangeValue(cells);
    }

    private static ScalarValue ErrorTypeScalar(ScalarValue value)
    {
        if (value is not ErrorValue ev) return ErrorValue.NA;
        return ev.Code switch
        {
            "#NULL!"  => new NumberValue(1),
            "#DIV/0!" => new NumberValue(2),
            "#VALUE!" => new NumberValue(3),
            "#REF!"   => new NumberValue(4),
            "#NAME?"  => new NumberValue(5),
            "#NUM!"   => new NumberValue(6),
            "#N/A"    => new NumberValue(7),
            "#GETTING_DATA" => new NumberValue(8),
            "#SPILL!" => new NumberValue(9),
            "#CALC!" => new NumberValue(14),
            _ => ErrorValue.NA
        };
    }
}
