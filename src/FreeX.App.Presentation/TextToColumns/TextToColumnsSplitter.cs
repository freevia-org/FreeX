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

            if (IsDelimiter(ch, delimiters))
            {
                parts.Add(current.ToString());
                current.Clear();
                atFieldStart = true;

                if (treatConsecutiveDelimitersAsOne)
                {
                    while (index + 1 < text.Length && IsDelimiter(text[index + 1], delimiters))
                        index++;
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
            if (!IsDelimiter(text[index], delimiters))
                continue;

            parts[writeIndex++] = text.Substring(start, index - start);

            if (treatConsecutiveDelimitersAsOne)
            {
                while (index + 1 < text.Length && IsDelimiter(text[index + 1], delimiters))
                    index++;
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
            if (!IsDelimiter(text[index], delimiters))
                continue;

            count++;
            if (treatConsecutiveDelimitersAsOne)
            {
                while (index + 1 < text.Length && IsDelimiter(text[index + 1], delimiters))
                    index++;
            }
        }

        return count;
    }

    private static bool IsDelimiter(char ch, string delimiters)
    {
        if (string.IsNullOrEmpty(delimiters))
            return ch == ',';

        return delimiters.Length == 1
            ? ch == delimiters[0]
            : delimiters.IndexOf(ch) >= 0;
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
