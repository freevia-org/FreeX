using System.Globalization;

namespace FreeX.Core.Commands;

/// <summary>
/// Collation for the text keys of a worksheet sort.
///
/// r188: these comparisons used to be ordinal, which orders by UTF-16 code point. That pushes every
/// accented word past the whole ASCII alphabet: sorting {zebra, east, elan-with-acute-e, apple}
/// ordinally gives apple, east, zebra, elan -- the accented word last, wherever it belongs
/// alphabetically. Excel sorts text with the user's collation, so a German or French speaker expects
/// elan next to east. The primary comparison is therefore culture-aware.
///
/// The ordinal fallback is not decoration. Culture-aware collation reports 0 for strings a user can
/// tell apart (ignorable punctuation, compatibility forms), and a comparison that calls distinct
/// values equal makes the sort's relative order depend on the input order. Falling back to ordinal
/// keeps the result total and deterministic while leaving the visible ordering to the culture.
/// </summary>
internal static class SortTextComparison
{
    internal static int CompareIgnoreCase(string a, string b)
    {
        var primary = string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        return primary != 0
            ? primary
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
