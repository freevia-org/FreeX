using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace FreeW.App.Avalonia.Tests;

public sealed class Wave199StyleDialogEvidenceTests
{
    private const string EvidenceDirectory = "freew/docs/parity/evidence";
    private const string EvidenceFile = "wave199-freew-style-dialog.json";
    private const string ArtifactDirectory = "wave199-freew-style-dialog-artifacts";

    private static readonly string[] ScenarioIds =
    [
        "style.initial",
        "style.populated",
        "style.validation-error",
    ];

    [Fact]
    public void Wave199_tracked_artifacts_recompute_every_checksum()
    {
        var artifactRoot = ArtifactRoot();
        var checksumPath = Path.Combine(artifactRoot, "SHA256SUMS.txt");
        File.Exists(checksumPath).Should().BeTrue();

        var entries = File.ReadAllLines(checksumPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseChecksum)
            .ToArray();
        var actualPaths = Directory.EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Equals(checksumPath, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(artifactRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        entries.Select(entry => entry.Path).Should().Equal(actualPaths);
        entries.Should().HaveCount(32);
        foreach (var entry in entries)
        {
            var path = ResolveInside(artifactRoot, entry.Path);
            Sha256(path).Should().Be(entry.Hash);
        }
    }

    [Fact]
    public void Wave199_manifests_pixels_and_metrics_are_independently_auditable()
    {
        using var evidenceDocument = LoadJson(EvidencePath(EvidenceFile));
        var evidence = evidenceDocument.RootElement;
        evidence.GetProperty("decision").GetString().Should().Be("style-width-candidate-rejected");
        evidence.GetProperty("candidate").GetProperty("retained").GetBoolean().Should().BeFalse();

        var durable = evidence.GetProperty("durableEvidence");
        durable.GetProperty("artifactRoot").GetString().Should().Be(ArtifactDirectory);
        var wpfManifestPath = EvidencePath(durable.GetProperty("wpfManifest").GetString()!);
        var finalManifestPath = EvidencePath(durable.GetProperty("finalAvaloniaManifest").GetString()!);
        var candidateManifestPath = EvidencePath(durable.GetProperty("candidateAvaloniaManifest").GetString()!);
        var finalComparisonPath = EvidencePath(durable.GetProperty("finalComparison").GetString()!);
        var candidateComparisonPath = EvidencePath(durable.GetProperty("candidateComparison").GetString()!);

        AssertManifest(wpfManifestPath, "wpf");
        AssertManifest(finalManifestPath, "avalonia");
        AssertManifest(candidateManifestPath, "avalonia");
        AssertComparison(finalComparisonPath, evidence.GetProperty("baseline").GetProperty("metrics"));
        AssertComparison(candidateComparisonPath, evidence.GetProperty("candidate").GetProperty("metrics"));

        Sha256(wpfManifestPath).Should().BeEquivalentTo(
            evidence.GetProperty("baseline").GetProperty("wpfManifestSha256").GetString());
        Sha256(finalManifestPath).Should().BeEquivalentTo(
            evidence.GetProperty("baseline").GetProperty("avaloniaManifestSha256").GetString());
        Sha256(finalComparisonPath).Should().BeEquivalentTo(
            evidence.GetProperty("baseline").GetProperty("comparisonSha256").GetString());
        Sha256(candidateManifestPath).Should().BeEquivalentTo(
            evidence.GetProperty("candidate").GetProperty("avaloniaManifestSha256").GetString());
        Sha256(candidateComparisonPath).Should().BeEquivalentTo(
            evidence.GetProperty("candidate").GetProperty("comparisonSha256").GetString());

        foreach (var scenarioId in ScenarioIds)
        {
            evidence.GetProperty("candidate").GetProperty("metrics")
                .GetProperty(scenarioId).GetProperty("changedPixels").GetInt32()
                .Should().BeGreaterThan(evidence.GetProperty("baseline").GetProperty("metrics")
                    .GetProperty(scenarioId).GetProperty("changedPixels").GetInt32());
        }

        var styleSource = File.ReadAllText(RepositoryPath("freew", "FreeW.App.Avalonia", "StyleDialog.cs"));
        styleSource.Should().NotContain("private static T Field<T>(StyleDialogFieldKind kind, T field)");
    }

    private static void AssertManifest(string manifestPath, string host)
    {
        using var document = LoadJson(manifestPath);
        var root = document.RootElement;
        root.GetProperty("schema").GetString().Should().Be("freew.dialog-capture-manifest.v1");
        root.GetProperty("host").GetString().Should().Be(host);
        Path.IsPathRooted(root.GetProperty("captureRoot").GetString()).Should().BeTrue();

        var captures = root.GetProperty("captures").EnumerateArray().ToArray();
        captures.Should().HaveCount(ScenarioIds.Length);
        captures.Select(capture => capture.GetProperty("scenarioId").GetString())
            .Should().BeEquivalentTo(ScenarioIds.Select(id => host + "." + id));
        foreach (var capture in captures)
        {
            capture.GetProperty("status").GetString().Should().Be("captured");
            capture.GetProperty("routeId").GetString().Should().Be("style");
            capture.GetProperty("fullPixelContent").GetProperty("passesContentGate").GetBoolean().Should().BeTrue();
            capture.GetProperty("targetPixelContent").GetProperty("passesContentGate").GetBoolean().Should().BeTrue();
            AssertPng(ResolveInside(Path.GetDirectoryName(manifestPath)!, capture.GetProperty("fullPngPath").GetString()!));
            AssertPng(ResolveInside(Path.GetDirectoryName(manifestPath)!, capture.GetProperty("targetPngPath").GetString()!));
        }
    }

    private static void AssertComparison(string comparisonPath, JsonElement recordedMetrics)
    {
        using var document = LoadJson(comparisonPath);
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(row => ScenarioIds.Contains(row.GetProperty("scenarioId").GetString(), StringComparer.Ordinal))
            .ToArray();
        rows.Select(row => row.GetProperty("scenarioId").GetString()).Should().BeEquivalentTo(ScenarioIds);
        rows.Should().OnlyContain(row =>
            row.GetProperty("captureStatus").GetString() == "captured/captured" &&
            row.GetProperty("classification").GetString() == "genuine-visual-mismatch" &&
            row.GetProperty("wpfContent").GetProperty("passesContentGate").GetBoolean() &&
            row.GetProperty("avaloniaContent").GetProperty("passesContentGate").GetBoolean());

        foreach (var row in rows)
        {
            var scenarioId = row.GetProperty("scenarioId").GetString()!;
            var actual = row.GetProperty("metrics");
            var recorded = recordedMetrics.GetProperty(scenarioId);
            actual.GetProperty("changedPixels").GetInt32().Should().Be(recorded.GetProperty("changedPixels").GetInt32());
            actual.GetProperty("changedRatio").GetDouble().Should().Be(recorded.GetProperty("changedRatio").GetDouble());
            actual.GetProperty("meanAbsoluteChannelDelta").GetDouble().Should().Be(recorded.GetProperty("meanChannelDelta").GetDouble());
            actual.GetProperty("p95AbsoluteChannelDelta").GetDouble().Should().Be(recorded.GetProperty("p95ChannelDelta").GetDouble());
            Bounds(row.GetProperty("wpfContent")).Should().Be(recorded.GetProperty("wpfBounds").GetString());
            Bounds(row.GetProperty("avaloniaContent")).Should().Be(recorded.GetProperty("avaloniaBounds").GetString());
            AssertPng(ResolveInside(Path.GetDirectoryName(comparisonPath)!, row.GetProperty("heatmapPath").GetString()!));
        }
    }

    private static string Bounds(JsonElement content)
    {
        var bounds = content.GetProperty("contentBounds");
        return $"{bounds.GetProperty("width").GetInt32()}x{bounds.GetProperty("height").GetInt32()}";
    }

    private static void AssertPng(string path)
    {
        File.Exists(path).Should().BeTrue();
        var bytes = File.ReadAllBytes(path);
        bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }).Should().BeTrue();
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)).Should().BeGreaterThan(0);
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)).Should().BeGreaterThan(0);
    }

    private static (string Hash, string Path) ParseChecksum(string line)
    {
        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        parts.Should().HaveCount(2);
        parts[0].Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        Path.IsPathRooted(parts[1]).Should().BeFalse();
        return (parts[0], parts[1]);
    }

    private static string ResolveInside(string root, string relativePath)
    {
        Path.IsPathRooted(relativePath).Should().BeFalse();
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        fullPath.Should().StartWith(fullRoot + Path.DirectorySeparatorChar);
        return fullPath;
    }

    private static JsonDocument LoadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string ArtifactRoot() => EvidencePath(ArtifactDirectory);

    private static string EvidencePath(string relativePath) =>
        RepositoryPath(EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), relativePath);

    private static string RepositoryPath(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));
}
