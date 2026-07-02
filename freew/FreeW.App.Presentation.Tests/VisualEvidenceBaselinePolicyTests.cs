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

        policy.IsComparable.Should().BeTrue();
        policy.BaselineScenarioId.Should().Be("f2-columns");
        FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)
            .Should().Be("f2-columns/p1/f2-columns_p1.png");
        candidates.Should().Contain([
            "f2-columns/f2-columns_p1.png",
            "f2-columns_p1.png"]);
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
    }

    private static FreeWVisualEvidenceNormalizedRow BuildRow(
        string scenarioId,
        string hostId,
        string outputName)
    {
        var scenario = FreeWVisualEvidencePlanner.ResolveScenario(scenarioId);
        var expectation = FreeWVisualEvidencePlanner.BuildPageExpectation(
            scenarioId,
            new PageSettings(),
            pageNumber: 1,
            pageCount: 1,
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
            PageNumber: 1,
            PageCount: 1,
            LayoutKind: expectation.LayoutKind,
            ExpectedOutputName: expectation.ExpectedOutputName,
            PageFeatures: expectation.Features,
            Tables: expectation.Tables,
            DrawingObjects: expectation.DrawingObjects,
            ChartSmartArt: expectation.ChartSmartArt,
            Trust: new FreeWVisualEvidenceTrust(true, []));
    }
}
