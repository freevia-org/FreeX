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
}
