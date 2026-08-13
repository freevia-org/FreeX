using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

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
        Descriptor("Margins").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "Margins",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins));
        Descriptor("Print Area").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "Print Area",
            PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintArea));
        Descriptor("Print Titles").Should().BeEquivalentTo(new PageLayoutRibbonActionDescriptor(
            "Print Titles",
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
        Descriptor(FreeXRibbonCommandIds.PageLayoutMarginsNormal).MarginPreset.Should().Be(PageLayoutMarginPreset.Normal);
        Descriptor(FreeXRibbonCommandIds.PageLayoutMarginsWide).MarginPreset.Should().Be(PageLayoutMarginPreset.Wide);
        Descriptor(FreeXRibbonCommandIds.PageLayoutOrientationPortrait).OrientationPreset.Should().Be(PageLayoutOrientationPreset.Portrait);
        Descriptor(FreeXRibbonCommandIds.PageLayoutOrientationLandscape).OrientationPreset.Should().Be(PageLayoutOrientationPreset.Landscape);
        Descriptor(FreeXRibbonCommandIds.PageLayoutPaperSizeLetter).PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.Letter);
        Descriptor(FreeXRibbonCommandIds.PageLayoutPaperSizeLegal).PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.Legal);
        Descriptor(FreeXRibbonCommandIds.PageLayoutPaperSizeA4).PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.A4);
        Descriptor(FreeXRibbonCommandIds.PageLayoutPaperSizeB4Jis).PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.B4);
        Descriptor(FreeXRibbonCommandIds.PageLayoutPaperSizeB5Jis).PaperSizePreset.Should().Be(PageLayoutPaperSizePreset.B5);
        Descriptor(FreeXRibbonCommandIds.PageLayoutBreakInsert).PageBreakAction.Should().Be(PageBreakMenuAction.Insert);
        Descriptor(FreeXRibbonCommandIds.PageLayoutBreakRemove).PageBreakAction.Should().Be(PageBreakMenuAction.Remove);
        Descriptor(FreeXRibbonCommandIds.PageLayoutBreakResetAll).PageBreakAction.Should().Be(PageBreakMenuAction.ResetAll);
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
    [InlineData(PageLayoutMarginPreset.Wide, 0.5, 0.5)]
    [InlineData(PageLayoutMarginPreset.Narrow, 0.3, 0.3)]
    public void PlanMarginsPreset_CarriesExcelHeaderFooterDistanceForPreset(
        PageLayoutMarginPreset preset,
        double expectedHeader,
        double expectedFooter)
    {
        var plan = PageLayoutRibbonActionPlanner.PlanMarginsPreset(preset);

        plan.HeaderMargin.Should().Be(expectedHeader);
        plan.FooterMargin.Should().Be(expectedFooter);
    }

    private static PageLayoutRibbonActionDescriptor Descriptor(string commandId) =>
        PageLayoutRibbonActionPlanner.RibbonActionDescriptors
            .Single(descriptor => descriptor.CommandId == commandId);
}
