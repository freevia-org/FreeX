using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class WholeWindowVisualEvidenceContractTests
{
    [Fact]
    public void Catalog_defines_unique_complete_96_dpi_whole_window_matrix()
    {
        WholeWindowVisualEvidenceCatalog.All.Should().HaveCount(31);
        WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id).Should().OnlyHaveUniqueItems();
        WholeWindowVisualEvidenceCatalog.LogicalClientWidth.Should().Be(1280);
        WholeWindowVisualEvidenceCatalog.LogicalClientHeight.Should().Be(760);
        WholeWindowVisualEvidenceCatalog.TargetDpi.Should().Be(96);

        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.Startup).Should().Be(2);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.StaticRibbonTab).Should().Be(6);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane).Should().Be(7);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind is WholeWindowVisualEvidenceScenarioKind.StatusBar or WholeWindowVisualEvidenceScenarioKind.ViewState).Should().Be(5);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion).Should().Be(3);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane).Should().Be(8);
    }

    [Fact]
    public void Catalog_does_not_invent_contextual_tabs_absent_from_the_product_ribbon()
    {
        WholeWindowVisualEvidenceCatalog.All
            .Should().NotContain(scenario => !string.IsNullOrWhiteSpace(scenario.ExpectedContextualTabId));
        WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id)
            .Should().NotContain("status.slide-1");
    }
}
