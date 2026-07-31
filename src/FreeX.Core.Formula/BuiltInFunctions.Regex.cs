using System.Text.RegularExpressions;

using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    private static ScalarValue RegexTest(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Check the leftmost argument (text) for an error before resolving the pattern/mode
        // arguments, matching Excel's left-to-right argument-error precedence that every other
        // multi-argument function in this codebase follows (AGGREGATE, IF, EXACT, TEXTBEFORE, ...).
        if (args[0] is ErrorValue e0) return e0;

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
        // Resolve arguments in strict ascending index order (text, pattern, return_mode,
        // case_sensitivity) so the leftmost error wins, matching Excel's left-to-right
        // argument-error precedence (see RegexTest / R104). Note that case_sensitivity is the
        // LAST parameter here, so it must not be resolved until return_mode (position 3) has
        // already been checked -- hence resolving the pattern text and building the final Regex
        // (which needs case_sensitivity) are two separate steps with return_mode in between.
        if (args[0] is ErrorValue e0) return e0;

        if (!TryResolveRegexPattern(args[1], out var patternText, out var error))
            return error;
        if (!TryGetOptionalMode(args, 2, defaultValue: 0, out int returnMode, out error))
            return error;
        if (returnMode is not (0 or 1 or 2))
            return ErrorValue.Value;
        if (!TryCreateRegexFromPattern(patternText, args.Count > 3 ? args[3] : BlankValue.Instance, out var regex, out error))
            return error;

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
        // Resolve arguments in strict ascending index order (text, pattern, replacement,
        // occurrence, case_sensitivity) so the leftmost error wins, matching Excel's left-to-right
        // argument-error precedence (see RegexTest / R104) -- rather than the previous 0,1,4,2,3
        // order that resolved case_sensitivity (the LAST parameter) right after the pattern,
        // ahead of replacement/occurrence. Resolving the pattern text and building the final
        // Regex (which needs case_sensitivity) are therefore two separate steps, with
        // replacement/occurrence checked in between.
        if (args[0] is ErrorValue e0) return e0;

        if (!TryResolveRegexPattern(args[1], out var patternText, out var error))
            return error;

        var replacement = SingleValueOrErrorAsValue(args[2], out error);
        if (replacement is null) return error;
        if (replacement is ErrorValue replacementError) return replacementError;

        if (!TryGetOptionalInteger(args, 3, defaultValue: 0, out int occurrence, out error))
            return error;

        if (!TryCreateRegexFromPattern(patternText, args.Count > 4 ? args[4] : BlankValue.Instance, out var regex, out error))
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
                return TextResult(regex.Replace(text, m => ExpandRe2Replacement(m, replacement)));

            var matches = regex.Matches(text);
            var matchIndex = occurrence > 0 ? occurrence - 1 : matches.Count + occurrence;
            if (matchIndex < 0 || matchIndex >= matches.Count)
                return TextResult(text);

            var match = matches[matchIndex];
            return TextResult(
                text[..match.Index] +
                ExpandRe2Replacement(match, replacement) +
                text[(match.Index + match.Length)..]);
        }
        catch (RegexMatchTimeoutException)
        {
            return ErrorValue.Value;
        }
    }

    /// <summary>
    /// Expands a REGEXREPLACE replacement template against a match using RE2/Go's
    /// regexp.Expand semantics (which is what real Excel's REGEXREPLACE runs on), not .NET's
    /// Match.Result semantics. The two differ on unresolved group references: RE2 replaces a
    /// reference to an out-of-range or unmatched group (numeric or named) with an empty string,
    /// while .NET leaves the literal '$N' text untouched. '$$' is a literal '$'; '$name' or
    /// '${name}' expands to that capture group.
    /// </summary>
    private static string ExpandRe2Replacement(Match match, string replacement)
    {
        if (replacement.IndexOf('$') < 0)
            return replacement;

        var sb = new System.Text.StringBuilder(replacement.Length);
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c != '$' || i + 1 >= replacement.Length)
            {
                sb.Append(c);
                continue;
            }

            var next = replacement[i + 1];
            if (next == '$')
            {
                sb.Append('$');
                i++;
                continue;
            }

            if (next == '{')
            {
                var close = replacement.IndexOf('}', i + 2);
                if (close < 0)
                {
                    sb.Append(c); // unterminated '${' -- leave the lone '$' literal
                    continue;
                }

                sb.Append(ResolveReplacementGroup(match, replacement[(i + 2)..close]));
                i = close;
                continue;
            }

            if (IsReplacementNameChar(next))
            {
                var j = i + 1;
                while (j < replacement.Length && IsReplacementNameChar(replacement[j]))
                    j++;

                sb.Append(ResolveReplacementGroup(match, replacement[(i + 1)..j]));
                i = j - 1;
                continue;
            }

            // '$' not followed by a name char, '{', or another '$' -- literal '$'.
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsReplacementNameChar(char c) =>
        c is '_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

    private static string ResolveReplacementGroup(Match match, string groupRef)
    {
        if (int.TryParse(groupRef, out var index))
        {
            return index >= 0 && index < match.Groups.Count && match.Groups[index].Success
                ? match.Groups[index].Value
                : "";
        }

        var group = match.Groups[groupRef];
        return group.Success ? group.Value : "";
    }

    private static bool TryCreateRegex(
        ScalarValue patternValue,
        ScalarValue caseSensitivityValue,
        out Regex regex,
        out ScalarValue error)
    {
        regex = new Regex("$.");

        if (!TryResolveRegexPattern(patternValue, out var patternText, out error))
            return false;

        return TryCreateRegexFromPattern(patternText, caseSensitivityValue, out regex, out error);
    }

    /// <summary>
    /// Resolves and validates the pattern argument only -- independent of case_sensitivity -- so
    /// callers with an optional argument positioned BETWEEN pattern and case_sensitivity (e.g.
    /// RegexExtract's return_mode, RegexReplace's replacement/occurrence) can check that argument
    /// for an error before ever touching case_sensitivity (the last parameter in both functions),
    /// preserving Excel's strict left-to-right argument-error precedence.
    /// </summary>
    private static bool TryResolveRegexPattern(ScalarValue patternValue, out string patternText, out ScalarValue error)
    {
        patternText = "";
        error = ErrorValue.Value;

        var pattern = SingleValueOrErrorAsValue(patternValue, out error);
        if (pattern is null) return false;
        if (pattern is ErrorValue patternError)
        {
            error = patternError;
            return false;
        }

        var text = ToText(pattern);

        // Excel's REGEX* functions run on Google's RE2 engine, which deliberately omits
        // backreferences and lookaround (for linear-time matching guarantees) -- both of those
        // compile fine under .NET's full regex engine and would silently produce a different
        // result than real Excel (which errors with #VALUE!) instead of merely a different error.
        // Reject them up front so both engines agree a formula using them is invalid.
        if (HasRe2UnsupportedConstruct(text))
        {
            error = ErrorValue.Value;
            return false;
        }

        // RE2/Excel accepts the Python-style named-group syntax (?P<name>...); .NET only
        // recognizes (?<name>...). Translate before compiling so a pattern real Excel accepts
        // doesn't spuriously fail here with #VALUE!.
        text = TranslatePythonNamedGroups(text);

        // RE2/Excel's $ (without a /m multiline flag, which these functions never expose) is a
        // strict end-of-text anchor -- equivalent to \z. .NET's default (non-Multiline) $ instead
        // matches either the true end of the string OR immediately before a single trailing '\n'.
        // Rewrite bare, unescaped, out-of-class '$' to '\z' so a source string ending in a
        // newline behaves like real Excel instead of silently matching one character early.
        text = NormalizeEndOfTextAnchor(text);

        // RE2/Excel's \p{Name} accepts Unicode *script* names (e.g. \p{Greek}) in addition to
        // general categories; .NET's \p{} only recognizes general-category abbreviations and
        // "Is"-prefixed named *blocks* (e.g. \p{IsGreek}). Translate the common RE2 script names
        // real spreadsheets are likely to use to their nearest .NET named-block equivalent so
        // these don't spuriously throw ArgumentException/#VALUE! for patterns Excel accepts.
        text = TranslateRe2ScriptNames(text);

        patternText = text;
        return true;
    }

    /// <summary>
    /// Compiles the final Regex from an already-resolved pattern string plus the (last-positioned)
    /// case_sensitivity argument. Split out from <see cref="TryResolveRegexPattern"/> so callers
    /// can resolve earlier-positioned optional arguments in between the two steps.
    /// </summary>
    private static bool TryCreateRegexFromPattern(
        string patternText,
        ScalarValue caseSensitivityValue,
        out Regex regex,
        out ScalarValue error)
    {
        regex = new Regex("$.");
        error = ErrorValue.Value;

        if (!TryGetRegexCaseSensitivity(caseSensitivityValue, out var options, out error))
            return false;

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
    /// Rewrites bare (unescaped, out-of-character-class) '$' meta-characters to '\z' so the anchor
    /// matches RE2/Excel's strict end-of-text semantics rather than .NET's default "end of string,
    /// or immediately before a single trailing newline" behavior.
    /// </summary>
    private static string NormalizeEndOfTextAnchor(string pattern)
    {
        if (pattern.IndexOf('$') < 0)
            return pattern;

        var sb = new System.Text.StringBuilder(pattern.Length + 4);
        var inClass = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(c).Append(pattern[i + 1]);
                i++;
                continue;
            }

            if (!inClass && c == '[')
            {
                inClass = true;
                sb.Append(c);
                continue;
            }
            if (inClass && c == ']')
            {
                inClass = false;
                sb.Append(c);
                continue;
            }

            if (!inClass && c == '$')
            {
                sb.Append(@"\z");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    // RE2 Unicode *script* names (as opposed to .NET's general-category / "Is"-block names) that
    // real spreadsheet patterns are most likely to reference, mapped to their nearest .NET named
    // Unicode block. Deliberately not exhaustive of RE2's full script list -- only translating
    // names we can map faithfully; anything not listed here is left as-is (and still throws, same
    // as before this fix, rather than risk mistranslating a script we cannot represent in .NET).
    private static readonly Dictionary<string, string> Re2ScriptNameToDotNetBlock = new(StringComparer.Ordinal)
    {
        ["Greek"] = "IsGreek",
        ["Cyrillic"] = "IsCyrillic",
        ["Hebrew"] = "IsHebrew",
        ["Arabic"] = "IsArabic",
        ["Armenian"] = "IsArmenian",
        ["Georgian"] = "IsGeorgian",
        ["Hiragana"] = "IsHiragana",
        ["Katakana"] = "IsKatakana",
        ["Thai"] = "IsThai",
        ["Devanagari"] = "IsDevanagari",
        ["Bengali"] = "IsBengali",
        ["Tamil"] = "IsTamil",
        ["Hangul"] = "IsHangulSyllables",
        ["Han"] = "IsCJKUnifiedIdeographs",
    };

    private static readonly Regex UnicodePropertyName = new(@"\\([pP])\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static string TranslateRe2ScriptNames(string pattern)
    {
        if (pattern.IndexOf(@"\p", StringComparison.Ordinal) < 0 && pattern.IndexOf(@"\P", StringComparison.Ordinal) < 0)
            return pattern;

        return UnicodePropertyName.Replace(pattern, m =>
        {
            var name = m.Groups[2].Value;
            return Re2ScriptNameToDotNetBlock.TryGetValue(name, out var dotNetBlock)
                ? $@"\{m.Groups[1].Value}{{{dotNetBlock}}}"
                : m.Value;
        });
    }

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
                    if (afterQuestion == '>')
                        return true; // (?>...) atomic group -- no RE2 equivalent
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
