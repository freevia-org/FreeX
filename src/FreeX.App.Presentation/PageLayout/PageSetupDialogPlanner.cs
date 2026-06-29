using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Renderer-neutral Page Setup dialog surface metadata. Platform shells own control construction;
/// this planner owns shared dimensions, automation contracts, and combo catalogs.
/// </summary>
public sealed record PageSetupChoicePlan<T>(IReadOnlyList<PageSetupChoice<T>> Choices, T FallbackValue)
{
    public int IndexOf(T value) =>
        PageSetupDialogModel.ChoiceIndex(Choices, value, FallbackValue);

    public T ValueAt(int selectedIndex) =>
        PageSetupDialogModel.ChoiceValue(Choices, selectedIndex, FallbackValue);
}

public sealed record PageSetupDialogOpenPlan(
    PageSetupInitialFocusTarget InitialFocusTarget,
    PageSetupValidationRoute InitialRoute);

public static class PageSetupDialogPlanner
{
    public const string TitleResourceKey = "PageSetup_Title";
    public const string DialogAutomationId = "PageSetupDialog";
    public const string TabsAutomationId = "PageSetupTabs";

    public const string OrientationBoxAutomationId = "PageSetupOrientationBox";
    public const string PaperSizeBoxAutomationId = "PageSetupPaperSizeBox";
    public const string HeaderPresetBoxAutomationId = "PageSetupHeaderPresetBox";
    public const string FooterPresetBoxAutomationId = "PageSetupFooterPresetBox";
    public const string PageOrderBoxAutomationId = "PageSetupPageOrderBox";
    public const string CellErrorsBoxAutomationId = "PageSetupCellErrorsBox";
    public const string CommentsBoxAutomationId = "PageSetupCommentsBox";
    public const string ValidationTextAutomationId = "PageSetupValidationText";
    public const string OkButtonAutomationId = "PageSetupOkButton";
    public const string CancelButtonAutomationId = "PageSetupCancelButton";
    public const string PrintButtonAutomationId = "PageSetupPrintButton";
    public const string PrintPreviewButtonAutomationId = "PageSetupPrintPreviewButton";
    public const string OptionsButtonAutomationId = "PageSetupOptionsButton";

    public const double WindowWidth = 600;
    public const double WindowHeight = 560;
    public const double MinWindowWidth = 580;
    public const double MinWindowHeight = 520;
    public const double FieldMinWidth = 220;
    public const double HeaderFooterPresetMinWidth = 260;
    public const double FooterButtonMinWidth = 84;
    public const double PrintPreviewButtonMinWidth = 100;

    public static PageSetupChoicePlan<WorksheetPageOrientation> OrientationChoices { get; } =
        new(PageSetupDialogModel.OrientationChoices, WorksheetPageOrientation.Portrait);

    public static PageSetupChoicePlan<WorksheetPaperSize> PaperSizeChoices { get; } =
        new(PageSetupDialogModel.PaperSizeChoices, WorksheetPaperSize.A4);

    public static PageSetupChoicePlan<WorksheetPageOrder> PageOrderChoices { get; } =
        new(PageSetupDialogModel.PageOrderChoices, WorksheetPageOrder.DownThenOver);

    public static PageSetupChoicePlan<WorksheetPrintErrorValue> PrintErrorValueChoices { get; } =
        new(PageSetupDialogModel.PrintErrorValueChoices, WorksheetPrintErrorValue.Displayed);

    public static PageSetupChoicePlan<WorksheetPrintComments> PrintCommentChoices { get; } =
        new(PageSetupDialogModel.PrintCommentChoices, WorksheetPrintComments.None);

    public static PageSetupDialogOpenPlan PlanOpen(PageLayoutPageSetupOpenSource source) =>
        PlanOpen(ResolveInitialFocusTarget(source));

    public static PageSetupDialogOpenPlan PlanOpen(PageSetupInitialFocusTarget initialFocusTarget) =>
        new(initialFocusTarget, ResolveInitialFocusRoute(initialFocusTarget));

    public static PageSetupInitialFocusTarget ResolveInitialFocusTarget(PageLayoutPageSetupOpenSource source) =>
        source switch
        {
            PageLayoutPageSetupOpenSource.CustomMargins => PageSetupInitialFocusTarget.Margins,
            PageLayoutPageSetupOpenSource.ExtendedPaperSize => PageSetupInitialFocusTarget.PaperSize,
            PageLayoutPageSetupOpenSource.ScaleToFit => PageSetupInitialFocusTarget.ScaleToFit,
            PageLayoutPageSetupOpenSource.PrintTitles => PageSetupInitialFocusTarget.RepeatRows,
            _ => PageSetupInitialFocusTarget.PageOrientation
        };

    public static PageSetupValidationRoute ResolveInitialFocusRoute(PageSetupInitialFocusTarget initialFocusTarget) =>
        initialFocusTarget switch
        {
            PageSetupInitialFocusTarget.Margins =>
                new(PageSetupDialogTab.Margins, PageSetupDialogField.Margins),
            PageSetupInitialFocusTarget.PaperSize =>
                new(PageSetupDialogTab.Page, PageSetupDialogField.PaperSize),
            PageSetupInitialFocusTarget.ScaleToFit =>
                new(PageSetupDialogTab.Page, PageSetupDialogField.Scaling),
            PageSetupInitialFocusTarget.RepeatRows =>
                new(PageSetupDialogTab.Sheet, PageSetupDialogField.RepeatRows),
            _ => new(PageSetupDialogTab.Page, PageSetupDialogField.Orientation)
        };

    public static IReadOnlyList<string> ResolveChoiceLabels<T>(
        PageSetupChoicePlan<T> plan,
        Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textProvider);

        return ResolveChoiceLabels(plan.Choices, textProvider);
    }

    public static IReadOnlyList<string> ResolveChoiceLabels<T>(
        IReadOnlyList<PageSetupChoice<T>> choices,
        Func<string, string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(textProvider);

        return choices.Select(choice => textProvider(choice.LabelResourceKey)).ToArray();
    }
}
