using System.Globalization;

namespace FreeX.Core.Commands;

/// <summary>
/// Collation for the text keys of a worksheet sort.
///
/// r188: these comparisons used to be ordinal, which orders by UTF-16 code point. That pushes every
/// accented word past the whole ASCII alphabet, so the primary comparison is culture-aware.
///
/// Excel-parity round 6 (measured against Excel's Range.Sort via COM, en-US): the .NET/ICU collation
/// the code used to call directly orders ASCII punctuation differently from Excel and does not treat
/// the hyphen and apostrophe as Excel's "word sort" does, so any column holding values like
/// <c>co-op</c>, <c>coop</c>, <c>$a</c>, <c>(a)</c> or <c>#N/A</c> sorted differently from Excel.
/// <see cref="ExcelTextCollation"/> reproduces Excel's rules and this class delegates to it.
/// </summary>
internal static class SortTextComparison
{
    internal static int CompareIgnoreCase(string a, string b) =>
        ExcelTextCollation.Compare(a, b, matchCase: false);
}

/// <summary>
/// Excel's (Windows "word sort", en-US) text ordering, implemented without depending on the host
/// platform's collation for the parts where hosts disagree:
/// <list type="number">
/// <item>Hyphen-minus, apostrophe and the Unicode dashes are ignored in the primary comparison
/// (<c>coop</c> == <c>co-op</c> at that level); a leading apostrophe is ignored entirely.</item>
/// <item>Every other symbol character sorts before every digit and letter, in Excel's fixed symbol
/// order (space, ! " # $ % &amp; ( ) * , . / : ; ? @ [ \ ] ^ _ ` { | } ~ ...).</item>
/// <item>Letters and digits fall back to the host's culture-aware comparison, accent- and
/// case-insensitive at the primary level.</item>
/// <item>Remaining ties are broken by accents, then (when <c>matchCase</c>) case with lowercase first,
/// then by the positions of the ignored hyphen/apostrophe characters (the later in the string, the
/// earlier it sorts; apostrophe before hyphen).</item>
/// </list>
/// Fully equal keys compare as 0 so the caller's stable ordering keeps equal rows in input order, as Excel does.
/// </summary>
internal static class ExcelTextCollation
{
    // Order of symbol characters in Excel's collation (all of them precede digits and letters).
    private const string SymbolOrder = "  !\"#$%&()*,./:;?@[\\]^_`{|}~¡¿‘’“”¢£¥€+<=>±×÷§©°µ¶…•";

    private static readonly Dictionary<char, int> SymbolRank = BuildSymbolRank();

    private static Dictionary<char, int> BuildSymbolRank()
    {
        var map = new Dictionary<char, int>(SymbolOrder.Length);
        for (var i = 0; i < SymbolOrder.Length; i++)
            map[SymbolOrder[i]] = i;
        return map;
    }

    private const CompareOptions PrimaryOptions =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    private const CompareOptions SecondaryOptions =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType;

