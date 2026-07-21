using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutRibbonPolicyPlannerTests
{
    [Theory]
    [InlineData(PageLayoutMarginPreset.Normal, 1.0, 1.0)]
    [InlineData(PageLayoutMarginPreset.Wide, 1.25, 1.0)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.5, 0.5)]
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
    [InlineData(PageLayoutMarginPreset.Normal, 0.3)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.3)]
    [InlineData(PageLayoutMarginPreset.Wide, 0.5)]
    public void ResolveHeaderMargin_MatchesExcelsMarginsGalleryPresets(
        PageLayoutMarginPreset preset,
        double expectedHeaderMargin)
    {
        PageLayoutRibbonPolicyPlanner.ResolveHeaderMargin(preset).Should().Be(expectedHeaderMargin);
    }

    [Theory]
    [InlineData(PageLayoutMarginPreset.Normal, 0.3)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.3)]
    [InlineData(PageLayoutMarginPreset.Wide, 0.5)]
    public void ResolveFooterMargin_MatchesExcelsMarginsGalleryPresets(
        PageLayoutMarginPreset preset,
        double expectedFooterMargin)
    {
        PageLayoutRibbonPolicyPlanner.ResolveFooterMargin(preset).Should().Be(expectedFooterMargin);
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
    public void PlanScalePercentCommit_InvalidInputRequestsRevert()
    {
        var current = new WorksheetScaleToFit(125, null, null);

        var plan = PageLayoutRibbonPolicyPlanner.PlanScalePercentCommit(current, "900%");

        plan.ShouldApply.Should().BeFalse();
        plan.ScaleToFit.Should().Be(current);
    }
}
