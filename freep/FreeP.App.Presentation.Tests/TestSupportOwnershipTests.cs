using FreeP.VisualEvidence;

namespace FreeP.App.Compositor.Tests;

public sealed class TestSupportOwnershipTests
{
    [Fact]
    public void ProductionPresentationAssemblyDoesNotOwnTestEvidencePlanners()
    {
        var productionAssembly = typeof(EditingSession).Assembly;

        productionAssembly.GetType("FreeP.App.Compositor.PresentationPdfVisualBaselineReadinessPlanner")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.SlideShowRecordingHostAdapterParityPlanner")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.ChartOptionsDialogTestPlanCatalog")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.ChartSeriesOptionsDialogTestSettings")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.PresentationExportBackstageEvidencePlanner")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.ChartVisualBaselineReadinessPlanner")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.AnimationPaneVisualBaselinePlanner")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.DialogPaneVisualEvidenceCatalog")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.DialogPaneVisualEvidencePreparationSession")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.DialogPaneVisualEvidenceFixtureFactory")
            .Should().BeNull();
        productionAssembly.GetType("FreeP.App.Compositor.WholeWindowVisualEvidencePreparationSession")
            .Should().BeNull();
    }

    [Fact]
    public void TestAssemblyOwnsTestEvidencePlanners()
    {
        ReferenceEquals(
            typeof(PresentationPdfVisualBaselineReadinessPlanner).Assembly,
            typeof(TestSupportOwnershipTests).Assembly).Should().BeTrue();
        ReferenceEquals(
            typeof(SlideShowRecordingHostAdapterParityPlanner).Assembly,
            typeof(TestSupportOwnershipTests).Assembly).Should().BeTrue();
        ReferenceEquals(
            typeof(ChartOptionsDialogTestPlanCatalog).Assembly,
            typeof(TestSupportOwnershipTests).Assembly).Should().BeTrue();
        ReferenceEquals(
            typeof(ChartSeriesOptionsDialogTestSettings).Assembly,
            typeof(TestSupportOwnershipTests).Assembly).Should().BeTrue();
    }

    [Fact]
    public void VisualEvidenceSupportAssemblyOwnsPortableEvidenceInfrastructure()
    {
        var supportAssembly = typeof(PresentationExportBackstageEvidencePlanner).Assembly;

        ReferenceEquals(supportAssembly, typeof(ChartVisualBaselineReadinessPlanner).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(AnimationPaneVisualBaselinePlanner).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(DialogPaneVisualEvidenceCatalog).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(DialogPaneVisualEvidencePreparationSession).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(DialogPaneVisualEvidenceFixtureFactory).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(WholeWindowVisualEvidencePreparationSession).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(IWholeWindowVisualEvidenceProbe).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(WholeWindowVisualEvidenceHostCoordinator).Assembly)
            .Should().BeTrue();
        ReferenceEquals(supportAssembly, typeof(EditingSession).Assembly)
            .Should().BeFalse();
    }
}
