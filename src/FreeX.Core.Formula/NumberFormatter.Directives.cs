using System.Text.RegularExpressions;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex NumericElapsedTokenRegex = new(@"\[([hH])\]|\[([mM])\]|\[([sS])\]");

    private readonly record struct FormatDirectivePreprocessResult(
        string Format,
        Match ElapsedTimeMatch);

    private static FormatDirectivePreprocessResult PreprocessBracketFormatDirectives(string format)
    {
        if (format.IndexOf('[') < 0)
            return new FormatDirectivePreprocessResult(format, Match.Empty);

        // Both directive scanners below must be quote-aware: text inside "..." literals
        // (and characters escaped with \) is never a directive, even if it looks like one
        // (e.g. the literal suffix "[hrs]" in 0"[hrs]" must not be mistaken for the elapsed-
        // time token [h]). Mirrors the inQuote scanning convention used throughout this
        // formatter (see NumberFormatter.Accounting.cs, ExtractNumericAffixes, etc.).
        var elapsedTimeMatch = FindUnquotedElapsedTimeToken(format);
        if (elapsedTimeMatch.Success)
        {
            return new FormatDirectivePreprocessResult(
                RemoveSpacingAndFillDirectives(format),
                elapsedTimeMatch);
        }

        return new FormatDirectivePreprocessResult(
            RemoveUnquotedBracketDirectives(format),
            elapsedTimeMatch);
    }

    // Finds the first [h]/[m]/[s] (any case) elapsed-time token that is not inside a
    // quoted "..." literal and not escaped with a leading backslash.
    private static Match FindUnquotedElapsedTimeToken(string format)
    {
        bool inQuote = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                i++;
                continue;
            }

            if (inQuote)
                continue;

            var candidate = NumericElapsedTokenRegex.Match(format, i);
            if (candidate.Success && candidate.Index == i)
                return candidate;
        }

        return Match.Empty;
    }

    // Strips bracketed directives (e.g. color codes like [Red], locale tokens already
    // handled elsewhere) that are not inside a quoted "..." literal and not escaped.
    // Quoted/escaped bracket text is left untouched so literal suffixes such as
    // "[kg]" survive.
    private static string RemoveUnquotedBracketDirectives(string format)
    {
        var sb = new System.Text.StringBuilder(format.Length);
        bool inQuote = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < format.Length)
            {
                sb.Append(c);
                sb.Append(format[++i]);
                continue;
            }

            if (!inQuote && c == '[')
            {
                int close = format.IndexOf(']', i + 1);
                if (close > i)
                {
                    i = close;
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
