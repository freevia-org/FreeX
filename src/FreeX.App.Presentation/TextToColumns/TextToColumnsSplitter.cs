using System.Text;

namespace FreeX.App.Presentation.TextToColumns;

/// <summary>
/// Pure splitting primitives shared by the delimited and fixed-width planners. The semantics mirror the
/// desktop hosts' splitter exactly: qualifier bracketing with doubled-qualifier escapes, optional
/// consecutive-delimiter collapsing, and fixed-width slicing that tolerates ragged or short rows.
/// </summary>
public static class TextToColumnsSplitter
{
    /// <summary>
    /// Splits a single line of text by any of the given delimiter characters. When a qualifier is set
    /// and present in the text, delimiters inside a qualified span are kept literal and a doubled
    /// qualifier is emitted as a single literal qualifier character.
    /// </summary>
    public static string[] SplitDelimited(
        string text,
        string delimiters,
        char? textQualifier = null,
        bool treatConsecutiveDelimitersAsOne = false)
    {
        if (textQualifier is not { } qualifier || text.IndexOf(qualifier) < 0)
            return SplitUnqualified(text, delimiters, treatConsecutiveDelimitersAsOne);

        var parts = new List<string>();
        var current = new StringBuilder();
        var inQualifiedText = false;
        var atFieldStart = true;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (inQualifiedText)
            {
                if (ch == qualifier)
                {
                    if (index + 1 < text.Length && text[index + 1] == qualifier)
                    {
                        current.Append(qualifier);
                        index++;
                        continue;
                    }

                    inQualifiedText = false;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == qualifier && atFieldStart)
            {
                inQualifiedText = true;
                atFieldStart = false;
                continue;
            }

            if (DelimiterLengthAt(text, index, delimiters) is var delimiterLength && delimiterLength > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                atFieldStart = true;
                index += delimiterLength - 1;

                if (treatConsecutiveDelimitersAsOne)
                {
                    while (index + 1 < text.Length && DelimiterLengthAt(text, index + 1, delimiters) is var next && next > 0)
                        index += next;
                }

                continue;
            }

            current.Append(ch);
            atFieldStart = false;
        }

        parts.Add(current.ToString());
        return parts.ToArray();
    }

    /// <summary>
    /// Slices a single line of text at the given 1-based break positions. Positions are normalised
    /// (positive, deduplicated, ascending). Breaks past the end of the text yield no extra field, and a
    /// shorter row simply produces fewer/short fields.
    /// </summary>
    public static string[] SplitFixedWidth(string text, IReadOnlyList<int> breakPositions)
    {
        if (breakPositions.Count == 0)
            return [text];

        var positions = NormalizeBreakPositions(breakPositions);
        if (positions.Count == 0)
            return [text];

        var parts = new List<string>(positions.Count + 1);
        var start = 0;
        foreach (var position in positions)
        {
            var end = Math.Min(position, text.Length);
            if (end > start)
                parts.Add(text[start..end]);
            start = Math.Min(position, text.Length);
        }

        if (start < text.Length)
            parts.Add(text[start..]);
        else if (parts.Count == 0)
            parts.Add(string.Empty);

        return parts.ToArray();
    }

    private static string[] SplitUnqualified(
        string text,
        string delimiters,
        bool treatConsecutiveDelimitersAsOne)
    {
        var partCount = CountUnqualifiedParts(text, delimiters, treatConsecutiveDelimitersAsOne);
        var parts = new string[partCount];
        var start = 0;
        var writeIndex = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var length = DelimiterLengthAt(text, index, delimiters);
            if (length == 0)
                continue;

            parts[writeIndex++] = text.Substring(start, index - start);
            index += length - 1;

            if (treatConsecutiveDelimitersAsOne)
            {
                while (index + 1 < text.Length && DelimiterLengthAt(text, index + 1, delimiters) is var next && next > 0)
                    index += next;
            }

            start = index + 1;
        }

        parts[writeIndex] = text[start..];
        return parts;
    }

    private static int CountUnqualifiedParts(
        string text,
        string delimiters,
        bool treatConsecutiveDelimitersAsOne)
    {
        var count = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var length = DelimiterLengthAt(text, index, delimiters);
            if (length == 0)
                continue;

            count++;
            index += length - 1;
            if (treatConsecutiveDelimitersAsOne)
            {
                while (index + 1 < text.Length && DelimiterLengthAt(text, index + 1, delimiters) is var next && next > 0)
                    index += next;
            }
        }

        return count;
    }

    /// <summary>
    /// The length of the delimiter matching <paramref name="text"/> at <paramref name="index"/>, or 0.
    /// </summary>
    /// <remarks>
    /// r200: <paramref name="delimiters"/> is a SET of delimiters concatenated together, and this used
    /// to test one UTF-16 code unit against it. A custom delimiter outside the BMP is TWO code units,
    /// so each half matched on its own -- splitting inside every unrelated astral character that
    /// happened to share the same surrogate half, and writing the orphaned halves into new cells.
    /// Matching by text element also lets a multi-unit delimiter consume its whole length.
    /// </remarks>
    private static int DelimiterLengthAt(string text, int index, string delimiters)
    {
        if (string.IsNullOrEmpty(delimiters))
            return text[index] == ',' ? 1 : 0;

        // Walked by code point rather than with StringInfo: this runs once per character of every
        // split line, and an enumerator plus a string per delimiter per position turned a
        // 7MB allocation budget into 86MB -- caught by this file's own allocation test.
        for (var d = 0; d < delimiters.Length; d++)
        {
            if (char.IsHighSurrogate(delimiters[d]) &&
                d + 1 < delimiters.Length &&
                char.IsLowSurrogate(delimiters[d + 1]))
            {
                if (index + 1 < text.Length &&
                    text[index] == delimiters[d] &&
                    text[index + 1] == delimiters[d + 1])
                {
                    return 2;
                }

                d++;
                continue;
            }

            if (text[index] == delimiters[d])
                return 1;
        }

        return 0;
    }

    private static List<int> NormalizeBreakPositions(IReadOnlyList<int> breakPositions)
    {
        var positions = new List<int>(breakPositions.Count);
        for (var index = 0; index < breakPositions.Count; index++)
        {
            var position = breakPositions[index];
            if (position > 0)
                positions.Add(position);
        }

        if (positions.Count <= 1)
            return positions;

        positions.Sort();
        var writeIndex = 1;
        for (var readIndex = 1; readIndex < positions.Count; readIndex++)
        {
            if (positions[readIndex] == positions[writeIndex - 1])
                continue;

            positions[writeIndex++] = positions[readIndex];
        }

        if (writeIndex < positions.Count)
            positions.RemoveRange(writeIndex, positions.Count - writeIndex);

        return positions;
    }
}
