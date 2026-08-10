using System.Globalization;
using Free.Shared.Localization;
using Free.Shared.Shell;

namespace FreeP.App.Compositor;

public sealed record PresentationBackstagePrintChoiceRow(
    string Label,
    string Description,
    bool IsSelected,
    bool IsAvailable,
    string DisplayText);

public enum PresentationBackstagePrintChoiceGroupKind
{
    OutputOptions,
    Preview,
    Layouts,
    SlideRange,
}

public sealed record PresentationBackstagePrintChoiceGroup(
    string StableId,
    PresentationBackstagePrintChoiceGroupKind Kind,
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
    string SettingsHeading,
    IReadOnlyList<BackstageFieldRow> Settings,
    IReadOnlyList<PresentationBackstagePrintChoiceGroup> ChoiceGroups,
    string CustomRangeHeading,
    string CustomRangeDescription,
    string CustomRangePlaceholder,
    string CustomRangeApplyLabel,
    LocalizedTextDescriptor CustomRangeApplyHelpText,
    string CustomRangeInputAutomationId,
    string CustomRangeApplyAutomationId,
    string CustomRangeText,
    string StatusText,
    PresentationNativePrintSurfacePlan NativePrint,
    string PrintHeading,
    IReadOnlyList<PresentationBackstagePrintAction> PrintActions);

/// <summary>
/// Projects the FreeP print plan into a renderer-neutral Backstage surface, including labels,
/// availability, automation identifiers, custom-range policy, and native handoff status.
/// </summary>
public static class PresentationBackstagePrintSurfacePlanner
{
    public static PresentationBackstagePrintSurface Build(
        PresentationPrintBackstagePlan plan,
        int? selectedPreviewPageNumber = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new PresentationBackstagePrintSurface(
            plan.Heading,
            plan.Description,
            SettingsHeading: "Settings",
            BuildSettings(plan),
            BuildChoiceGroups(plan, selectedPreviewPageNumber),
            CustomRangeHeading: "Custom range",
            CustomRangeDescription: "Enter slide numbers and ranges, for example 2,4-6.",
            CustomRangePlaceholder: "e.g. 2,4-6",
            CustomRangeApplyLabel: "Apply range",
            CustomRangeApplyHelpText: PresentationShellTextCatalog.PrintCustomRangeApplyHelp,
            CustomRangeInputAutomationId: "FreePPrintCustomRangeInput",
            CustomRangeApplyAutomationId: "FreePPrintCustomRangeApply",
            CustomRangeText: plan.SelectedRange.Request?.CustomRangeText ?? string.Empty,
            StatusText: plan.DisabledReason ?? plan.NativePrintHandoff.Reason,
            NativePrint: plan.NativePrintHandoff.Surface,
            PrintHeading: "Print",
            PrintActions: plan.LayoutChoices.Select(choice => BuildPrintAction(choice, plan)).ToArray());
    }

    public static string? NormalizeCustomRangeText(string? rangeText)
        => PresentationBackstagePrintRequestPlanner.NormalizeCustomRangeText(rangeText);

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
        new("Pages", plan.PageCount.ToString(CultureInfo.InvariantCulture)),
        new("Preview", plan.PreviewPlan.PageCountText),
        new("Hidden slides", plan.PrintHiddenSlides ? "Included" : "Not included"),
        new("Options", plan.Options.DisplaySummary),
        new("Native printer handoff", plan.NativePrintHandoff.StatusText),
    ];

    private static IReadOnlyList<PresentationBackstagePrintChoiceGroup> BuildChoiceGroups(
        PresentationPrintBackstagePlan plan,
        int? selectedPreviewPageNumber) =>
    [
        new("output-options", PresentationBackstagePrintChoiceGroupKind.OutputOptions, "Output options",
            plan.OutputOptionChoices.Select(choice => BuildChoiceRow(
            $"{choice.Group}: {choice.DisplayName}",
            choice.Description,
            choice.IsSelected,
            choice.IsAvailable)).ToArray()),
        new("preview", PresentationBackstagePrintChoiceGroupKind.Preview, "Preview",
            plan.PreviewPlan.Pages.Select(page => BuildChoiceRow(
            page.ThumbnailLabel,
            page.Detail,
            page.PageNumber == (selectedPreviewPageNumber ?? 1),
            isAvailable: true)).ToArray()),
        new("layouts", PresentationBackstagePrintChoiceGroupKind.Layouts, "Layouts",
            plan.LayoutChoices.Select(choice => BuildChoiceRow(
            choice.Layout.DisplayName,
            choice.PackagePlan.LayoutSummary,
            choice.IsSelected,
            isAvailable: true)).ToArray()),
        new("slide-range", PresentationBackstagePrintChoiceGroupKind.SlideRange, "Slide range",
            plan.RangeChoices.Select(choice => BuildChoiceRow(
            choice.DisplayName,
            choice.Description,
            choice.Kind == plan.SelectedRange.Kind,
            choice.IsAvailable)).ToArray()),
    ];

    private static PresentationBackstagePrintChoiceRow BuildChoiceRow(
        string label,
        string description,
        bool isSelected,
        bool isAvailable)
    {
        var prefix = isSelected ? "Selected: " : string.Empty;
        var availability = isAvailable ? string.Empty : " (unavailable)";
        return new PresentationBackstagePrintChoiceRow(
            label,
            description,
            isSelected,
            isAvailable,
            $"{prefix}{label}{availability}\n{description}");
    }

    private static PresentationBackstagePrintAction BuildPrintAction(
        PresentationPrintBackstageLayoutChoice choice,
        PresentationPrintBackstagePlan plan)
    {
        var request = PresentationBackstagePrintRequestPlanner.BuildRequest(plan, choice);
        var canPrint = choice.PackagePlan.CanBuildPackage &&
            (plan.NativePrintHandoff.CanOpenNativePrintDialog ||
             plan.NativePrintHandoff.CanSubmitToNativePrinter);

        return new PresentationBackstagePrintAction(
            $"Print {choice.Layout.DisplayName}",
            "BackstagePrint_" + AutomationIdToken.KeepLettersAndDigits(choice.Layout.DisplayName),
            canPrint ? choice.PackagePlan.LayoutSummary : plan.NativePrintHandoff.Reason,
            canPrint,
            request);
    }

}
