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
    Legal,
    B4,
    B5
}

public enum PageLayoutScaleField
{
    Width,
    Height,
    Percent
}

public enum PageLayoutPageSetupOpenSource
{
    DialogButton,
    CustomMargins,
    ExtendedPaperSize,
    ScaleToFit,
    PrintArea,
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

    /// <summary>
    /// Resolves the header/footer distance-from-edge (in inches) that Excel's Margins gallery applies
    /// alongside the Left/Right/Top/Bottom margins for a given preset. Normal and Narrow both use
    /// Excel's 0.3in header/footer; Wide uses 0.5in.
    /// </summary>
    public static (double HeaderMargin, double FooterMargin) ResolveHeaderFooterMargins(
        PageLayoutMarginPreset preset) =>
        preset switch
        {
            PageLayoutMarginPreset.Wide => (0.5, 0.5),
            _ => (0.3, 0.3)
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
            PageLayoutPaperSizePreset.B4 => WorksheetPaperSize.B4,
            PageLayoutPaperSizePreset.B5 => WorksheetPaperSize.B5,
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

    public static PageLayoutScaleCommitPlan PlanScaleCommit(
        PageLayoutScaleField field,
        WorksheetScaleToFit current,
        string text) =>
        field switch
        {
            PageLayoutScaleField.Width => PlanScaleWidthCommit(current, text),
            PageLayoutScaleField.Height => PlanScaleHeightCommit(current, text),
            PageLayoutScaleField.Percent => PlanScalePercentCommit(current, text),
            _ => PageLayoutScaleCommitPlan.Revert(current)
        };

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
