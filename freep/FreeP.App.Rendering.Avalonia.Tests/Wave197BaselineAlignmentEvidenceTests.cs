using System.Text.Json;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave197BaselineAlignmentEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829";

    [Fact]
    public void BaselineAlignmentCandidate_IsRefutedByTargetAndControlMetrics()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave197.deck17-baseline-alignment.v1");
        root.GetProperty("status").GetString().Should().Be("candidate-refuted");

        var candidate = root.GetProperty("candidate");
        candidate.GetProperty("acceptedBaselinePixelAlignment").GetString()
            .Should().Be("Unaligned");
        candidate.GetProperty("candidateBaselinePixelAlignment").GetString()
            .Should().Be("Aligned");
        candidate.GetProperty("outcome").GetString()
            .Should().Be("rejected-preserve-unaligned-raster");

        var control = root.GetProperty("measurements").GetProperty("slide01Control");
        control.GetProperty("candidateChangedPercent").GetDouble().Should().Be(0.0);
        control.GetProperty("candidateMaxChannel").GetInt32().Should().Be(0);
        control.GetProperty("candidateMatchesAcceptedPng").GetBoolean().Should().BeTrue();

        var target = root.GetProperty("measurements").GetProperty("slide02Target");
        target.GetProperty("candidateAvaloniaOffice").GetDouble().Should().Be(2.5116);
        target.GetProperty("deltaPercentagePoints").GetDouble().Should().Be(0.0296);
        target.GetProperty("candidateWpfAvalonia").GetDouble().Should().Be(2.9053);
        target.GetProperty("pairDeltaPercentagePoints").GetDouble().Should().Be(0.0298);
    }

    private static string EvidenceFile(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }
}
