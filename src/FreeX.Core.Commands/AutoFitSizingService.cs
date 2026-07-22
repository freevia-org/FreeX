namespace FreeX.Core.Commands;

/// <summary>
/// One cell's contribution to an AutoFit measurement: its display text, whether Wrap Text is
/// enabled for it, its own column's width (in the same character-based unit as
/// <see cref="AutoFitSizingService.EstimateColumnWidth"/>), and its TextRotation (Format Cells >
/// Alignment > Orientation; -90..90 degrees, or 255 for stacked/vertical text, matching the
/// normalized range used by <c>CellStyle.TextRotation</c> and
/// <c>CellTextOrientationLayoutPlanner</c>). <see cref="AutoFitSizingService.EstimateRowHeight"/>
/// uses the wrap flag and column width together to size wrapped multi-line text to the number of
/// visual lines it occupies at that column's width, and uses TextRotation to grow the row for
/// angled or stacked text, matching Excel.
/// </summary>
public readonly record struct AutoFitCellText(string Text, bool WrapText = false, double ColumnWidth = 0, int TextRotation = 0);

public static class AutoFitSizingService
{
    public const double MinimumColumnWidth = 3.0;
    public const double MaximumColumnWidth = 255.0;
    public const double MinimumRowHeight = 16.0;
    public const double MaximumRowHeight = 220.0;

    /// <summary>Characters of usable width per column-width unit, mirroring the "+2.0" padding used by <see cref="EstimateColumnWidth"/>.</summary>
    private const double ColumnWidthPadding = 2.0;

    /// <summary>
    /// Approximate glyph width as a fraction of line height, used only to project a rotated
    /// text run's on-screen bounding-box height in the absence of real font metrics (this
    /// service is character-count based throughout, never measuring actual glyphs).
    /// </summary>
    private const double CharacterWidthToLineHeightRatio = 0.6;

    /// <summary>
    /// Estimates AutoFit column width from each cell's display text, narrowing the estimate for
    /// cells with a non-zero <see cref="AutoFitCellText.TextRotation"/> (255 for stacked/vertical
    /// text, or an angled -90..90 orientation) instead of measuring the unrotated string length --
    /// stacked text only needs one glyph's width per column, and an angled run's horizontal
    /// footprint is shorter than its full character count, matching Excel narrowing columns for
    /// rotated/stacked content rather than over-widening them.
    /// </summary>
    public static double EstimateColumnWidth(IEnumerable<AutoFitCellText> displayTexts, double defaultWidth)
    {
        var longestUnits = 0.0;
        foreach (var cellText in displayTexts)
        {
            var longestLine = 0;
            foreach (var line in EnumerateLines(cellText.Text))
                longestLine = Math.Max(longestLine, line.Length);

            var widthUnits = cellText.TextRotation == 0
                ? longestLine
                : EstimateRotatedWidthUnits(longestLine, cellText.TextRotation);
            longestUnits = Math.Max(longestUnits, widthUnits);
        }

        var estimate = longestUnits <= 0
            ? defaultWidth
            : Math.Max(defaultWidth, longestUnits + ColumnWidthPadding);

        return Math.Clamp(estimate, MinimumColumnWidth, MaximumColumnWidth);
    }

    /// <summary>Back-compat overload for callers without rotation information (no rotation applied).</summary>
    public static double EstimateColumnWidth(IEnumerable<string> displayTexts, double defaultWidth) =>
        EstimateColumnWidth(displayTexts.Select(text => new AutoFitCellText(text)), defaultWidth);

