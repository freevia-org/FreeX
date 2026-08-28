using System.Text.Json;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave197Deck17LeadingResidualEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave197-deck17-leading-residual-20260829";

    [Fact]
    public void LeadingCandidate_IsRefutedByStableBodyCadence()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave197.deck17-leading-residual.v1");
        root.GetProperty("status").GetString()
            .Should().Be("candidate-refuted");

        var candidate = root.GetProperty("candidate");
        candidate.GetProperty("outcome").GetString()
            .Should().Be("rejected-preserve-body-cadence");
        candidate.GetProperty("currentLineHeightDip").GetDouble()
            .Should().Be(28.8);
        candidate.GetProperty("candidateLineHeightDip").GetDouble()
            .Should().Be(26.784);
        candidate.GetProperty("candidateFinalBaselineDriftDip").GetDouble()
            .Should().Be(30.24);

        var geometry = root.GetProperty("observedGeometry");
        geometry.GetProperty("officeBodyBandStarts").GetArrayLength()
            .Should().Be(16);
        geometry.GetProperty("avaloniaBodyBandStarts").GetArrayLength()
            .Should().Be(16);
        geometry.GetProperty("maxBandStartDeltaPx").GetInt32().Should().Be(0);

        root.GetProperty("controls")
            .GetProperty("slide01BeforeAfterAvaloniaChangedPercent")
            .GetDouble().Should().Be(0.0);
    }

    private static string EvidenceFile(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }
}