    internal static int Compare(string a, string b, bool matchCase)
    {
        if (ReferenceEquals(a, b) || a == b)
            return 0;

        var (strippedA, specialsA) = Strip(a);
        var (strippedB, specialsB) = Strip(b);

        var primary = ComparePrimary(strippedA, strippedB);
        if (primary != 0)
            return primary;

        var info = CultureInfo.CurrentCulture.CompareInfo;
        var secondary = info.Compare(strippedA, strippedB, SecondaryOptions);
        if (secondary != 0)
            return Math.Sign(secondary);

        var tertiary = info.Compare(strippedA, strippedB, CompareOptions.IgnoreCase);
        if (tertiary != 0)
            return Math.Sign(tertiary);

        if (matchCase)
        {
            var caseOrder = CompareCaseLowerFirst(strippedA, strippedB);
            if (caseOrder != 0)
                return caseOrder;
        }

        var specials = CompareSpecials(specialsA, specialsB);
        if (specials != 0)
            return specials;

        // Canonically equivalent spellings of the same text (e.g. a precomposed e-acute against e + a
        // combining accent) are ordered ordinally, so the result is a total order that does not depend on
        // input order. Anything else the levels above call equal (case differences, the ignored leading
        // apostrophe) stays equal: Excel keeps those rows in their original (stable) order.
        return string.Equals(a.Normalize(System.Text.NormalizationForm.FormC), b.Normalize(System.Text.NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase)
            ? Math.Sign(string.Compare(a, b, StringComparison.OrdinalIgnoreCase))
            : 0;
    }

    private static int ComparePrimary(string a, string b)
    {
        var info = CultureInfo.CurrentCulture.CompareInfo;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            var aSymbol = SymbolRank.TryGetValue(a[i], out var rankA);
            var bSymbol = SymbolRank.TryGetValue(b[j], out var rankB);
            if (aSymbol && bSymbol)
            {
                if (rankA != rankB)
                    return rankA < rankB ? -1 : 1;
                i++;
                j++;
                continue;
            }

            if (aSymbol != bSymbol)
                return aSymbol ? -1 : 1;

            var endA = i;
            while (endA < a.Length && !SymbolRank.ContainsKey(a[endA]))
                endA++;
            var endB = j;
            while (endB < b.Length && !SymbolRank.ContainsKey(b[endB]))
                endB++;

            var cmp = info.Compare(a, i, endA - i, b, j, endB - j, PrimaryOptions);
            if (cmp != 0)
                return Math.Sign(cmp);

            i = endA;
            j = endB;
        }

        if (i < a.Length)
            return 1;
        if (j < b.Length)
            return -1;
        return 0;
    }

    private static int CompareCaseLowerFirst(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        for (var index = 0; index < length; index++)
        {
            var left = a[index];
            var right = b[index];
            if (left == right)
                continue;

            var leftLower = char.IsLower(left);
            var rightLower = char.IsLower(right);
            if (leftLower != rightLower)
                return leftLower ? -1 : 1;
        }

        return 0;
    }

    // Ignored (primary-level) characters and the weight they carry in the final tie-break.
    private static int SpecialWeight(char c) => c switch
    {
        '\'' => 0,
        '-' => 1,
        '‐' => 2,
        '‑' => 3,
        '‒' => 4,
        '–' => 5,
        '—' => 6,
        _ => -1,
    };

    private static (string Stripped, List<(int Distance, int Weight)>? Specials) Strip(string text)
    {
        var hasIgnorable = false;
        foreach (var c in text)
        {
            if (c == '­' || SpecialWeight(c) >= 0)
            {
                hasIgnorable = true;
                break;
            }
        }

        if (!hasIgnorable)
            return (text, null);

        var builder = new System.Text.StringBuilder(text.Length);
        List<(int Position, int Weight)>? positions = null;
        var leading = true;
        foreach (var c in text)
        {
            if (c == '­')
                continue;

            var weight = SpecialWeight(c);
            if (weight < 0)
            {
                leading = false;
                builder.Append(c);
                continue;
            }

            // A leading apostrophe carries no weight at all in Excel's comparison.
            if (c == '\'' && leading)
                continue;

            leading = false;
            (positions ??= []).Add((builder.Length, weight));
        }

        var stripped = builder.ToString();
        List<(int Distance, int Weight)>? specials = null;
        if (positions is not null)
        {
            specials = new List<(int, int)>(positions.Count);
            foreach (var (position, weight) in positions)
                specials.Add((stripped.Length - position, weight));
        }

        return (stripped, specials);
    }

    private static int CompareSpecials(List<(int Distance, int Weight)>? a, List<(int Distance, int Weight)>? b)
    {
        var countA = a?.Count ?? 0;
        var countB = b?.Count ?? 0;
        var shared = Math.Min(countA, countB);
        for (var index = 0; index < shared; index++)
        {
            var (distanceA, weightA) = a![index];
            var (distanceB, weightB) = b![index];
            if (distanceA != distanceB)
                return distanceA < distanceB ? -1 : 1;
            if (weightA != weightB)
                return weightA < weightB ? -1 : 1;
        }

        return countA.CompareTo(countB);
    }
}
