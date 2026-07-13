using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class VisualEvidenceBaselinePolicyTests
{
    [Fact]
    public void WordBaselinePolicy_MapsAvaloniaColumnEvidenceToF2WordBaseline()
    {
        var row = BuildRow(
            "page-composition-columns",
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
            "freew_columns_layout.png");

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);
        var missing = FreeWVisualBaselineComparisonPlanner.BuildMissingBaselineComparison(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("f2-columns");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("f2-columns/p1/f2-columns_p1.png");
        candidates.Should().Contain([
            "f2-columns/f2-columns_p1.png",
            "f2-columns_p1.png"]);
        FreeWVisualBaselineComparisonPlanner.ClassifyBaselineEvidence(missing)
            .Should().Be(FreeWVisualBaselineComparisonPlanner.WordPngBaselineMissingClass);
        FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidence(missing)
            .Should().Contain("candidate paths are recorded");
    }

    [Fact]
    public void WordBaselinePolicy_SkipsUnmappedAvaloniaLayoutRowsWithoutFailingTrust()
    {
        var row = BuildRow(
            "page-composition-web-layout",
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
            "freew_web_layout.png");

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);
        var comparison = FreeWVisualBaselineComparisonPlanner.BuildSkippedBaselineComparison(row);

        policy.IsComparable.Should().BeFalse();
        policy.SkipReason.Should().Contain("no direct MS Word PNG baseline mapping");
        candidates.Should().BeEmpty();
        comparison.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.SkippedStatus);
        comparison.Trust.Passed.Should().BeTrue();
        comparison.BaselinePath.Should().BeEmpty();
        comparison.SkipReason.Should().Contain("no direct MS Word PNG baseline mapping");
        FreeWVisualBaselineComparisonPlanner.ClassifyBaselineEvidence(comparison)
            .Should().Be(FreeWVisualBaselineComparisonPlanner.ScenarioSkippedOrUnmappedClass);
        FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidence(comparison)
            .Should().Contain("unmapped");
    }

    [Fact]
    public void WordBaselinePolicy_KeepsSharedComplexObjectScenariosDirectlyComparable()
    {
        var row = BuildRow(
            "chart-smartart-complex",
            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
            "chart-smartart-complex_p1.png");

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("chart-smartart-complex");
        candidates.Should().Contain([
            "chart-smartart-complex/chart-smartart-complex_p1.png",
            "chart-smartart-complex_p1.png"]);
    }

    [Fact]
    public void WordBaselinePolicy_KeepsTablePaginationRepeatHeaderDirectlyComparable()
    {
        var row = BuildRow(
            "table-pagination-repeat-header",
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
            "table-pagination-repeat-header_p2.png",
            pageNumber: 2,
            pageCount: 2);

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("table-pagination-repeat-header");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("table-pagination-repeat-header/p2/table-pagination-repeat-header_p2.png");
        candidates.Should().Contain([
            "table-pagination-repeat-header/table-pagination-repeat-header_p2.png",
            "table-pagination-repeat-header_p2.png"]);
    }

    [Fact]
    public void WordBaselinePolicy_KeepsTablePageCompositionStressDirectlyComparable()
    {
        var row = BuildRow(
            "table-page-composition-stress",
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
            "table-page-composition-stress_p2.png",
            pageNumber: 2,
            pageCount: 2);

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("table-page-composition-stress");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("table-page-composition-stress/p2/table-page-composition-stress_p2.png");
        candidates.Should().Contain([
            "table-page-composition-stress/table-page-composition-stress_p2.png",
            "table-page-composition-stress_p2.png"]);
    }

    [Fact]
    public void WordBaselinePolicy_KeepsReferencesHeavyFieldsDirectlyComparable()
    {
        var row = BuildRow(
            "references-heavy-fields",
            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
            "references-heavy-fields_p1.png",
            pageNumber: 1,
            pageCount: 2);

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("references-heavy-fields");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("references-heavy-fields/p1/references-heavy-fields_p1.png");
        candidates.Should().Contain([
            "references-heavy-fields/references-heavy-fields_p1.png",
            "references-heavy-fields_p1.png"]);
    }

    [Fact]
    public void WordBaselinePolicy_KeepsEquationStructuresDirectlyComparable()
    {
        var row = BuildRow(
            "equation-structures",
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
            "equation-structures_p1.png",
            pageNumber: 1,
            pageCount: 1);

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var candidates = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("equation-structures");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("equation-structures/p1/equation-structures_p1.png");
        candidates.Should().Contain([
            "equation-structures/equation-structures_p1.png",
            "equation-structures_p1.png"]);
    }

    [Fact]
    public void WordBaselineUnavailableComparison_ReportsCandidatesAndReasonWithoutFailingTrust()
    {
        var row = BuildRow(
            "f2-hf-basic",
            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
            "f2-hf-basic_p1.png");

        var comparison = FreeWVisualBaselineComparisonPlanner.BuildWordBaselineUnavailableComparison(
            row,
            FreeWVisualBaselineComparisonTolerance.WordPngDefault,
            "COM ProgID 'Word.Application' is not registered");

        comparison.Status.Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus);
        comparison.Trust.Passed.Should().BeTrue();
        comparison.BaselineScenarioId.Should().Be("f2-hf-basic");
        comparison.BaselineId.Should().Be("f2-hf-basic/p1/f2-hf-basic_p1.png");
        comparison.BaselinePath.Should().BeEmpty();
        comparison.CandidateBaselinePaths.Should().Contain([
            "f2-hf-basic/f2-hf-basic_p1.png",
            "f2-hf-basic_p1.png"]);
        comparison.SkipReason.Should().Contain("Word.Application");
        FreeWVisualBaselineComparisonPlanner.ClassifyBaselineEvidence(comparison)
            .Should().Be(FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableClass);
        FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidence(comparison)
            .Should().Contain("no authoritative Word PNG parity claimed");
    }

    private static FreeWVisualEvidenceNormalizedRow BuildRow(
        string scenarioId,
        string hostId,
        string outputName,
        int pageNumber = 1,
        int pageCount = 1)
    {
        var scenario = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId);
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            scenarioId,
            new PageSettings(),
            pageNumber: pageNumber,
            pageCount: pageCount,
            outputName: outputName);

        return new FreeWVisualEvidenceNormalizedRow(
            EvidenceId: $"{hostId}/{scenarioId}/{outputName}",
            SourceManifestPath: "freew_visual_evidence_manifest.json",
            ScenarioId: scenarioId,
            HostId: hostId,
            ExpectedFeatureTags: scenario.ExpectedFeatureTags,
            OutputName: outputName,
            OutputPath: $"{hostId}/{outputName}",
            PixelWidth: 20,
            PixelHeight: 20,
            ByteLength: 1024,
            Sha256: new string('0', 64),
            PixelStats: new FreeWVisualPixelStats(
                Width: 20,
                Height: 20,
                SampledPixels: 400,
                DistinctSampledColors: 4,
                DominantColorHex: "#FFFFFF",
                DominantColorRatio: 0.5,
                BackgroundColorHex: "#FFFFFF",
                NonBackgroundSampledPixels: 200,
                NonBackgroundRatio: 0.5),
            HostMetadata: new Dictionary<string, string>(),
            PageNumber: pageNumber,
            PageCount: pageCount,
            LayoutKind: expectation.LayoutKind,
            ExpectedOutputName: expectation.ExpectedOutputName,
            PageFeatures: expectation.Features,
            Tables: expectation.Tables,
            DrawingObjects: expectation.DrawingObjects,
            ChartSmartArt: expectation.ChartSmartArt,
            Fields: expectation.Fields,
            Equations: expectation.Equations,
            HeaderFooters: expectation.HeaderFooters,
            TableOfAuthorities: expectation.TableOfAuthorities,
            ProofingDiagnostics: expectation.ProofingDiagnostics,
            ReviewProtection: expectation.ReviewProtection,
            Trust: new FreeWVisualEvidenceTrust(true, []));
    }
}
