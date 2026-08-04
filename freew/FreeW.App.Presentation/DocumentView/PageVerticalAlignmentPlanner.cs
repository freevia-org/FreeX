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
}
