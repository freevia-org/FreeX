using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutRibbonPolicyPlannerTests
{
    [Theory]
    [InlineData(PageLayoutMarginPreset.Normal, 0.7, 0.75)]
    [InlineData(PageLayoutMarginPreset.Wide, 1.25, 1.0)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.25, 0.75)]
    public void ResolveMargins_MapsRibbonPresetToWorksheetMargins(
        PageLayoutMarginPreset preset,
        double expectedLeft,
        double expectedTop)
    {
        var margins = PageLayoutRibbonPolicyPlanner.ResolveMargins(preset);

        margins.Left.Should().Be(expectedLeft);
        margins.Top.Should().Be(expectedTop);
    }

    [Theory]
    [InlineData(PageLayoutMarginPreset.Normal, 0.3, 0.3)]
    [InlineData(PageLayoutMarginPreset.Wide, 0.5, 0.5)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.3, 0.3)]
    public void ResolveHeaderFooterMargins_MapsRibbonPresetToExcelHeaderFooterDistance(
        PageLayoutMarginPreset preset,
        double expectedHeader,
        double expectedFooter)
    {
        var (header, footer) = PageLayoutRibbonPolicyPlanner.ResolveHeaderFooterMargins(preset);

        header.Should().Be(expectedHeader);
        footer.Should().Be(expectedFooter);
    }

    [Theory]
    [InlineData(PageLayoutOrientationPreset.Portrait, WorksheetPageOrientation.Portrait)]
    [InlineData(PageLayoutOrientationPreset.Landscape, WorksheetPageOrientation.Landscape)]
    public void ResolveOrientation_MapsRibbonPresetToWorksheetOrientation(
        PageLayoutOrientationPreset preset,
        WorksheetPageOrientation expected)
    {
        PageLayoutRibbonPolicyPlanner.ResolveOrientation(preset).Should().Be(expected);
    }

    [Theory]
    [InlineData(PageLayoutPaperSizePreset.Letter, WorksheetPaperSize.Letter)]
    [InlineData(PageLayoutPaperSizePreset.A4, WorksheetPaperSize.A4)]
    [InlineData(PageLayoutPaperSizePreset.Legal, WorksheetPaperSize.Legal)]
    [InlineData(PageLayoutPaperSizePreset.B4, WorksheetPaperSize.B4)]
    [InlineData(PageLayoutPaperSizePreset.B5, WorksheetPaperSize.B5)]
    public void ResolvePaperSize_MapsRibbonPresetToWorksheetPaperSize(
        PageLayoutPaperSizePreset preset,
        WorksheetPaperSize expected)
    {
        PageLayoutRibbonPolicyPlanner.ResolvePaperSize(preset).Should().Be(expected);
    }

    [Theory]
    [InlineData(PageLayoutPageSetupOpenSource.DialogButton, PageSetupInitialFocusTarget.PageOrientation)]
    [InlineData(PageLayoutPageSetupOpenSource.CustomMargins, PageSetupInitialFocusTarget.Margins)]
    [InlineData(PageLayoutPageSetupOpenSource.ExtendedPaperSize, PageSetupInitialFocusTarget.PaperSize)]
    [InlineData(PageLayoutPageSetupOpenSource.ScaleToFit, PageSetupInitialFocusTarget.ScaleToFit)]
    [InlineData(PageLayoutPageSetupOpenSource.PrintArea, PageSetupInitialFocusTarget.PrintArea)]
    [InlineData(PageLayoutPageSetupOpenSource.PrintTitles, PageSetupInitialFocusTarget.RepeatRows)]
    public void ResolvePageSetupInitialFocus_MapsRibbonSourceToDialogFocus(
        PageLayoutPageSetupOpenSource source,
        PageSetupInitialFocusTarget expected)
    {
        PageLayoutRibbonPolicyPlanner.ResolvePageSetupInitialFocus(source).Should().Be(expected);
    }

    [Fact]
    public void PlanScaleWidthCommit_AppliesParsedWidthAndPreservesCurrentHeight()
    {
        var current = new WorksheetScaleToFit(null, 1, 3);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScaleWidthCommit(current, "2 pages");

        plan.ShouldApply.Should().BeTrue();
        plan.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 2, 3));
    }

    [Fact]
    public void PlanScalePercentCommit_AutomaticLeavesFitToPagesAlone()
    {
        // "Automatic" is what the Percent combo displays while fit-to-pages drives the scaling,
        // so committing it is the combo echoing its own state, not a request to leave that mode.
        // Applying it resolves to (100, null, null) and would wipe the fit-to-pages the user just
        // set -- which is exactly how a just-applied "1 page" reverted to Automatic.
        var current = new WorksheetScaleToFit(100, 1, null);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(current, "Automatic");

        plan.ShouldApply.Should().BeFalse();
        plan.ScaleToFit.Should().Be(current);
    }

    [Fact]
    public void PlanScalePercentCommit_AutomaticStillAppliesWhenNoFitToPagesIsSet()
    {
        // With no fit-to-pages in play, Automatic genuinely means "back to 100%".
        var current = new WorksheetScaleToFit(125, null, null);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(current, "Automatic");

        plan.ShouldApply.Should().BeTrue();
        plan.ScaleToFit.Should().Be(new WorksheetScaleToFit(100, null, null));
    }

    [Fact]
    public void PlanScalePercentCommit_InvalidInputRequestsRevert()
    {
        var current = new WorksheetScaleToFit(125, null, null);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(current, "900%");

        plan.ShouldApply.Should().BeFalse();
        plan.ScaleToFit.Should().Be(current);
    }

    [Theory]
    [InlineData(PageLayoutScaleField.Width, "2 pages", null, 2, 3)]
    [InlineData(PageLayoutScaleField.Height, "4 pages", null, 1, 4)]
    [InlineData(PageLayoutScaleField.Percent, "125%", 125, null, null)]
    public void PlanScaleCommit_RoutesFieldThroughPortablePolicy(
        PageLayoutScaleField field,
        string text,
        int? expectedPercent,
        int? expectedWide,
        int? expectedTall)
    {
        var current = new WorksheetScaleToFit(null, 1, 3);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScaleCommit(field, current, text);

        plan.ShouldApply.Should().BeTrue();
        plan.ScaleToFit.Should().Be(new WorksheetScaleToFit(expectedPercent, expectedWide, expectedTall));
    }
}
