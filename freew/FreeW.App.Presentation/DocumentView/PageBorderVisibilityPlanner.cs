using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum PageBorderRenderLayer
{
    BehindText,
    InFrontOfText
}

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

    public static PageBorderRenderLayer LayerFor(PageBorderZOrder zOrder) =>
        zOrder == PageBorderZOrder.Behind
            ? PageBorderRenderLayer.BehindText
            : PageBorderRenderLayer.InFrontOfText;
}
