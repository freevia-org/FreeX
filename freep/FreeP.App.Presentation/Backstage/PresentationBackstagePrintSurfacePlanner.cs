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
            SettingsHeading: Resolve(PresentationShellTextCatalog.PrintSurfaceSettingsHeading),
            BuildSettings(plan),
            BuildChoiceGroups(plan, selectedPreviewPageNumber),
            CustomRangeHeading: Resolve(PresentationShellTextCatalog.PrintSurfaceCustomRangeHeading),
            CustomRangeDescription: Resolve(PresentationShellTextCatalog.PrintSurfaceCustomRangeDescription),
            CustomRangePlaceholder: Resolve(PresentationShellTextCatalog.PrintSurfaceCustomRangePlaceholder),
            CustomRangeApplyLabel: Resolve(PresentationShellTextCatalog.PrintSurfaceCustomRangeApplyLabel),
            CustomRangeApplyHelpText: PresentationShellTextCatalog.PrintCustomRangeApplyHelp,
            CustomRangeInputAutomationId: "FreePPrintCustomRangeInput",
            CustomRangeApplyAutomationId: "FreePPrintCustomRangeApply",
            CustomRangeText: plan.SelectedRange.Request?.CustomRangeText ?? string.Empty,
            StatusText: plan.DisabledReason ?? plan.NativePrintHandoff.Reason,
            NativePrint: plan.NativePrintHandoff.Surface,
            PrintHeading: Resolve(PresentationShellTextCatalog.PrintSurfacePrintHeading),
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
        new(Resolve(PresentationShellTextCatalog.PrintSurfaceLayoutField), plan.SelectedLayout.Layout.DisplayName),
        new(Resolve(PresentationShellTextCatalog.PrintSurfaceSlidesField), plan.SlideRangeSummary),
        new(Resolve(PresentationShellTextCatalog.PrintSurfacePagesField), plan.PageCount.ToString(CultureInfo.InvariantCulture)),
        new(Resolve(PresentationShellTextCatalog.PrintSurfacePreviewField), plan.PreviewPlan.PageCountText),
        new(Resolve(PresentationShellTextCatalog.PrintSurfaceHiddenSlidesField), plan.PrintHiddenSlides
            ? Resolve(PresentationShellTextCatalog.PrintSurfaceIncludedValue)
            : Resolve(PresentationShellTextCatalog.PrintSurfaceNotIncludedValue)),
        new(Resolve(PresentationShellTextCatalog.PrintSurfaceOptionsField), plan.Options.DisplaySummary),
        new(Resolve(PresentationShellTextCatalog.PrintSurfaceNativePrinterHandoffField), plan.NativePrintHandoff.StatusText),
    ];

    private static IReadOnlyList<PresentationBackstagePrintChoiceGroup> BuildChoiceGroups(
        PresentationPrintBackstagePlan plan,
        int? selectedPreviewPageNumber) =>
    [
        new("output-options", PresentationBackstagePrintChoiceGroupKind.OutputOptions,
            Resolve(PresentationShellTextCatalog.PrintSurfaceOutputOptionsGroup),
            plan.OutputOptionChoices.Select(choice => BuildChoiceRow(
            Resolve(PresentationShellTextCatalog.PrintSurfaceGroupChoice(choice.Group, choice.DisplayName)),
            choice.Description,
            choice.IsSelected,
            choice.IsAvailable)).ToArray()),
        new("preview", PresentationBackstagePrintChoiceGroupKind.Preview,
            Resolve(PresentationShellTextCatalog.PrintSurfacePreviewGroup),
            plan.PreviewPlan.Pages.Select(page => BuildChoiceRow(
            page.ThumbnailLabel,
            page.Detail,
            page.PageNumber == (selectedPreviewPageNumber ?? 1),
            isAvailable: true)).ToArray()),
        new("layouts", PresentationBackstagePrintChoiceGroupKind.Layouts,
            Resolve(PresentationShellTextCatalog.PrintSurfaceLayoutsGroup),
            plan.LayoutChoices.Select(choice => BuildChoiceRow(
            choice.Layout.DisplayName,
            choice.PackagePlan.LayoutSummary,
            choice.IsSelected,
            isAvailable: true)).ToArray()),
        new("slide-range", PresentationBackstagePrintChoiceGroupKind.SlideRange,
            Resolve(PresentationShellTextCatalog.PrintSurfaceSlideRangeGroup),
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
        var displayLabel = isSelected
            ? Resolve(PresentationShellTextCatalog.PrintSurfaceSelectedChoice(label))
            : label;
        if (!isAvailable)
            displayLabel = Resolve(PresentationShellTextCatalog.PrintSurfaceUnavailableChoice(displayLabel));
        return new PresentationBackstagePrintChoiceRow(
            label,
            description,
            isSelected,
            isAvailable,
            $"{displayLabel}\n{description}");
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
            Resolve(PresentationShellTextCatalog.PrintSurfaceAction(choice.Layout.DisplayName)),
            "BackstagePrint_" + AutomationIdToken.KeepLettersAndDigits(choice.Layout.DisplayName),
            canPrint ? choice.PackagePlan.LayoutSummary : plan.NativePrintHandoff.Reason,
            canPrint,
            request);
    }

    private static string Resolve(LocalizedTextDescriptor descriptor) =>
        PresentationShellTextCatalog.Resolve(descriptor);

}
