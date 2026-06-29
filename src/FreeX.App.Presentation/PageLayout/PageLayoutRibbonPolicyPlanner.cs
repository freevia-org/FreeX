using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageLayoutMarginPreset
{
    Normal,
    Wide,
    Narrow
}

public enum PageLayoutOrientationPreset
{
    Portrait,
    Landscape
}

public enum PageLayoutPaperSizePreset
{
    Letter,
    A4,
    Legal
}

public enum PageLayoutPageSetupOpenSource
{
    DialogButton,
    CustomMargins,
    ExtendedPaperSize,
    ScaleToFit,
    PrintTitles
}

public sealed record PageLayoutScaleCommitPlan(bool ShouldApply, WorksheetScaleToFit ScaleToFit)
{
    public static PageLayoutScaleCommitPlan Revert(WorksheetScaleToFit current) => new(false, current);

    public static PageLayoutScaleCommitPlan Apply(WorksheetScaleToFit scaleToFit) => new(true, scaleToFit);
}

/// <summary>
/// Portable Page Layout ribbon policy. Renderers still own menus, dialogs, and command dispatch; this
/// planner owns the Office-style mapping from visible ribbon choices to model values or Page Setup routes.
/// </summary>
public static class PageLayoutRibbonPolicyPlanner
{
    public static WorksheetPageMargins ResolveMargins(PageLayoutMarginPreset preset) =>
        preset switch
        {
            PageLayoutMarginPreset.Wide => WorksheetPageMargins.Wide,
            PageLayoutMarginPreset.Narrow => WorksheetPageMargins.Narrow,
            _ => WorksheetPageMargins.Normal
        };

    public static WorksheetPageOrientation ResolveOrientation(PageLayoutOrientationPreset preset) =>
        preset == PageLayoutOrientationPreset.Landscape
            ? WorksheetPageOrientation.Landscape
            : WorksheetPageOrientation.Portrait;

    public static WorksheetPaperSize ResolvePaperSize(PageLayoutPaperSizePreset preset) =>
        preset switch
        {
            PageLayoutPaperSizePreset.Letter => WorksheetPaperSize.Letter,
            PageLayoutPaperSizePreset.Legal => WorksheetPaperSize.Legal,
            _ => WorksheetPaperSize.A4
        };

    public static PageSetupInitialFocusTarget ResolvePageSetupInitialFocus(PageLayoutPageSetupOpenSource source) =>
        PageSetupDialogPlanner.ResolveInitialFocusTarget(source);

    public static PageLayoutScaleCommitPlan PlanScaleWidthCommit(
        WorksheetScaleToFit current,
        string text)
    {
        return PageLayoutInputParser.TryParseScalePages(text, out var pagesWide)
            ? PageLayoutScaleCommitPlan.Apply(
                PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(
                    current,
                    pagesWide,
                    current.FitToPagesTall))
            : PageLayoutScaleCommitPlan.Revert(current);
    }

    public static PageLayoutScaleCommitPlan PlanScaleHeightCommit(
        WorksheetScaleToFit current,
        string text)
    {
        return PageLayoutInputParser.TryParseScalePages(text, out var pagesTall)
            ? PageLayoutScaleCommitPlan.Apply(
                PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(
                    current,
                    current.FitToPagesWide,
                    pagesTall))
            : PageLayoutScaleCommitPlan.Revert(current);
    }

    public static PageLayoutScaleCommitPlan PlanScalePercentCommit(
        WorksheetScaleToFit current,
        string text)
    {
        return PageLayoutInputParser.TryParseScalePercent(text, out var percent)
            ? PageLayoutScaleCommitPlan.Apply(PageLayoutRibbonCommandPlanner.ResolveScalePercent(percent))
            : PageLayoutScaleCommitPlan.Revert(current);
    }
}
