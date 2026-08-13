namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidenceOwnershipTests
{
    private static readonly string[] SupportOnlyFiles =
    [
        "FreeWVisualEvidenceDocumentFactory.cs",
        "VisualEvidencePlanner.cs",
        "VisualEvidenceManifestNormalizer.cs",
        "VisualEvidenceBaselineComparison.cs",
        "VisualEvidenceWordBaselinePlanner.cs",
        "WordBaselineRasterSurfacePlanner.cs"
    ];

    [Fact]
    public void Visual_evidence_infrastructure_is_owned_by_test_support()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var productionDirectory = Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "DocumentView");
        var supportDirectory = Path.Combine(
            root,
            "freew",
            "tests",
            "FreeW.VisualEvidence.TestSupport");

        foreach (var fileName in SupportOnlyFiles)
        {
            File.Exists(Path.Combine(productionDirectory, fileName)).Should().BeFalse();
            File.Exists(Path.Combine(supportDirectory, fileName)).Should().BeTrue();
        }
    }

    [Fact]
    public void Production_backstage_owns_only_the_requirement_catalog()
    {
        var planner = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "freew",
            "FreeW.App.Presentation",
            "Backstage",
            "BackstagePrintPanePlanner.cs"));
        var catalog = File.ReadAllText(TestWorkspaceFileLocator.Find(
            "freew",
            "FreeW.App.Presentation",
            "Backstage",
            "BackstagePrintEvidenceRequirementCatalog.cs"));

        planner.Should().Contain("BackstagePrintEvidenceRequirementCatalog.Build(kind)");
        planner.Should().NotContain("FreeWVisualEvidenceManifestNormalizer");
        planner.Should().NotContain("FreeWVisualEvidenceNormalizedSummary");
        catalog.Should().Contain("wpf-fidelity-render");
        catalog.Should().Contain("avalonia-page-layout-shot");
    }

    [Fact]
    public void Evidence_consumers_reference_one_compiled_support_project()
    {
        var consumers = new[]
        {
            new[] { "freew", "FreeW.App.Presentation.Tests", "FreeW.App.Presentation.Tests.csproj" },
            new[] { "freew", "FreeW.App.Host.Tests", "FreeW.App.Host.Tests.csproj" },
            new[] { "freew", "FreeW.App.Avalonia.Tests", "FreeW.App.Avalonia.Tests.csproj" },
            new[] { "freew", "tools", "FreeW.FidelityRender", "FreeW.FidelityRender.csproj" },
            new[] { "freew", "tools", "FreeW.PageLayoutShot", "FreeW.PageLayoutShot.csproj" },
            new[] { "freew", "tools", "FreeW.VisualEvidenceSummary", "FreeW.VisualEvidenceSummary.csproj" }
        };

        foreach (var parts in consumers)
        {
            var project = File.ReadAllText(TestWorkspaceFileLocator.Find(parts));
            project.Should().Contain("FreeW.VisualEvidence.TestSupport.csproj");
            foreach (var fileName in SupportOnlyFiles)
                project.Should().NotContain(fileName);
        }
    }
}
