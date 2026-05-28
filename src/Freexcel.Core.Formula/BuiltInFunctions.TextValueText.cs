using System.Globalization;
using System.Text;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue ValueToText(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetValueTextFormat(args, out int format, out var error))
            return error;

        if (args[0] is RangeValue { RowCount: 1, ColCount: 1 } singleCellRange)
            return TextResult(ValueText(singleCellRange.Cells[0, 0], format == 1));

        if (args[0] is RangeValue range)
            return TextResult(format == 1 ? StrictArrayText(range) : ConciseArrayText(range));

        return TextResult(ValueText(args[0], format == 1));
    }

    private static ScalarValue ArrayToText(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryGetValueTextFormat(args, out int format, out var error))
            return error;

        var range = args[0] as RangeValue ?? new RangeValue(new ScalarValue[1, 1] { { args[0] } });
        return TextResult(format == 1 ? StrictArrayText(range) : ConciseArrayText(range));
    }

    private static bool TryGetValueTextFormat(IReadOnlyList<ScalarValue> args, out int format, out ScalarValue error)
    {
        format = 0;
        error = ErrorValue.Value;
        if (args.Count < 2 || args[1] is BlankValue)
            return true;

        if (!TryGetScalarControlArgument(args[1], out var formatValue, out error))
            return false;

        var raw = ToNumber(formatValue);
        if (!double.IsFinite(raw))
            return false;

        format = (int)raw;
        return format is 0 or 1;
    }

    private static string ConciseArrayText(RangeValue range)
    {
        var parts = new List<string>(range.RowCount * range.ColCount);
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
                parts.Add(ValueText(range.Cells[r, c], strict: false));

        return string.Join(", ", parts);
    }

    private static string StrictArrayText(RangeValue range)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (int r = 0; r < range.RowCount; r++)
        {
            if (r > 0) sb.Append(';');
            for (int c = 0; c < range.ColCount; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(ValueText(range.Cells[r, c], strict: true));
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string ValueText(ScalarValue value, bool strict)
    {
        if (!strict)
            return ToText(value);

        return value switch
        {
            DirectTextLiteralValue t => QuoteValueText(t.Value),
            TextValue t => QuoteValueText(t.Value),
            BlankValue => QuoteValueText(""),
            NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
            DateTimeValue d => d.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            ErrorValue e => e.Code,
            _ => QuoteValueText(ToText(value))
        };
    }

    private static string QuoteValueText(string text) =>
        "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
