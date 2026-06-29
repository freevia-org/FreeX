using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFileDialogPlannerTests
{
    [Fact]
    public void DialogPlans_DefaultToPptxAndKeepLegacyFxpFilters()
    {
        PresentationFileDialogPlanner.LegacyFxpExtension.Should().Be(FxpFormat.Extension);

        var openPlan = PresentationFileDialogPlanner.BuildOpenDialogPlan();
        openPlan.Filter.Should().Be(
            "PowerPoint presentations (*.pptx)|*.pptx|FreeP legacy presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        openPlan.DefaultExtensionWithDot.Should().Be(".pptx");

        var savePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(null);
        savePlan.SuggestedFileName.Should().Be("Presentation.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FilterIndex.Should().Be(1);
        savePlan.Filter.Should().Be(openPlan.Filter);

        var legacySourcePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan("Legacy.fxp");
        legacySourcePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        legacySourcePlan.FilterIndex.Should().Be(1);
    }

    [Fact]
    public void PickerPlans_UseTheSamePresentationPolicyForAvaloniaAdapters()
    {
        var openPlan = PresentationFileDialogPlanner.BuildOpenPickerPlan();
        openPlan.FileTypes.Select(fileType => fileType.DisplayName)
            .Should()
            .Equal("All supported presentations", "PowerPoint presentations", "FreeP legacy presentations");
        openPlan.FileTypes[0].Patterns.Should().Equal("*.pptx", "*.fxp");
        openPlan.FileTypes[1].Patterns.Should().Equal("*.pptx");
        openPlan.FileTypes[2].Patterns.Should().Equal("*.fxp");

        var savePlan = PresentationFileDialogPlanner.BuildSavePickerPlan("Legacy.fxp");
        savePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FileTypes.Select(fileType => fileType.DisplayName)
            .Should()
            .Equal("PowerPoint presentations", "FreeP legacy presentations");
    }

    [Theory]
    [InlineData("deck.fxp", true)]
    [InlineData("deck.FXP", true)]
    [InlineData("deck.pptx", false)]
    public void IsLegacyPresentationPath_MatchesLegacyFxpExtensionCaseInsensitively(
        string path,
        bool expected) =>
        PresentationFileDialogPlanner.IsLegacyPresentationPath(path).Should().Be(expected);

    [Fact]
    public void PdfExportPlan_UsesSourceNameBaseAndPdfExtension()
    {
        var plan = PresentationFileDialogPlanner.BuildPdfExportDialogPlan("Quarterly Review.pptx");

        plan.Filter.Should().Be("PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*");
        plan.SuggestedFileName.Should().Be("Quarterly Review.pdf");
        plan.DefaultExtensionWithDot.Should().Be(".pdf");
        plan.DefaultExtensionWithoutDot.Should().Be("pdf");
        plan.FilterIndex.Should().Be(1);
    }
}
