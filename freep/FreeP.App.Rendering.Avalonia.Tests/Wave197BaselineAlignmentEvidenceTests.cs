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

    [Fact]
    public void EvidenceImages_MatchTheirRecordedHashes()
    {
        using var images = JsonDocument.Parse(File.ReadAllText(EvidenceFile("images.json")));
        using var metrics = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var integrity = metrics.RootElement.GetProperty("imageIntegrity");

        foreach (var image in images.RootElement.EnumerateObject())
        {
            var imagePath = EvidenceFile(image.Name);
            File.Exists(imagePath).Should().BeTrue($"the recorded evidence image must exist: {image.Name}");

            using var stream = File.OpenRead(imagePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            actualHash.Should().Be(image.Value.GetString(), $"the images.json hash must match {image.Name}");

            var metricProperty = image.Name switch
            {
                "candidate-avalonia-slide-01.png" => "candidateAvaloniaSlide01Sha256",
                "candidate-avalonia-slide-02.png" => "candidateAvaloniaSlide02Sha256",
                "candidate-wpf-slide-01.png" => "candidateWpfSlide01Sha256",
                "candidate-wpf-slide-02.png" => "candidateWpfSlide02Sha256",
                _ => null
            };
            metricProperty.Should().NotBeNull($"the metrics must bind the recorded image {image.Name}");
            integrity.GetProperty(metricProperty!).GetString().Should().Be(actualHash);
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
