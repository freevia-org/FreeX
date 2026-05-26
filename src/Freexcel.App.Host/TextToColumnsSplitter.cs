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
        var delimiterChars = string.IsNullOrEmpty(delimiters)
            ? [',']
            : delimiters.Distinct().ToArray();

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

            if (!inQualifiedText && delimiterChars.Contains(ch))
            {
                parts.Add(current.ToString());
                current.Clear();

                if (treatConsecutiveDelimitersAsOne)
                {
                    while (index + 1 < text.Length && delimiterChars.Contains(text[index + 1]))
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
        var positions = breakPositions
            .Where(position => position > 0)
            .Distinct()
            .Order()
            .ToList();
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
}
