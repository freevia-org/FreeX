using System.Globalization;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // Phase A1 text functions: UNICHAR, UNICODE, NUMBERVALUE.

    private static ScalarValue Unichar(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UnicharScalar);
        return UnicharScalar(args[0]);
    }

    private static ScalarValue UnicharScalar(ScalarValue value)
    {
        var n = ToNumber(value);
        if (!double.IsFinite(n)) return ErrorValue.Value;
        int codePoint = (int)Math.Truncate(n);
        if (codePoint <= 0 || codePoint > 0x10FFFF) return ErrorValue.Value;
        if (codePoint >= 0xD800 && codePoint <= 0xDFFF) return ErrorValue.Value; // surrogate halves
        return new TextValue(char.ConvertFromUtf32(codePoint));
    }

    private static ScalarValue UnicodeFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UnicodeScalar);
        return UnicodeScalar(args[0]);
    }

    private static ScalarValue UnicodeScalar(ScalarValue value)
    {
        var text = ToText(value);
        if (text.Length == 0) return ErrorValue.Value;
        if (char.IsHighSurrogate(text[0]))
        {
            if (text.Length < 2 || !char.IsLowSurrogate(text[1])) return ErrorValue.Value;
            return new NumberValue(char.ConvertToUtf32(text[0], text[1]));
        }
        if (char.IsLowSurrogate(text[0])) return ErrorValue.Value;
        return new NumberValue(text[0]);
    }

    private static ScalarValue Numbervalue(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args.Count > 1 && args[1] is ErrorValue e1) return e1;
        if (args.Count > 2 && args[2] is ErrorValue e2) return e2;

        var decSep = args.Count > 1 ? ToText(args[1]) : ".";
        var grpSep = args.Count > 2 ? ToText(args[2]) : ",";
        if (args[0] is RangeValue range)
            return MapUnaryTextRange(range, value => NumbervalueScalar(value, decSep, grpSep));
        return NumbervalueScalar(args[0], decSep, grpSep);
    }

    private static ScalarValue NumbervalueScalar(ScalarValue value, string decSep, string grpSep)
    {
        var text = ToText(value).Trim();
        // Validate separators per Excel spec
        if (decSep.Length != 1) return ErrorValue.Value;
        if (grpSep.Length == 0) return ErrorValue.Value;
        if (decSep == grpSep) return ErrorValue.Value;
        if (grpSep.Contains(decSep)) return ErrorValue.Value;

        // Strip whitespace (Excel allows whitespace anywhere)
        text = text.Replace(" ", "").Replace("\t", "");
        bool accountingNegative = text.StartsWith('(') && text.EndsWith(')');
        if (accountingNegative)
            text = text[1..^1];

        // Trailing percent
        int pctCount = 0;
        while (text.EndsWith('%'))
        {
            pctCount++;
            text = text[..^1];
        }

        // Remove all group separator characters
        text = text.Replace(grpSep, string.Empty, StringComparison.Ordinal);
        // Substitute decimal separator with '.'
        if (decSep != ".") text = text.Replace(decSep, ".", StringComparison.Ordinal);

        if (text.Length == 0) return new NumberValue(0);

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return ErrorValue.Value;

        for (int i = 0; i < pctCount; i++) v /= 100.0;
        if (accountingNegative) v = -v;
        return NumberResult(v);
    }
}
