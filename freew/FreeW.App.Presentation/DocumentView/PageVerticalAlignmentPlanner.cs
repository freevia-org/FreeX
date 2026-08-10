using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Resolves the body translation used by Word's section-level vertical alignment when a page has
/// unused content height. Justified alignment changes inter-paragraph spacing and therefore remains
/// on the normal top-flow path rather than being approximated by a translation.
/// </summary>
public static class PageVerticalAlignmentPlanner
{
    public readonly record struct BodyFlowStart(
        int BlockIndex,
        int ColumnIndex,
        double PageSpaceY);

    public static PageVerticalAlignment Next(PageVerticalAlignment alignment) => alignment switch
    {
        PageVerticalAlignment.Top => PageVerticalAlignment.Center,
        PageVerticalAlignment.Center => PageVerticalAlignment.Bottom,
        PageVerticalAlignment.Bottom => PageVerticalAlignment.Justified,
        _ => PageVerticalAlignment.Top,
    };

    /// <summary>
    /// Orders the first rendered occurrence of each body block using Word's reading order:
    /// columns from left to right, then content from top to bottom within each column.
    /// A block that continues into a later column still contributes only its earliest start,
    /// so vertical justification never inserts a gap inside that block merely because it wraps.
    /// </summary>
    public static IReadOnlyList<BodyFlowStart> OrderBodyStartsByColumn(
        IEnumerable<BodyFlowStart> starts)
    {
        ArgumentNullException.ThrowIfNull(starts);

        return starts
            .GroupBy(start => start.BlockIndex)
            .Select(group => group
                .OrderBy(start => start.ColumnIndex)
                .ThenBy(start => start.PageSpaceY)
                .First())
            .OrderBy(start => start.ColumnIndex)
            .ThenBy(start => start.PageSpaceY)
            .ThenBy(start => start.BlockIndex)
            .ToArray();
    }

    public static double ResolveBodyOffset(PageVerticalAlignment alignment, double freeSpaceDip)
    {
        var freeSpace = Math.Max(0, freeSpaceDip);
        return alignment switch
        {
            PageVerticalAlignment.Center => freeSpace / 2,
            PageVerticalAlignment.Bottom => freeSpace,
            _ => 0
        };
    }

    /// <summary>
    /// Resolves the extra space Word distributes at each paragraph boundary for section
    /// vertical alignment <c>both</c>. The caller supplies the number of boundaries on the
    /// page, so a page with one flow block remains unchanged.
    /// </summary>
    public static double ResolveJustifiedParagraphGap(
        PageVerticalAlignment alignment,
        double freeSpaceDip,
        int paragraphGapCount)
    {
        if (alignment != PageVerticalAlignment.Justified || paragraphGapCount <= 0)
            return 0;

        return Math.Max(0, freeSpaceDip) / paragraphGapCount;
    }
}
