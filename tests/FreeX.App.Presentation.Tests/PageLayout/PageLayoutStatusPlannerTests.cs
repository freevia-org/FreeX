using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutStatusPlannerTests
{
    [Fact]
    public void ForPreset_UsesPresetStatusKeyForSuccessAndFailureFallback()
    {
        var preset = PageLayoutRibbonActionPlanner.PlanMarginsPreset(PageLayoutMarginPreset.Wide);
        var plan = PageLayoutStatusPlanner.ForPreset(preset);

        PageLayoutStatusPlanner.ResolveCommandStatus(plan, success: true, errorMessage: null, Resolve)
            .Should().Be("resolved:RibbonWire_MarginsWide");
        PageLayoutStatusPlanner.ResolveCommandStatus(plan, success: false, errorMessage: null, Resolve)
            .Should().Be("resolved:RibbonWire_MarginsWide");
    }

    [Fact]
    public void ResolveCommandStatus_PrefersFailureErrorMessageOverFallbackResource()
    {
        var status = PageLayoutStatusPlanner.ResolveCommandStatus(
            PageLayoutStatusPlanner.PrintAreaSet,
            success: false,
            errorMessage: "Selection is unavailable.",
            Resolve);

        status.Should().Be("Selection is unavailable.");
    }

    [Fact]
    public void PrintAreaPlans_ExposeSharedSuccessAndFailureResourceKeys()
    {
        PageLayoutStatusPlanner.PrintAreaSet.Should().Be(new PageLayoutCommandStatusPlan(
            "RibbonWire_PrintAreaSet",
            "RibbonWire_PrintAreaSetFailed"));
        PageLayoutStatusPlanner.PrintAreaClear.Should().Be(new PageLayoutCommandStatusPlan(
            "RibbonWire_PrintAreaCleared",
            "RibbonWire_PrintAreaClearFailed"));
    }

    [Theory]
    [InlineData(WorksheetViewMode.Normal, WorksheetViewMode.PageBreakPreview, "ShellLoc_PageBreakPreviewOn")]
    [InlineData(WorksheetViewMode.PageLayout, WorksheetViewMode.PageBreakPreview, "ShellLoc_PageBreakPreviewOn")]
    [InlineData(WorksheetViewMode.PageBreakPreview, WorksheetViewMode.Normal, "ShellLoc_PageBreakPreviewOff")]
    public void PlanPageBreakPreviewToggle_MapsCurrentModeToTargetAndStatus(
        WorksheetViewMode currentViewMode,
        WorksheetViewMode expectedTargetViewMode,
        string expectedSuccessResourceKey)
    {
        var plan = PageLayoutStatusPlanner.PlanPageBreakPreviewToggle(currentViewMode);

        plan.TargetViewMode.Should().Be(expectedTargetViewMode);
        plan.Status.SuccessResourceKey.Should().Be(expectedSuccessResourceKey);
        plan.Status.FailureResourceKey.Should().Be("ShellLoc_PageBreakPreviewOff");
    }

    [Fact]
    public void ResolvePageSetupValidationIssue_UsesSharedInvalidFallbackResource()
    {
        var validation = new PageSetupSubmissionValidation(
            null,
            new PageSetupValidationRoute(PageSetupDialogTab.Page, PageSetupDialogField.Orientation),
            new PageSetupValidationMessage(null, null));

        PageLayoutStatusPlanner.ResolvePageSetupValidationIssue(validation, Resolve)
            .Should().Be("resolved:ShellLoc_PageSetupInvalid");
    }

    private static string Resolve(string key) => $"resolved:{key}";
}
