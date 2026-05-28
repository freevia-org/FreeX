using System.Text.RegularExpressions;

using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue RegexTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCreateRegex(args[1], args.Count > 2 ? args[2] : BlankValue.Instance, out var regex, out var error))
            return error;

        if (args[0] is RangeValue textRange)
            return MapUnaryTextRange(textRange, value => RegexTestScalar(value, regex));

        return RegexTestScalar(args[0], regex);
    }

    private static ScalarValue RegexTestScalar(ScalarValue value, Regex regex)
    {
        if (value is ErrorValue e) return e;
        return new BoolValue(regex.IsMatch(ToText(value)));
    }

    private static ScalarValue RegexExtract(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCreateRegex(args[1], args.Count > 3 ? args[3] : BlankValue.Instance, out var regex, out var error))
            return error;
        if (!TryGetOptionalMode(args, 2, defaultValue: 0, out int returnMode, out error))
            return error;
        if (returnMode is not (0 or 1 or 2))
            return ErrorValue.Value;

        if (args[0] is RangeValue textRange)
            return MapUnaryTextRange(textRange, value => RegexExtractScalar(value, regex, returnMode));

        return RegexExtractScalar(args[0], regex, returnMode);
    }

    private static ScalarValue RegexExtractScalar(ScalarValue value, Regex regex, int returnMode)
    {
        if (value is ErrorValue e) return e;

        var text = ToText(value);
        var matches = regex.Matches(text);
        if (matches.Count == 0)
            return ErrorValue.NA;

        if (returnMode == 1)
        {
            var cells = new ScalarValue[matches.Count, 1];
            for (int i = 0; i < matches.Count; i++)
                cells[i, 0] = TextResult(matches[i].Value);

            return new RangeValue(cells);
        }

        if (returnMode == 2)
        {
            var first = matches[0];
            if (first.Groups.Count <= 1)
                return ErrorValue.NA;

            var cells = new ScalarValue[1, first.Groups.Count - 1];
            for (int i = 1; i < first.Groups.Count; i++)
                cells[0, i - 1] = first.Groups[i].Success ? TextResult(first.Groups[i].Value) : new TextValue("");

            return new RangeValue(cells);
        }

        return TextResult(matches[0].Value);
    }

    private static ScalarValue RegexReplace(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (!TryCreateRegex(args[1], args.Count > 4 ? args[4] : BlankValue.Instance, out var regex, out var error))
            return error;

        var replacement = SingleValueOrErrorAsValue(args[2], out error);
        if (replacement is null) return error;
        if (replacement is ErrorValue replacementError) return replacementError;

        if (!TryGetOptionalInteger(args, 3, defaultValue: 0, out int occurrence, out error))
            return error;

        if (args[0] is RangeValue textRange)
            return MapUnaryTextRange(textRange, value => RegexReplaceScalar(value, regex, ToText(replacement), occurrence));

        return RegexReplaceScalar(args[0], regex, ToText(replacement), occurrence);
    }

    private static ScalarValue RegexReplaceScalar(ScalarValue value, Regex regex, string replacement, int occurrence)
    {
        if (value is ErrorValue e) return e;

        var text = ToText(value);
        if (occurrence == 0)
            return TextResult(regex.Replace(text, replacement));

        var matches = regex.Matches(text);
        var matchIndex = occurrence > 0 ? occurrence - 1 : matches.Count + occurrence;
        if (matchIndex < 0 || matchIndex >= matches.Count)
            return TextResult(text);

        var match = matches[matchIndex];
        return TextResult(
            text[..match.Index] +
            match.Result(replacement) +
            text[(match.Index + match.Length)..]);
    }

    private static bool TryCreateRegex(
        ScalarValue patternValue,
        ScalarValue caseSensitivityValue,
        out Regex regex,
        out ScalarValue error)
    {
        regex = new Regex("$.");
        error = ErrorValue.Value;

        var pattern = SingleValueOrErrorAsValue(patternValue, out error);
        if (pattern is null) return false;
        if (pattern is ErrorValue patternError)
        {
            error = patternError;
            return false;
        }

        if (!TryGetRegexCaseSensitivity(caseSensitivityValue, out var options, out error))
            return false;

        try
        {
            regex = new Regex(ToText(pattern), options, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            error = ErrorValue.Value;
            return false;
        }
    }

    private static bool TryGetRegexCaseSensitivity(ScalarValue value, out RegexOptions options, out ScalarValue error)
    {
        options = RegexOptions.CultureInvariant;
        error = ErrorValue.Value;
        if (value is BlankValue)
            return true;

        if (!TryGetScalarControlArgument(value, out var scalar, out error))
            return false;

        var raw = ToNumber(scalar);
        if (!double.IsFinite(raw))
            return false;

        var mode = (int)raw;
        if (mode == 0)
            return true;
        if (mode == 1)
        {
            options |= RegexOptions.IgnoreCase;
            return true;
        }

        return false;
    }
}
