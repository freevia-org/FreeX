using System.Text.RegularExpressions;

using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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
        try
        {
            return new BoolValue(regex.IsMatch(ToText(value)));
        }
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }
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
        {
            // return_mode 1/2 makes RegexExtractScalar itself return a per-cell RangeValue (all
            // matches / capture groups). Over a MULTI-cell text range that would require nesting a
            // RangeValue inside another RangeValue's cell -- a ragged, non-rectangular combination
            // that has no well-defined flattening. Real Excel can't represent this either and
            // surfaces #CALC!, so match that instead of letting MapUnaryTextRange store the nested
            // RangeValue verbatim into a single outer cell (which corrupts the sheet -- the cell
            // would display the array's own record dump). A single-cell range still spills normally.
            if (returnMode is 1 or 2 && textRange.RowCount * textRange.ColCount > 1)
                return ErrorValue.Calc;

            return MapUnaryTextRange(textRange, value => RegexExtractScalar(value, regex, returnMode));
        }

        return RegexExtractScalar(args[0], regex, returnMode);
    }

    private static ScalarValue RegexExtractScalar(ScalarValue value, Regex regex, int returnMode)
    {
        if (value is ErrorValue e) return e;

        var text = ToText(value);
        MatchCollection matches;
        try
        {
            matches = regex.Matches(text);
            _ = matches.Count;
        }
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }

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
        try
        {
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
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }
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

        var patternText = ToText(pattern);

        // Excel's REGEX* functions run on Google's RE2 engine, which deliberately omits
        // backreferences and lookaround (for linear-time matching guarantees) -- both of those
        // compile fine under .NET's full regex engine and would silently produce a different
        // result than real Excel (which errors with #VALUE!) instead of merely a different error.
        // Reject them up front so both engines agree a formula using them is invalid.
        if (HasRe2UnsupportedConstruct(patternText))
        {
            error = ErrorValue.Value;
            return false;
        }

        // RE2/Excel accepts the Python-style named-group syntax (?P<name>...); .NET only
        // recognizes (?<name>...). Translate before compiling so a pattern real Excel accepts
        // doesn't spuriously fail here with #VALUE!.
        patternText = TranslatePythonNamedGroups(patternText);

        try
        {
            regex = new Regex(patternText, options, FormulaSafetyLimits.RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            error = ErrorValue.Value;
            return false;
        }
    }

    private static readonly Regex PythonNamedGroupSyntax = new(@"\(\?P<", RegexOptions.Compiled);

    private static string TranslatePythonNamedGroups(string pattern) =>
        PythonNamedGroupSyntax.IsMatch(pattern) ? PythonNamedGroupSyntax.Replace(pattern, "(?<") : pattern;

    /// <summary>
    /// Detects the two RE2-unsupported construct families that .NET's regex engine happily
    /// compiles but real Excel/RE2 rejects with #VALUE!: backreferences (\1-\9) and lookaround
    /// ((?=...), (?!...), (?&lt;=...), (?&lt;!...)). Named groups ((?&lt;name&gt;...)) are deliberately not
    /// flagged -- only a lookbehind's '=' / '!' right after '(?&lt;' trips this check.
    /// </summary>
    private static bool HasRe2UnsupportedConstruct(string pattern)
    {
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                var next = pattern[i + 1];
                if (!inClass && next is >= '1' and <= '9')
                    return true; // backreference \1..\9
                i++; // skip the escaped character
                continue;
            }

            if (!inClass && c == '[')
            {
                inClass = true;
                continue;
            }
            if (inClass && c == ']')
            {
                inClass = false;
                continue;
            }

            if (!inClass && c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?')
            {
                if (i + 2 < pattern.Length)
                {
                    var afterQuestion = pattern[i + 2];
                    if (afterQuestion is '=' or '!')
                        return true; // (?=...) or (?!...) lookahead
                    if (afterQuestion == '<' && i + 3 < pattern.Length && pattern[i + 3] is '=' or '!')
                        return true; // (?<=...) or (?<!...) lookbehind
                }
            }
        }

        return false;
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
