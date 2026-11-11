using System.Text;
using System.Text.RegularExpressions;

namespace FreeW.Core.Model;

/// <summary>
/// The full Find option set. The three leading members are the historical FreeW options; the rest are
/// Word's "Match prefix", "Match suffix", "Ignore punctuation characters" and "Ignore white-space
/// characters". Wildcard mode ignores everything except <see cref="MatchCase"/>.
/// </summary>
public readonly record struct TextSearchOptions(
    bool MatchCase = false,
    bool WholeWord = false,
    bool UseWildcards = false,
    bool MatchPrefix = false,
    bool MatchSuffix = false,
    bool IgnorePunctuation = false,
    bool IgnoreWhiteSpace = false);

/// <summary>
/// Word's caret ("Special") codes for the non-wildcard Find what / Replace with boxes, and the
/// characters the search text uses to stand for document structure.
/// </summary>
/// <remarks>
/// A search haystack is ordinary run text plus a few structural characters: <see cref="ParagraphMark"/>
/// between paragraphs of one story, <see cref="CellMark"/> where a story/cell ends (never matched by
/// any code), and <see cref="ObjectPlaceholder"/> for an inline object. Manual line breaks and tabs
/// are already stored as <c>\n</c> and <c>\t</c> in run text.
/// </remarks>
public static class FindSpecialCodes
{
    public const char ParagraphMark = '\r';
    public const char ManualLineBreak = '\n';
    public const char Tab = '\t';
    public const char ManualPageBreak = '\f';
    public const char ColumnBreak = '\u000E';
    public const char NonBreakingSpace = ' ';
    public const char OptionalHyphen = Hyphenator.SoftHyphen;
    public const char NonBreakingHyphen = Hyphenator.NoBreakHyphen;
    public const char EmDash = '—';
    public const char EnDash = '–';

    /// <summary>End of a table cell / story. Never matched by a special code or by "any character".</summary>
    public const char CellMark = '\u0007';

    /// <summary>Stands for an inline object (picture, shape, note reference). Never matched.</summary>
    public const char ObjectPlaceholder = '￼';

    private const string WordCharacterClass = @"[\p{L}\p{Nd}_]";

    /// <summary>True when <paramref name="text"/> contains at least one recognised caret code.</summary>
    public static bool ContainsFindCode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '^' && FindCodeToRegex(text[i + 1]) is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a non-wildcard search has to run through the regex engine: it uses a caret code, or one
    /// of the options the plain substring scan does not implement.
    /// </summary>
    public static bool RequiresPatternSearch(string needle, TextSearchOptions options) =>
        options.MatchPrefix
        || options.MatchSuffix
        || options.IgnorePunctuation
        || options.IgnoreWhiteSpace
        || ContainsFindCode(needle);

    /// <summary>
    /// Translates a non-wildcard Find what string (literal text plus caret codes) to a .NET regex.
    /// Returns null when nothing searchable remains (for example a needle that is only punctuation
    /// while punctuation is ignored).
    /// </summary>
    public static string? ToRegexPattern(string needle, TextSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(needle);

        var tokens = new List<string>();
        for (var i = 0; i < needle.Length; i++)
        {
            var c = needle[i];
            if (c == '^' && i + 1 < needle.Length && FindCodeToRegex(needle[i + 1]) is { } code)
            {
                tokens.Add(code);
                i++;
                continue;
            }

            if (options.IgnorePunctuation && char.IsPunctuation(c))
                continue;
            if (options.IgnoreWhiteSpace && IsIgnorableWhiteSpace(c))
                continue;

            tokens.Add(Regex.Escape(c.ToString()));
        }

        if (tokens.Count == 0)
            return null;

        var gap = (options.IgnorePunctuation, options.IgnoreWhiteSpace) switch
        {
            (true, true) => @"[\p{P} \t ]*",
            (true, false) => @"\p{P}*",
            (false, true) => @"[ \t ]*",
            _ => string.Empty,
        };

        var pattern = new StringBuilder();
        if (options.WholeWord || options.MatchPrefix)
            pattern.Append("(?<!").Append(WordCharacterClass).Append(')');
        pattern.Append(string.Join(gap, tokens));
        if (options.WholeWord || options.MatchSuffix)
            pattern.Append("(?!").Append(WordCharacterClass).Append(')');
        return pattern.ToString();
    }

    /// <summary>
    /// Expands the caret codes Word accepts in Replace with: <c>^p ^t ^l ^s ^- ^~ ^+ ^= ^^</c> and
    /// <c>^&amp;</c> (the found text). A paragraph mark comes back as <see cref="ParagraphMark"/>; the
    /// caller decides how to realise it. Unrecognised codes (including <c>^c</c>, clipboard contents,
    /// which is not supported) are kept literally.
    /// </summary>
    public static string ExpandReplacement(string? replacement, string? foundText)
    {
        if (string.IsNullOrEmpty(replacement))
            return string.Empty;
        if (replacement.IndexOf('^') < 0)
            return replacement;

        var result = new StringBuilder(replacement.Length + (foundText?.Length ?? 0));
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];
            if (c != '^' || i + 1 >= replacement.Length)
            {
                result.Append(c);
                continue;
            }

            var code = replacement[i + 1];
            switch (code)
            {
                case 'p' or 'P': result.Append(ParagraphMark); break;
                case 't' or 'T': result.Append(Tab); break;
                case 'l' or 'L': result.Append(ManualLineBreak); break;
                case 's' or 'S': result.Append(NonBreakingSpace); break;
                case '-': result.Append(OptionalHyphen); break;
                case '~': result.Append(NonBreakingHyphen); break;
                case '+': result.Append(EmDash); break;
                case '=': result.Append(EnDash); break;
                case '^': result.Append('^'); break;
                case '&': result.Append(foundText); break;
                default:
                    result.Append(c);
                    continue;
            }

            i++;
        }

        return result.ToString();
    }

    private static bool IsIgnorableWhiteSpace(char c) => c is ' ' or Tab or NonBreakingSpace;

    private static string? FindCodeToRegex(char code) => code switch
    {
        'p' or 'P' => @"\r",
        't' or 'T' => @"\t",
        'l' or 'L' => @"\n",
        'm' or 'M' => @"\f",
        'n' or 'N' => @"\u000E",
        's' or 'S' => @" ",
        '-' => @"­",
        '~' => @"‑",
        '+' => @"—",
        '=' => @"–",
        '^' => @"\^",
        '?' => @"[^\r\u0007￼]",
        '#' => "[0-9]",
        '$' => @"\p{L}",
        'w' or 'W' => @"[ \t ]+",
        _ => null,
    };
}
