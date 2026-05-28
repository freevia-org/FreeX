namespace Freexcel.App.Host;

internal static class TextToColumnsSplitter
{
    public static string[] SplitText(string text, string delimiters)
    {
        return SplitText(text, delimiters, '"', false);
    }

    public static string[] SplitText(
        string text,
        string delimiters,
        char? textQualifier,
        bool treatConsecutiveDelimitersAsOne)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQualifiedText = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (textQualifier is { } qualifier && ch == qualifier)
            {
                if (inQualifiedText && index + 1 < text.Length && text[index + 1] == qualifier)
                {
                    current.Append(qualifier);
                    index++;
                    continue;
                }

                inQualifiedText = !inQualifiedText;
                continue;
            }

            if (!inQualifiedText && IsDelimiter(ch, delimiters))
            {
                parts.Add(current.ToString());
                current.Clear();

                if (treatConsecutiveDelimitersAsOne)
                {
                    while (index + 1 < text.Length && IsDelimiter(text[index + 1], delimiters))
                        index++;
                }

                continue;
            }

            current.Append(ch);
        }

        parts.Add(current.ToString());
        return parts.ToArray();
    }

    public static string[] SplitFixedWidthText(string text, IReadOnlyList<int> breakPositions)
    {
        var positions = NormalizeBreakPositions(breakPositions);
        if (positions.Count == 0)
            return [text];

        var parts = new List<string>();
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
