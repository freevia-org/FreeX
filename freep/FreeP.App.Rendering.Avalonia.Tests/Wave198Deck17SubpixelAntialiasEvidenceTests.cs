using System.Security.Cryptography;
using System.Text.Json;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class Wave198Deck17SubpixelAntialiasEvidenceTests
{
    private const string EvidenceDirectory =
        "docs/parity/evidence/freep-wave198-deck17-subpixel-antialias-20260829";

    [Fact]
    public void SubpixelCandidate_IsRefutedByCrossRendererTargetMetric()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;

        root.GetProperty("schema").GetString()
            .Should().Be("freep.parity.wave198.deck17-subpixel-antialias.v1");
        root.GetProperty("status").GetString().Should().Be("candidate-refuted");

        var candidate = root.GetProperty("candidate");
        candidate.GetProperty("acceptedTextRenderingMode").GetString().Should().Be("Antialias");
        candidate.GetProperty("candidateTextRenderingMode").GetString().Should().Be("SubpixelAntialias");
        candidate.GetProperty("outcome").GetString()
            .Should().Be("rejected-preserve-grayscale-cross-renderer-parity");

        var provenance = root.GetProperty("sourceProvenance");
        provenance.GetProperty("generationLinkage").GetString()
            .Should().Be("not-independently-proven");
        provenance.GetProperty("generationLinkageBasis").GetString()
            .Should().Be(
                "The candidate change and render commands are described in the parity note, but the retained bundle has no exact candidate-source-byte or patch hash and no captured generation log that independently binds those inputs to these PNG bytes.");

        var control = root.GetProperty("measurements").GetProperty("slide01Control");
        control.GetProperty("candidateChangedPercent").GetDouble().Should().Be(0.0);
        control.GetProperty("candidateMaxChannel").GetInt32().Should().Be(0);
        control.GetProperty("candidateMatchesAcceptedPng").GetBoolean().Should().BeTrue();

        var target = root.GetProperty("measurements").GetProperty("slide02Target");
        target.GetProperty("candidateAvaloniaOffice").GetDouble().Should().Be(2.4583);
        target.GetProperty("avaloniaOfficeDeltaPercentagePoints").GetDouble().Should().Be(-0.0237);
        target.GetProperty("candidateWpfAvalonia").GetDouble().Should().Be(2.8847);
        target.GetProperty("wpfAvaloniaDeltaPercentagePoints").GetDouble().Should().Be(0.0092);
        target.GetProperty("candidateBeforeAfterAvalonia").GetDouble().Should().Be(0.5355);
    }

    [Fact]
    public void EvidenceImages_MatchRecordedHashes()
    {
        using var images = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));
        using var metrics = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var trackedImages = metrics.RootElement
            .GetProperty("imageIntegrity")
            .GetProperty("trackedImages");

        foreach (var image in images.RootElement.EnumerateObject())
        {
            var imagePath = EvidenceFile(image.Name);
            File.Exists(imagePath).Should().BeTrue($"the evidence image must exist: {image.Name}");

            using var stream = File.OpenRead(imagePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            actualHash.Should().Be(image.Value.GetString(), $"images.json must match {image.Name}");
            trackedImages.GetProperty(image.Name).GetString().Should().Be(actualHash);
        }
    }

    private static string EvidenceFile(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }
}
