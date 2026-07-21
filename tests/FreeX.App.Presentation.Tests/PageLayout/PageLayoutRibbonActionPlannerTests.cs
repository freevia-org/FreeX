using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutRibbonActionPlannerTests
{
    [Fact]
    public void RibbonActionDescriptors_HaveUniqueCommandIds()
    {
        PageLayoutRibbonActionPlanner.RibbonActionDescriptors
            .Select(descriptor => descriptor.CommandId)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public void RibbonActionDescriptors_RoutePageSetupButtonsThroughSharedOpenSources()
    {
        Descriptor("pageLayout.margins").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "pageLayout.margins",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins));
        Descriptor("pageLayout.printArea").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "pageLayout.printArea",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintArea));
        Descriptor("pageLayout.printTitles").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "pageLayout.printTitles",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintTitles));
        Descriptor("Scale to Fit").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "Scale to Fit",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit));
    }

    [Fact]
    public void RibbonActionDescriptors_OwnPresetAndPageBreakCommandPayloads()
    {
        Descriptor("Normal").MarginPreset.Should().Be(PageLayoutMarginPreset.Normal);
        Descriptor("Wide").MarginPreset.Should().Be(PageLayoutMarginPreset.Wide);
        Descriptor("Portrait").OrientationPreset.Should().Be(PageLayoutOrientationPreset.Portrait);
        Descriptor("Landscape").OrientationPreset.Should().Be(PageLayoutOrientationPreset.Landscape);
        Descriptor("Letter").PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.Letter);
        Descriptor("Legal").PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.Legal);
        Descriptor("A4").PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.A4);
        Descriptor("B4 (JIS)").PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.B4);
        Descriptor("B5 (JIS)").PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.B5);
        Descriptor("Insert Page Break").PageBreakAction.Should().Be(PageBreakMenuAction.Insert);
        Descriptor("Remove Page Break").PageBreakAction.Should().Be(PageBreakMenuAction.Remove);
        Descriptor("Reset All Page Breaks").PageBreakAction.Should().Be(PageBreakMenuAction.ResetAll);
    }

    [Fact]
    public void PresetCommandPlans_CentralizeValuesLabelsAndStatusKeys()
    {
        var margins = PageLayoutRibbonActionPlanner.PlanMarginsPreset(PageLayoutMarginPreset.Narrow);
        margins.Value.Should().Be(WorksheetPageMargins.Narrow);
        margins.CommandLabel.Should().Be(PageLayoutRibbonActionPlanner.PageMarginsCommandLabel);
        margins.StatusResourceKey.Should().Be("RibbonWire_MarginsNarrow");

        var orientation = PageLayoutRibbonActionPlanner.PlanOrientationPreset(PageLayoutOrientationPreset.Landscape);
        orientation.Value.Should().Be(WorksheetPageOrientation.Landscape);
        orientation.CommandLabel.Should().Be(PageLayoutRibbonActionPlanner.PageOrientationCommandLabel);
        orientation.StatusResourceKey.Should().Be("RibbonWire_OrientationLandscape");

        var paperSize = PageLayoutRibbonActionPlanner.PlanPaperSizePreset(PageLayoutPaperSizePreset.Legal);
        paperSize.Value.Should().Be(WorksheetPaperSize.Legal);
        paperSize.CommandLabel.Should().Be(PageLayoutRibbonActionPlanner.PaperSizeCommandLabel);
        paperSize.StatusResourceKey.Should().Be("RibbonWire_PaperLegal");

        var b4PaperSize = PageLayoutRibbonActionPlanner.PlanPaperSizePreset(PageLayoutPaperSizePreset.B4);
        b4PaperSize.Value.Should().Be(WorksheetPaperSize.B4);
        b4PaperSize.CommandLabel.Should().Be(PageLayoutRibbonActionPlanner.PaperSizeCommandLabel);
        b4PaperSize.StatusResourceKey.Should().Be("RibbonWire_PaperB4");
    }

    [Theory]
    [InlineData(PageLayoutMarginPreset.Normal, 0.3, 0.3)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.3, 0.3)]
    [InlineData(PageLayoutMarginPreset.Wide, 0.5, 0.5)]
    public void PlanMarginsPreset_CarriesHeaderAndFooterMarginForPreset(
        PageLayoutMarginPreset preset,
        double expectedHeaderMargin,
        double expectedFooterMargin)
    {
        var plan = PageLayoutRibbonActionPlanner.PlanMarginsPreset(preset);

        plan.HeaderMargin.Should().Be(expectedHeaderMargin);
        plan.FooterMargin.Should().Be(expectedFooterMargin);
    }

    [Fact]
    public void PlanOrientationPreset_LeavesHeaderAndFooterMarginUnset()
    {
        var orientation = PageLayoutRibbonActionPlanner.PlanOrientationPreset(PageLayoutOrientationPreset.Landscape);

        orientation.HeaderMargin.Should().BeNull();
        orientation.FooterMargin.Should().BeNull();
    }

    private static PageLayoutRibbonActionDescriptor Descriptor(string commandId) =>
        PageLayoutRibbonActionPlanner.RibbonActionDescriptors
            .Single(descriptor => descriptor.CommandId == commandId);
}
