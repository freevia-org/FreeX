using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public static class PageBorderVisibilityPlanner
{
    public static bool ShouldRender(PageBorderDisplay display, int zeroBasedPageIndex)
    {
        var pageIndex = Math.Max(0, zeroBasedPageIndex);
        return display switch
        {
            PageBorderDisplay.FirstPage => pageIndex == 0,
            PageBorderDisplay.NotFirstPage => pageIndex > 0,
            _ => true,
        };
    }
}
