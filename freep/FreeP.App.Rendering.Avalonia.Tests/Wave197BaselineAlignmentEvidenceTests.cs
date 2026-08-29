using System.Security.Cryptography;
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
            .Should().Be("freep.parity.wave197.deck17-baseline-alignment.v2");
        root.GetProperty("status").GetString().Should().Be("candidate-refuted");

        var provenance = root.GetProperty("sourceProvenance");
        provenance.GetProperty("sourceRevision").ValueKind.Should().Be(JsonValueKind.Null);
        provenance.GetProperty("sourceRevisionRole").GetString().Should().Be("not-recorded");
        provenance.GetProperty("generationLinkage").GetString()
            .Should().Be("not-independently-proven");

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

        var integrity = root.GetProperty("imageIntegrity");
        integrity.GetProperty("status").GetString().Should().Be("incomplete-missing-tracked-images");
        integrity.GetProperty("claimBoundary").GetString()
            .Should().Contain("no current byte-integrity or source-generation claim is made");
    }

    [Fact]
    public void EvidenceManifest_ExplicitlyRecordsEveryMissingCandidateImage()
    {
        using var images = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));
        using var metrics = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var integrity = metrics.RootElement.GetProperty("imageIntegrity");
        var missingImages = integrity.GetProperty("missingImages")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        missingImages.Should().BeEquivalentTo(
            images.RootElement.EnumerateObject().Select(image => image.Name));

        foreach (var image in images.RootElement.EnumerateObject())
        {
            var imagePath = EvidenceFile(image.Name);
            File.Exists(imagePath).Should().BeFalse(
                $"the evidence contract must not claim verification for the known-missing image {image.Name}");
            image.Value.GetString().Should().MatchRegex("^[0-9a-f]{64}$");

            var metricProperty = image.Name switch
            {
                "candidate-avalonia-slide-01.png" => "candidateAvaloniaSlide01Sha256",
                "candidate-avalonia-slide-02.png" => "candidateAvaloniaSlide02Sha256",
                "candidate-wpf-slide-01.png" => "candidateWpfSlide01Sha256",
                "candidate-wpf-slide-02.png" => "candidateWpfSlide02Sha256",
                _ => null
            };
            metricProperty.Should().NotBeNull($"the metrics must bind the recorded image {image.Name}");
            integrity.GetProperty(metricProperty!).GetString().Should().Be(image.Value.GetString());
        }

        AssertFileHash(
            WorkspaceFile("docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/avalonia-slide-01.png"),
            integrity.GetProperty("acceptedAvaloniaSlide01Sha256").GetString());
        AssertFileHash(
            WorkspaceFile("docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/avalonia-slide-02.png"),
            integrity.GetProperty("acceptedAvaloniaSlide02Sha256").GetString());
    }

    private static void AssertFileHash(string filePath, string? expectedHash)
    {
        File.Exists(filePath).Should().BeTrue($"the accepted evidence image must exist: {filePath}");
        using var stream = File.OpenRead(filePath);
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant().Should().Be(expectedHash);
    }

    private static string EvidenceFile(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }

    private static string WorkspaceFile(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
