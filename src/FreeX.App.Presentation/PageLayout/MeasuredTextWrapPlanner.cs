namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// UI-free policy for greedily wrapping measured text into a bounded number of lines and applying
/// character ellipsis when content does not fit. Renderers retain ownership of native font
/// measurement and drawing by supplying a width-measurement delegate.
/// </summary>
public static class MeasuredTextWrapPlanner
{
    public const string Ellipsis = "\u2026";

    public static IReadOnlyList<string> WrapWithCharacterEllipsis(
        string text,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureWidth);

        if (maxLines <= 0)
            return [];

        var width = Math.Max(1, maxWidth);
        var lines = new List<string>();
        var hardLines = NormalizeHardLines(text);

        var truncated = false;
        for (var hardLineIndex = 0;
             hardLineIndex < hardLines.Length && lines.Count < maxLines && !truncated;
             hardLineIndex++)
        {
            truncated = AddWrappedHardLine(
                lines,
                hardLines[hardLineIndex],
                width,
                measureWidth,
                maxLines);
        }

        if (lines.Count > 0 &&
            !lines[^1].EndsWith(Ellipsis, StringComparison.Ordinal) &&
            (truncated ||
             lines.Count == maxLines && ProducesMoreLines(text, lines.Count, width, measureWidth, maxLines)))
        {
            lines[^1] = TrimToWidth(lines[^1], width, measureWidth);
        }

        return lines;
    }

    private static bool AddWrappedHardLine(
        ICollection<string> lines,
        string hardLine,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines)
    {
        var words = hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            if (lines.Count < maxLines)
                lines.Add("");
            return false;
        }

        var index = 0;
        while (index < words.Length && lines.Count < maxLines)
        {
            var line = words[index++];
            while (index < words.Length && FitsWidth($"{line} {words[index]}", maxWidth, measureWidth))
                line = $"{line} {words[index++]}";

            if (!FitsWidth(line, maxWidth, measureWidth))
            {
                lines.Add(TrimToWidth(line, maxWidth, measureWidth));
                return true;
            }

            lines.Add(line);
        }

        return index < words.Length;
    }

    private static bool ProducesMoreLines(
        string originalText,
        int emittedLineCount,
        double maxWidth,
        Func<string, double> measureWidth,
        int maxLines)
    {
        var replay = new List<string>();
        foreach (var hardLine in NormalizeHardLines(originalText))
        {
            AddWrappedHardLine(replay, hardLine, maxWidth, measureWidth, int.MaxValue);
            if (replay.Count > maxLines)
                return true;
        }

        return replay.Count > emittedLineCount;
    }

    private static string[] NormalizeHardLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static bool FitsWidth(string text, double maxWidth, Func<string, double> measureWidth) =>
        measureWidth(text) <= maxWidth;

    private static string TrimToWidth(string text, double maxWidth, Func<string, double> measureWidth)
    {
        var candidate = text.TrimEnd();
        while (candidate.Length > 0 && !FitsWidth(candidate + Ellipsis, maxWidth, measureWidth))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? Ellipsis : candidate + Ellipsis;
    }
}
