using System.Globalization;

namespace FreeX.Core.Commands;

/// <summary>
/// Pure, low-level string primitives shared by the Flash Fill single-column
/// pattern detectors. These are deliberately free of any Flash Fill domain
/// concepts (training examples, pattern inference): each method is a stateless,
/// side-effect-free transformation over plain strings/characters, which makes
/// them straightforward to unit-test in isolation.
/// </summary>
internal static class FlashFillTextPrimitives
{
    /// <summary>Returns the digit characters of <paramref name="value"/> concatenated in order.</summary>
    public static string ExtractDigits(string value) =>
        string.Concat(value.Where(char.IsDigit));

    /// <summary>
    /// Produces a mask where each digit in <paramref name="value"/> is replaced by
    /// <c>#</c> and every non-digit character is preserved verbatim.
    /// </summary>
    public static string CreateDigitMask(string value) =>
        new(value.Select(c => char.IsDigit(c) ? '#' : c).ToArray());

    /// <summary>
    /// Fills the <c>#</c> placeholders of <paramref name="mask"/> from
    /// <paramref name="digits"/> in order, copying all other mask characters verbatim.
    /// The caller must supply at least as many digits as there are placeholders.
    /// </summary>
    public static string ApplyDigitMask(string digits, string mask)
    {
        var index = 0;
        var chars = new char[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            chars[i] = mask[i] == '#'
                ? digits[index++]
                : mask[i];
        }

        return new string(chars);
    }

    /// <summary>
    /// Converts <paramref name="s"/> to invariant title case (proper case), first
    /// lower-casing the input so existing all-caps words are normalized.
    /// </summary>
    public static string ToProperCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(s.ToLowerInvariant());
    }

    /// <summary>
    /// Returns the upper-cased first character of <paramref name="value"/> as a string,
    /// or an empty string when the input is empty.
    /// </summary>
    /// <remarks>
    /// r198: the leading TEXT ELEMENT, not <c>value[0]</c>. Flash Fill writes the initial it derives
    /// into <c>Cell.Value</c> for every filled row, so a name beginning outside the BMP used to store
    /// a lone high surrogate — a codepoint with no glyph, which survives the .xlsx round trip as an
    /// <c>_xD83D_</c> escape and renders as a replacement box in FreeX and Excel alike.
    /// </remarks>
    public static string GetUpperInitial(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value[..StringInfo.GetNextTextElementLength(value)].ToUpperInvariant();

    /// <summary>
    /// Advances <paramref name="start"/> and retreats <paramref name="end"/> past any
    /// leading/trailing whitespace within <c>[start, end]</c> of <paramref name="source"/>.
    /// </summary>
    public static void TrimSegment(string source, ref int start, ref int end)
    {
        while (start <= end && char.IsWhiteSpace(source[start]))
            start++;

        while (end >= start && char.IsWhiteSpace(source[end]))
            end--;
    }

    /// <summary>Retreats <paramref name="end"/> past any trailing whitespace of <paramref name="source"/>.</summary>
    public static void TrimTrailingWhitespace(string source, ref int end)
    {
        while (end >= 0 && char.IsWhiteSpace(source[end]))
            end--;
    }

    /// <summary>
    /// Returns the substring <c>[start, endExclusive)</c> of <paramref name="source"/>,
    /// returning the original instance when the range spans the whole string.
    /// </summary>
    public static string SliceSegment(string source, int start, int endExclusive) =>
        start == 0 && endExclusive == source.Length
            ? source
            : source[start..endExclusive];

    /// <summary>
    /// Returns the whitespace-trimmed substring <c>[start, endExclusive)</c> of
    /// <paramref name="source"/>, or an empty string when the trimmed range is empty.
    /// </summary>
    public static string SliceTrimmedSegment(string source, int start, int endExclusive)
    {
        var trimStart = start;
        var trimEnd = endExclusive - 1;
        TrimSegment(source, ref trimStart, ref trimEnd);
        return trimStart <= trimEnd
            ? SliceSegment(source, trimStart, trimEnd + 1)
            : string.Empty;
    }

    /// <summary>
    /// Determines whether the whitespace-trimmed substring <c>[start, endExclusive)</c> of
    /// <paramref name="source"/> equals <paramref name="expected"/> (ordinal comparison).
    /// </summary>
    public static bool TrimmedSegmentEquals(string source, int start, int endExclusive, string expected)
    {
        var trimStart = start;
        var trimEnd = endExclusive - 1;
        TrimSegment(source, ref trimStart, ref trimEnd);

        var length = trimStart <= trimEnd ? trimEnd - trimStart + 1 : 0;
        return length == expected.Length &&
               source.AsSpan(trimStart, length).SequenceEqual(expected.AsSpan());
    }

    /// <summary>
    /// Determines whether <paramref name="source"/> contains any non-whitespace,
    /// non-<paramref name="delimiter"/> character before <paramref name="delimiterIndex"/>.
    /// </summary>
    public static bool HasNonEmptyPartBeforeDelimiter(string source, int delimiterIndex, char delimiter)
    {
        for (var i = 0; i < delimiterIndex; i++)
        {
            if (source[i] != delimiter && !char.IsWhiteSpace(source[i]))
                return true;
        }

        return false;
    }
}
