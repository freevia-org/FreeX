using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Case, whitespace, repetition, value, and cleaning text functions.

    private static readonly Regex MultiSpaceRegex = new(@" {2,}", RegexOptions.Compiled);

    private static ScalarValue Trim(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, TrimText);
        return TrimText(args[0]);
    }

    private static ScalarValue Upper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, UpperText);
        return UpperText(args[0]);
    }

    private static ScalarValue Lower(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, LowerText);
        return LowerText(args[0]);
    }

    private static ScalarValue Proper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ProperText);
        return ProperText(args[0]);
    }

    private static ScalarValue TrimText(ScalarValue value)
    {
        var text = MultiSpaceRegex.Replace(ToText(value).Trim(' '), " ");
        return TextResult(text);
    }

    private static ScalarValue UpperText(ScalarValue value) =>
        TextResult(ToText(value).ToUpperInvariant());

    private static ScalarValue LowerText(ScalarValue value) =>
        TextResult(ToText(value).ToLowerInvariant());

    private static ScalarValue ProperText(ScalarValue value)
    {
        var text = ToText(value);
        if (text.Length == 0) return new TextValue("");
        var sb = new System.Text.StringBuilder(text.Length);
        bool capitaliseNext = true;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || !char.IsLetter(ch)) { capitaliseNext = true; sb.Append(ch); }
            else if (capitaliseNext) { sb.Append(char.ToUpperInvariant(ch)); capitaliseNext = false; }
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return TextResult(sb.ToString());
    }

    private static ScalarValue Rept(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[1] is ErrorValue repeatError) return repeatError;
        return MapBinaryMathArgs(args[0], args[1], ReptScalarWithTimes);
    }

    private static ScalarValue ReptScalarWithTimes(ScalarValue value, ScalarValue timesValue)
    {
        if (value is ErrorValue valueError) return valueError;
        if (timesValue is ErrorValue timesError) return timesError;
        var timesD = ToNumber(timesValue);
        if (!double.IsFinite(timesD) || timesD < 0 || timesD > int.MaxValue) return ErrorValue.Value;
        int times = (int)timesD;
        return ReptText(ToText(value), times);
    }

    private static ScalarValue ReptText(string text, int times)
    {
        var characterCount = ContainsSurrogatePair(text) ? CountTextElements(text) : text.Length;
        var outputCharacterCount = (long)characterCount * times;
        if (outputCharacterCount > 32767) return ErrorValue.Value;
        if (outputCharacterCount == 0) return new TextValue("");

        var outputLength = (long)text.Length * times;
        var repeated = string.Create((int)outputLength, (text, times), static (span, state) =>
        {
            var source = state.text.AsSpan();
            for (var i = 0; i < state.times; i++)
            {
                source.CopyTo(span);
                span = span[source.Length..];
            }
        });
        return new TextValue(repeated);
    }

    private static ScalarValue ValueFunc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, ValueScalar);
        return ValueScalar(args[0]);
    }

    private static ScalarValue ValueScalar(ScalarValue value)
    {
        if (value is NumberValue nv) return nv;
        var text = ToText(value).Trim();
        if (ExcelTextNumberParser.TryParse(text, out var d))
            return new NumberValue(d);
        return ErrorValue.Value;
    }

    private static ScalarValue Clean(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        if (args[0] is RangeValue range) return MapUnaryTextRange(range, CleanText);
        return CleanText(args[0]);
    }

    private static ScalarValue CleanText(ScalarValue value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in ToText(value))
            if (c >= 32) sb.Append(c);
        return TextResult(sb.ToString());
    }

}
