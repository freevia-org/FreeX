using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Resolves the body translation used by Word's section-level vertical alignment when a page has
/// unused content height. Justified alignment changes inter-paragraph spacing and therefore remains
/// on the normal top-flow path rather than being approximated by a translation.
/// </summary>
public static class PageVerticalAlignmentPlanner
{
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