    /// <summary>
    /// Estimates AutoFit row height from each cell's display text and line count. When a cell has
    /// Wrap Text enabled, its text is additionally wrapped at its own <see cref="AutoFitCellText.ColumnWidth"/>
    /// (the same character-based metric used by <see cref="EstimateColumnWidth"/>) so long single-line
    /// content that visually wraps in a narrow column is sized to its wrapped line count, matching
    /// Excel's "AutoFit Row Height" behavior for wrapped cells. When a cell has a non-zero
    /// <see cref="AutoFitCellText.TextRotation"/> (an angled orientation, or 255 for
    /// stacked/vertical text), the row height instead grows from the rotated text's projected
    /// bounding-box height (an angled long string projects a taller box; stacked text needs one
    /// line-height per character) rather than the plain line count, matching Excel's AutoFit for
    /// rotated cells. A cell with no rotation is unaffected -- its height is computed exactly as
    /// before.
    /// </summary>
    public static double EstimateRowHeight(IEnumerable<AutoFitCellText> displayTexts, double defaultHeight)
    {
        var maxHeightUnits = 0.0;
        foreach (var cellText in displayTexts)
        {
            var heightUnits = cellText.TextRotation == 0
                ? EstimateLineCount(cellText.Text, cellText.WrapText, cellText.ColumnWidth)
                : EstimateRotatedHeightUnits(cellText.Text, cellText.TextRotation);
            maxHeightUnits = Math.Max(maxHeightUnits, heightUnits);
        }

        var lineHeight = Math.Max(defaultHeight, MinimumRowHeight);
        var estimate = maxHeightUnits <= 0
            ? defaultHeight
            : Math.Max(defaultHeight, maxHeightUnits * lineHeight);

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

    /// <summary>
    /// Estimates the row-height contribution (in multiples of one line-height) of a rotated or
    /// stacked cell's longest logical line, ignoring Wrap Text (Excel's rotated/vertical
    /// orientations are sized from the unwrapped run).
    /// </summary>
    private static double EstimateRotatedHeightUnits(string text, int textRotation)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var longestLine = 0;
        foreach (var line in EnumerateLines(text))
            longestLine = Math.Max(longestLine, line.Length);

        if (longestLine == 0)
            return 1;

        // Stacked/vertical text (raw rotation 255): Excel stacks one glyph per line (see
        // CellTextOrientationLayoutPlanner.PrepareDisplayText, which splits the run onto one line
        // per character for rendering), so the row needs one line-height per character.
        if (textRotation == 255)
            return longestLine;

        // Any other out-of-range value (shouldn't occur for normalized model data) renders
        // unrotated, matching CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay.
        if (textRotation is < -90 or > 90)
            return 1;

        // Angled text (Format Cells > Alignment > Angle, -90..90 degrees): project the run's
        // rotated bounding box, the same trig used by CellTextOrientationLayoutPlanner.CalculateLayout
        // for rendering. Character width is approximated as a fraction of line height
        // (CharacterWidthToLineHeightRatio) since this service has no real font metrics.
        var radians = Math.Abs(textRotation) * Math.PI / 180.0;
        var runWidthUnits = longestLine * CharacterWidthToLineHeightRatio;
        var projectedHeightUnits = runWidthUnits * Math.Sin(radians) + Math.Cos(radians);
        return Math.Max(1, projectedHeightUnits);
    }

    /// <summary>
    /// Estimates the column-width contribution (in the same character-count unit as
    /// <see cref="EstimateColumnWidth"/>) of a rotated or stacked cell's longest logical line --
    /// the horizontal counterpart of <see cref="EstimateRotatedHeightUnits"/>.
    /// </summary>
    private static double EstimateRotatedWidthUnits(int longestLine, int textRotation)
    {
        if (longestLine <= 0)
            return 0;

        // Stacked/vertical text (raw rotation 255): Excel stacks one glyph per line (see
        // CellTextOrientationLayoutPlanner.PrepareDisplayText), so the column only needs to be as
        // wide as a single character, regardless of the string's length.
        if (textRotation == 255)
            return 1;

        // Any other out-of-range value (shouldn't occur for normalized model data) renders
        // unrotated, matching CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay.
        if (textRotation is < -90 or > 90)
            return longestLine;

        // Angled text (Format Cells > Alignment > Angle, -90..90 degrees): project the run's
        // rotated bounding box onto the horizontal axis -- the inverse of
        // EstimateRotatedHeightUnits's vertical projection, using the same trig and
        // character/line-height ratio.
        var radians = Math.Abs(textRotation) * Math.PI / 180.0;
        var projectedWidthUnits = longestLine * Math.Cos(radians) + Math.Sin(radians) / CharacterWidthToLineHeightRatio;
        return Math.Max(1, projectedWidthUnits);
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
