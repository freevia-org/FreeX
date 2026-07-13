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
        if (FirstError(args) is { } e) return e;

        // Excel's dynamic-array CONCATENATE broadcasts range/array arguments element-wise
        // (equally-shaped ranges pair up cell-by-cell; a scalar broadcasts across every cell;
        // mismatched non-1x1 shapes yield #VALUE!) via the shared MapScalarArgs helper. With no
        // range arguments at all, this collapses to a single ordinary scalar concatenation.
        return MapScalarArgs(args, scalarArgs =>
        {
            if (FirstError(scalarArgs) is { } cellError) return cellError;

            var sb = new System.Text.StringBuilder();
            foreach (var value in scalarArgs)
                sb.Append(ToText(value));
            return TextResult(sb.ToString());
        });
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
