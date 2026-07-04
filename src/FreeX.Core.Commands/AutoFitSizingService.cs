namespace FreeX.Core.Commands;

/// <summary>
/// One cell's contribution to an AutoFit measurement: its display text, whether Wrap Text is
/// enabled for it, and its own column's width (in the same character-based unit as
/// <see cref="AutoFitSizingService.EstimateColumnWidth"/>). <see cref="AutoFitSizingService.EstimateRowHeight"/>
/// uses the wrap flag and column width together to size wrapped multi-line text to the number of
/// visual lines it occupies at that column's width, matching Excel.
/// </summary>
public readonly record struct AutoFitCellText(string Text, bool WrapText = false, double ColumnWidth = 0);

public static class AutoFitSizingService
{
    public const double MinimumColumnWidth = 3.0;
    public const double MaximumColumnWidth = 255.0;
    public const double MinimumRowHeight = 16.0;
    public const double MaximumRowHeight = 220.0;

    /// <summary>Characters of usable width per column-width unit, mirroring the "+2.0" padding used by <see cref="EstimateColumnWidth"/>.</summary>
    private const double ColumnWidthPadding = 2.0;

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
            : Math.Max(defaultWidth, longestLine + ColumnWidthPadding);

        return Math.Clamp(estimate, MinimumColumnWidth, MaximumColumnWidth);
    }

    /// <summary>
    /// Estimates AutoFit row height from each cell's display text and line count. When a cell has
    /// Wrap Text enabled, its text is additionally wrapped at its own <see cref="AutoFitCellText.ColumnWidth"/>
    /// (the same character-based metric used by <see cref="EstimateColumnWidth"/>) so long single-line
    /// content that visually wraps in a narrow column is sized to its wrapped line count, matching
    /// Excel's "AutoFit Row Height" behavior for wrapped cells.
    /// </summary>
    public static double EstimateRowHeight(IEnumerable<AutoFitCellText> displayTexts, double defaultHeight)
    {
        var maxLineCount = 0;
        foreach (var cellText in displayTexts)
            maxLineCount = Math.Max(maxLineCount, EstimateLineCount(cellText.Text, cellText.WrapText, cellText.ColumnWidth));

        var lineHeight = Math.Max(defaultHeight, MinimumRowHeight);
        var estimate = maxLineCount == 0
            ? defaultHeight
            : Math.Max(defaultHeight, maxLineCount * lineHeight);

        return Math.Clamp(estimate, MinimumRowHeight, MaximumRowHeight);
    }

    /// <summary>Back-compat overload for callers without wrap/column-width information (no wrapping applied).</summary>
    public static double EstimateRowHeight(IEnumerable<string> displayTexts, double defaultHeight) =>
        EstimateRowHeight(displayTexts.Select(text => new AutoFitCellText(text)), defaultHeight);

    private static int EstimateLineCount(string text, bool wrapText, double columnWidth)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var lineCount = 0;
        foreach (var line in EnumerateLines(text))
            lineCount += wrapText ? EstimateWrappedLineCount(line, columnWidth) : 1;

        return lineCount;
    }

    /// <summary>
    /// Estimates how many visual lines a single logical line wraps onto at the given column width,
    /// using the same character-count metric as <see cref="EstimateColumnWidth"/> (usable width is
    /// the column width less the same padding subtracted there).
    /// </summary>
    private static int EstimateWrappedLineCount(string line, double columnWidth)
    {
        if (line.Length == 0)
            return 1;

        var usableChars = (int)Math.Floor(columnWidth - ColumnWidthPadding);
        if (usableChars < 1)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(line.Length / (double)usableChars));
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
