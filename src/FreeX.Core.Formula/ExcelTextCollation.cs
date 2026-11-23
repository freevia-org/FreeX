using System.Globalization;
using System.Text;

namespace FreeX.Core.Formula;

/// <summary>
/// The text ordering Excel's SORT/SORTBY use (measured against Excel with en-US), modelled on the Windows
/// "word sort" rules: hyphens and apostrophes are skipped when comparing letters, symbols sort before digits and
/// digits before letters, accents break ties between otherwise-equal letters, and case is ignored (equal text keeps
/// its original order because SORT is stable). Only when everything else ties does a string that carries the
/// skipped characters sort after the one that does not ("-a" after "a" and "A").
/// </summary>
internal static class ExcelTextCollation
{
    // Order of the symbol class (all of which sort before digits and letters); '-' and the apostrophe are
    // not in it because they are ignorable.
    private const string SymbolOrder = "_,;:!?.\"()[]{}@*/\\&#%`^+<=>|~$";

    public static int Compare(string a, string b)
    {
        BuildKeys(a, out var primaryA, out var accentsA, out int ignorableA);
        BuildKeys(b, out var primaryB, out var accentsB, out int ignorableB);

        int common = Math.Min(primaryA.Count, primaryB.Count);
        for (int i = 0; i < common; i++)
        {
            int cmp = primaryA[i].CompareTo(primaryB[i]);
            if (cmp != 0) return cmp;
        }

        if (primaryA.Count != primaryB.Count) return primaryA.Count.CompareTo(primaryB.Count);

        for (int i = 0; i < primaryA.Count; i++)
        {
            int cmp = accentsA[i].CompareTo(accentsB[i]);
            if (cmp != 0) return cmp;
        }

        return ignorableA.CompareTo(ignorableB);
    }

    private static void BuildKeys(string text, out List<int> primary, out List<string> accents, out int ignorable)
    {
        primary = new List<int>(text.Length);
        accents = new List<string>(text.Length);
        ignorable = 0;
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var accent = new StringBuilder();
        foreach (char c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                accent.Append(c);
                continue;
            }

            if (accents.Count > 0 && accent.Length > 0)
            {
                accents[^1] = accent.ToString();
            }

            accent.Clear();
            if (c is '-' or '\'')
            {
                ignorable++;
                continue;
            }

            primary.Add(PrimaryWeight(c, category));
            accents.Add(string.Empty);
        }

        if (accents.Count > 0 && accent.Length > 0)
            accents[^1] = accent.ToString();
    }

    private static int PrimaryWeight(char c, UnicodeCategory category)
    {
        if (char.IsWhiteSpace(c)) return 0;
        if (char.IsDigit(c)) return 1_000 + (int)char.GetNumericValue(c);
        if (char.IsLetter(c)) return 10_000 + char.ToLowerInvariant(c);

        int symbol = SymbolOrder.IndexOf(c);
        return symbol >= 0 ? 100 + symbol : 500 + c % 400;
    }
}
