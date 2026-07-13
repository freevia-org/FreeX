using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Concatenation, T, and hyperlink text functions.

    private static ScalarValue Concat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var arg in args)
        {
            if (arg is ErrorValue err) return err;
            if (arg is RangeValue range)
            {
                foreach (var cell in range.Flatten())
                {
                    if (cell is ErrorValue cellError) return cellError;
                    sb.Append(ToText(cell));
                }

                continue;
            }

            sb.Append(ToText(arg));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Concatenate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        var rangeIndex = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is ErrorValue e) return e;
            if (args[i] is RangeValue)
            {
                if (rangeIndex >= 0) return ErrorValue.Value;
                rangeIndex = i;
            }
        }

        if (rangeIndex >= 0)
            return MapConcatenateRange((RangeValue)args[rangeIndex], args, rangeIndex);

        var sb = new System.Text.StringBuilder();
        foreach (var a in args)
        {
            sb.Append(ToText(a));
        }
        return TextResult(sb.ToString());
    }

    private static RangeValue MapConcatenateRange(RangeValue range, IReadOnlyList<ScalarValue> args, int rangeIndex)
    {
        var cells = new ScalarValue[range.RowCount, range.ColCount];
        for (int r = 0; r < range.RowCount; r++)
            for (int c = 0; c < range.ColCount; c++)
            {
                var value = range.Cells[r, c];
                if (value is ErrorValue e)
                {
                    cells[r, c] = e;
                    continue;
                }

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < args.Count; i++)
                    sb.Append(i == rangeIndex ? ToText(value) : ToText(args[i]));
                cells[r, c] = TextResult(sb.ToString());
            }

        return new RangeValue(cells);
    }

    private static ScalarValue TFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, TScalar);
        return TScalar(args[0]);
    }

    private static ScalarValue TScalar(ScalarValue value) =>
        value switch
        {
            ErrorValue e => e,
            TextValue t => TextResult(t.Value),
            DirectTextLiteralValue t => TextResult(t.Value),
            _ => new TextValue("")
        };

    private static ScalarValue Hyperlink(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 1 && args[0] is RangeValue && args[1] is RangeValue)
            return MapBinaryMathArgs(args[0], args[1], HyperlinkScalar);
        if (args.Count > 1 && args[1] is RangeValue friendlyRange)
            return MapUnaryTextRange(friendlyRange, value => HyperlinkScalar(args[0], value));
        if (args[0] is RangeValue linkRange)
            return MapUnaryTextRange(linkRange, value => HyperlinkScalar(value, args.Count > 1 ? args[1] : null));

        return HyperlinkScalar(args[0], args.Count > 1 ? args[1] : null);
    }

    private static ScalarValue HyperlinkScalar(ScalarValue link, ScalarValue? friendlyName)
    {
        // friendlyName is null only when the argument slot was genuinely omitted (see call
        // sites above, which pass `args.Count > 1 ? args[1] : null`); in that case Excel falls
        // back to displaying the link location. A present-but-blank friendly_name (e.g. a
        // reference to an empty cell) is NOT the same as omitted: real Excel coerces the blank
        // like an ordinary numeric argument and displays "0" (the documented HYPERLINK quirk,
        // worked around by wrapping the argument as `cell&""`), it does not fall back to the link.
        var display = friendlyName switch
        {
            null => ToText(link),
            BlankValue => "0",
            _ => ToText(friendlyName)
        };
        return TextResult(display);
    }

}
