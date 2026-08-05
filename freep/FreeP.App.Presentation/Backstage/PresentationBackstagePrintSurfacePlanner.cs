using Free.Shared.Shell;

namespace FreeP.App.Compositor;

public sealed record PresentationBackstagePrintChoiceRow(
    string Label,
    string Description,
    bool IsSelected,
    bool IsAvailable);

public sealed record PresentationBackstagePrintChoiceGroup(
    string Heading,
    IReadOnlyList<PresentationBackstagePrintChoiceRow> Choices);

public sealed record PresentationBackstagePrintAction(
    string Label,
    string AutomationId,
    string HelpText,
    bool IsEnabled,
    PresentationPrintRequest Request);

public sealed record PresentationBackstagePrintSurface(
    string Heading,
    string Description,
    IReadOnlyList<BackstageFieldRow> Settings,
    IReadOnlyList<PresentationBackstagePrintChoiceGroup> ChoiceGroups,
    string CustomRangeHeading,
    string CustomRangeDescription,
    string CustomRangePlaceholder,
    string CustomRangeApplyLabel,
    string CustomRangeInputAutomationId,
    string CustomRangeApplyAutomationId,
    string CustomRangeText,
    string StatusText,
    string PrintHeading,
    IReadOnlyList<PresentationBackstagePrintAction> PrintActions);

/// <summary>
/// Projects the FreeP print plan into a renderer-neutral Backstage surface, including labels,
/// availability, automation identifiers, custom-range policy, and native handoff status.
/// </summary>
public static class PresentationBackstagePrintSurfacePlanner
{
    public static PresentationBackstagePrintSurface Build(PresentationPrintBackstagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new PresentationBackstagePrintSurface(
            plan.Heading,
            plan.Description,
            BuildSettings(plan),
            BuildChoiceGroups(plan),
            CustomRangeHeading: "Custom Range",
            CustomRangeDescription: "Enter slide numbers and ranges, for example 2,4-6.",
            CustomRangePlaceholder: "e.g. 2,4-6",
            CustomRangeApplyLabel: "Apply range",
            CustomRangeInputAutomationId: "FreePPrintCustomRangeInput",
            CustomRangeApplyAutomationId: "FreePPrintCustomRangeApply",
            CustomRangeText: plan.SelectedRange.Request?.CustomRangeText ?? string.Empty,
            StatusText: plan.DisabledReason ?? plan.NativePrintHandoff.Reason,
            PrintHeading: "Print",
            PrintActions: plan.LayoutChoices.Select(choice => BuildPrintAction(choice, plan)).ToArray());
    }

    public static string? NormalizeCustomRangeText(string? rangeText)
    {
        var normalized = rangeText?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static PresentationPrintRequest? BuildCustomRangeRequest(string? rangeText)
    {
        var normalized = NormalizeCustomRangeText(rangeText);
        return normalized is null
            ? null
            : new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: normalized));
    }

    private static IReadOnlyList<BackstageFieldRow> BuildSettings(PresentationPrintBackstagePlan plan) =>
    [
        new("Layout", plan.SelectedLayout.Layout.DisplayName),
        new("Slides", plan.SlideRangeSummary),
        new("Pages", plan.PageCount.ToString()),
        new("Preview", plan.PreviewPlan.PageCountText),
        new("Hidden slides", plan.PrintHiddenSlides ? "Included" : "Not included"),
        new("Options", plan.Options.DisplaySummary),
        new("Native printer handoff", plan.NativePrintHandoff.StatusText),
    ];

    private static IReadOnlyList<PresentationBackstagePrintChoiceGroup> BuildChoiceGroups(
        PresentationPrintBackstagePlan plan) =>
    [
        new("Output Options", plan.OutputOptionChoices.Select(choice => new PresentationBackstagePrintChoiceRow(
            $"{choice.Group}: {choice.DisplayName}",
            choice.Description,
            choice.IsSelected,
            choice.IsAvailable)).ToArray()),
        new("Preview", plan.PreviewPlan.Pages.Select(page => new PresentationBackstagePrintChoiceRow(
            page.ThumbnailLabel,
            page.Detail,
            page.PageNumber == 1,
            IsAvailable: true)).ToArray()),
        new("Layouts", plan.LayoutChoices.Select(choice => new PresentationBackstagePrintChoiceRow(
            choice.Layout.DisplayName,
            choice.PackagePlan.LayoutSummary,
            choice.IsSelected,
            IsAvailable: true)).ToArray()),
        new("Slide Range", plan.RangeChoices.Select(choice => new PresentationBackstagePrintChoiceRow(
            choice.DisplayName,
            choice.Description,
            choice.Kind == plan.SelectedRange.Kind,
            choice.IsAvailable)).ToArray()),
    ];

    private static PresentationBackstagePrintAction BuildPrintAction(
        PresentationPrintBackstageLayoutChoice choice,
        PresentationPrintBackstagePlan plan)
    {
        var request = new PresentationPrintRequest(
            choice.Layout.Layout,
            plan.SelectedRange.Request,
            HandoutSlidesPerPage: choice.Layout.SlidesPerPage);
        var canPrint = choice.PackagePlan.CanBuildPackage &&
            (plan.NativePrintHandoff.CanOpenNativePrintDialog ||
             plan.NativePrintHandoff.CanSubmitToNativePrinter);

        return new PresentationBackstagePrintAction(
            $"Print {choice.Layout.DisplayName}",
            "BackstagePrint_" + AutomationToken(choice.Layout.DisplayName),
            canPrint ? choice.PackagePlan.LayoutSummary : plan.NativePrintHandoff.Reason,
            canPrint,
            request);
    }

    private static string AutomationToken(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));
}
