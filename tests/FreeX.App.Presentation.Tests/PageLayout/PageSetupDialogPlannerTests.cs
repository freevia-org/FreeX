using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageSetupDialogPlannerTests
{
    [Fact]
    public void ChoicePlans_WrapSharedCatalogsWithFallbacks()
    {
        PageSetupDialogPlanner.OrientationChoices.Choices
            .Should()
            .Equal(PageSetupDialogModel.OrientationChoices);
        PageSetupDialogPlanner.PaperSizeChoices.Choices
            .Should()
            .Equal(PageSetupDialogModel.PaperSizeChoices);

        PageSetupDialogPlanner.PaperSizeChoices.IndexOf(WorksheetPaperSize.B5).Should().Be(7);
        PageSetupDialogPlanner.PaperSizeChoices.ValueAt(99).Should().Be(WorksheetPaperSize.A4);
        PageSetupDialogPlanner.PrintErrorValueChoices.ValueAt(2).Should().Be(WorksheetPrintErrorValue.Dash);
    }

    [Fact]
    public void ResolveChoiceLabels_UsesCatalogResourceKeysInOrder()
    {
        PageSetupDialogPlanner.ResolveChoiceLabels(PageSetupDialogPlanner.OrientationChoices, key => $"loc:{key}")
            .Should()
            .Equal("loc:PageSetup_Portrait", "loc:PageSetup_Landscape");
    }

    [Fact]
    public void SurfaceMetadata_CentralizesSharedDialogContract()
    {
        PageSetupDialogPlanner.DialogAutomationId.Should().Be("PageSetupDialog");
        PageSetupDialogPlanner.TabsAutomationId.Should().Be("PageSetupTabs");
        PageSetupDialogPlanner.WindowWidth.Should().Be(600);
        PageSetupDialogPlanner.WindowHeight.Should().Be(560);
        PageSetupDialogPlanner.FieldMinWidth.Should().Be(220);
        PageSetupDialogPlanner.PrintPreviewButtonAutomationId.Should().Be("PageSetupPrintPreviewButton");
    }
}
