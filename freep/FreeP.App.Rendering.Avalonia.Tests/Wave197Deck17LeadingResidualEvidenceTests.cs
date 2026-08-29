using System.Diagnostics;
using System.Security.Cryptography;
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

    [Fact]
    public void SourceAndRetainedImageReferences_MatchTrackedFiles()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(EvidenceFile("metrics.json")));
        var root = document.RootElement;
        var workspaceRoot = WorkspaceRoot();

        var sourceRevision = root.GetProperty("sourceRevision").GetString();
        sourceRevision.Should().NotBeNullOrWhiteSpace();
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "cat-file", "-e", $"{sourceRevision}^{{commit}}" }
        }))
        {
            process.Should().NotBeNull();
            process!.WaitForExit();
            process.ExitCode.Should().Be(0, "the recorded source revision must resolve to a Git commit");
        }

        var target = root.GetProperty("target");
        var officeReference = target.GetProperty("officeReference").GetString();
        var officeReferenceHash = target.GetProperty("officeReferenceSha256").GetString();
        officeReference.Should().NotBeNullOrWhiteSpace();
        officeReferenceHash.Should().NotBeNullOrWhiteSpace();

        var retainedEvidence = root.GetProperty("retainedEvidence")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray();

        foreach (var relativePath in retainedEvidence)
        {
            var filePath = WorkspaceFile(relativePath);
            File.Exists(filePath).Should().BeTrue($"retained evidence must exist: {relativePath}");

            if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
                continue;

            var expectedHash = string.Equals(relativePath, officeReference, StringComparison.Ordinal)
                ? officeReferenceHash
                : ReadImageManifestHash(filePath);

            expectedHash.Should().NotBeNullOrWhiteSpace($"a tracked image hash must exist for {relativePath}");
            using var stream = File.OpenRead(filePath);
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
                .Should().Be(expectedHash, $"the tracked image hash must match {relativePath}");
        }
    }

    private static string EvidenceFile(string fileName)
    {
        return Path.Combine(WorkspaceRoot(), EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }

    private static string WorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

    private static string WorkspaceFile(string relativePath) =>
        Path.Combine(WorkspaceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string? ReadImageManifestHash(string imagePath)
    {
        var manifestPath = Path.Combine(Path.GetDirectoryName(imagePath)!, "images.json");
        if (!File.Exists(manifestPath))
            return null;

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.TryGetProperty(Path.GetFileName(imagePath), out var hash)
            ? hash.GetString()
            : null;
    }
}
