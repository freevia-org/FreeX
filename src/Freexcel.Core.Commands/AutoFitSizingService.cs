namespace Freexcel.Core.Commands;

public static class AutoFitSizingService
{
    public const double MinimumColumnWidth = 24.0;
    public const double MaximumColumnWidth = 300.0;
    public const double MinimumRowHeight = 16.0;
    public const double MaximumRowHeight = 220.0;

    public static double EstimateColumnWidth(IEnumerable<string> displayTexts, double defaultWidth)
    {
        var longestLine = 0;
        foreach (var text in displayTexts)
        {
            foreach (var line in EnumerateLines(text))
                longestLine = Math.Max(longestLine, line.Length);
        }

        var estimate = longestLine == 0
            ? defaultWidth
            : Math.Max(defaultWidth, longestLine + 2.0);

        return Math.Clamp(estimate, MinimumColumnWidth, MaximumColumnWidth);
    }

    public static double EstimateRowHeight(IEnumerable<string> displayTexts, double defaultHeight)
    {
        var maxLineCount = 0;
        foreach (var text in displayTexts)
            maxLineCount = Math.Max(maxLineCount, EstimateLineCount(text));

        var lineHeight = Math.Max(defaultHeight, MinimumRowHeight);
        var estimate = maxLineCount == 0
            ? defaultHeight
            : Math.Max(defaultHeight, maxLineCount * lineHeight);

        return Math.Clamp(estimate, MinimumRowHeight, MaximumRowHeight);
    }

    private static int EstimateLineCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var lineCount = 0;
        foreach (var line in EnumerateLines(text))
            lineCount += Math.Max(1, (int)Math.Ceiling(line.Length / 40.0));

        return lineCount;
    }

    private static IEnumerable<string> EnumerateLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return "";
            yield break;
        }

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }
}
