using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

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

        foreach (var traversal in new[]
                 {
                     "../escape.json",
                     "final/../../escape.json",
                     Path.Combine(Path.GetPathRoot(artifactRoot)!, "escape.json"),
                 })
        {
            var resolve = () => ResolveInside(artifactRoot, traversal);
            resolve.Should().Throw<InvalidDataException>();
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
        var artifactRoot = ResolveInside(EvidenceRoot(), durable.GetProperty("artifactRoot").GetString()!);
        artifactRoot.Should().Be(ArtifactRoot());
        ResolveInside(artifactRoot, durable.GetProperty("checksumFile").GetString()!)
            .Should().Be(Path.Combine(artifactRoot, "SHA256SUMS.txt"));
        var wpfManifestPath = ResolveInside(artifactRoot, durable.GetProperty("wpfManifest").GetString()!);
        var finalManifestPath = ResolveInside(artifactRoot, durable.GetProperty("finalAvaloniaManifest").GetString()!);
        var candidateManifestPath = ResolveInside(artifactRoot, durable.GetProperty("candidateAvaloniaManifest").GetString()!);
        var finalComparisonPath = ResolveInside(artifactRoot, durable.GetProperty("finalComparison").GetString()!);
        var candidateComparisonPath = ResolveInside(artifactRoot, durable.GetProperty("candidateComparison").GetString()!);

        var wpfCaptures = AssertManifest(wpfManifestPath, "wpf");
        var finalCaptures = AssertManifest(finalManifestPath, "avalonia");
        var candidateCaptures = AssertManifest(candidateManifestPath, "avalonia");
        AssertComparison(
            finalComparisonPath,
            wpfCaptures,
            finalCaptures,
            evidence.GetProperty("baseline").GetProperty("metrics"));
        AssertComparison(
            candidateComparisonPath,
            wpfCaptures,
            candidateCaptures,
            evidence.GetProperty("candidate").GetProperty("metrics"));

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

    private static IReadOnlyDictionary<string, CapturePixels> AssertManifest(string manifestPath, string host)
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
        var result = new Dictionary<string, CapturePixels>(StringComparer.Ordinal);
        foreach (var capture in captures)
        {
            capture.GetProperty("status").GetString().Should().Be("captured");
            capture.GetProperty("routeId").GetString().Should().Be("style");
            capture.GetProperty("fullPixelContent").GetProperty("passesContentGate").GetBoolean().Should().BeTrue();
            capture.GetProperty("targetPixelContent").GetProperty("passesContentGate").GetBoolean().Should().BeTrue();
            var manifestRoot = Path.GetDirectoryName(manifestPath)!;
            AssertPng(ResolveInside(manifestRoot, capture.GetProperty("fullPngPath").GetString()!));
            var targetPath = ResolveInside(manifestRoot, capture.GetProperty("targetPngPath").GetString()!);
            AssertPng(targetPath);
            var qualifiedScenarioId = capture.GetProperty("scenarioId").GetString()!;
            var scenarioId = qualifiedScenarioId[(host.Length + 1)..];
            result.Add(scenarioId, new CapturePixels(
                targetPath,
                capture.GetProperty("logicalWidth").GetInt32(),
                capture.GetProperty("logicalHeight").GetInt32()));
        }

        return result;
    }

    private static void AssertComparison(
        string comparisonPath,
        IReadOnlyDictionary<string, CapturePixels> wpfCaptures,
        IReadOnlyDictionary<string, CapturePixels> avaloniaCaptures,
        JsonElement recordedMetrics)
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
            using var wpf = DecodeAndScale(wpfCaptures[scenarioId]);
            using var avalonia = DecodeAndScale(avaloniaCaptures[scenarioId]);
            var recomputed = ComputeMetrics(wpf, avalonia);
            recomputed.ComparedPixels.Should().Be(actual.GetProperty("comparedPixels").GetInt32());
            recomputed.ChangedPixels.Should().Be(actual.GetProperty("changedPixels").GetInt32());
            recomputed.ChangedPixels.Should().Be(recorded.GetProperty("changedPixels").GetInt32());
            recomputed.ChangedRatio.Should().BeApproximately(actual.GetProperty("changedRatio").GetDouble(), 1e-15);
            recomputed.ChangedRatio.Should().BeApproximately(recorded.GetProperty("changedRatio").GetDouble(), 1e-15);
            recomputed.MeanAbsoluteChannelDelta.Should().BeApproximately(
                actual.GetProperty("meanAbsoluteChannelDelta").GetDouble(), 1e-12);
            recomputed.MeanAbsoluteChannelDelta.Should().BeApproximately(
                recorded.GetProperty("meanChannelDelta").GetDouble(), 1e-12);
            recomputed.P95AbsoluteChannelDelta.Should().BeApproximately(
                actual.GetProperty("p95AbsoluteChannelDelta").GetDouble(), 1e-12);
            recomputed.P95AbsoluteChannelDelta.Should().BeApproximately(
                recorded.GetProperty("p95ChannelDelta").GetDouble(), 1e-12);
            Bounds(row.GetProperty("wpfContent")).Should().Be(recorded.GetProperty("wpfBounds").GetString());
            Bounds(row.GetProperty("avaloniaContent")).Should().Be(recorded.GetProperty("avaloniaBounds").GetString());
            var heatmapPath = ResolveInside(Path.GetDirectoryName(comparisonPath)!, row.GetProperty("heatmapPath").GetString()!);
            AssertPng(heatmapPath);
            AssertHeatmap(wpf, avalonia, heatmapPath);
        }
    }

    private static IndependentMetrics ComputeMetrics(SKBitmap wpf, SKBitmap avalonia)
    {
        var count = Math.Min(wpf.Width * wpf.Height, avalonia.Width * avalonia.Height);
        long changed = 0;
        double total = 0;
        var deltas = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var wpfPixel = wpf.GetPixel(i % wpf.Width, i / wpf.Width);
            var avaloniaPixel = avalonia.GetPixel(i % avalonia.Width, i / avalonia.Width);
            var delta = (
                Math.Abs(wpfPixel.Red - avaloniaPixel.Red) +
                Math.Abs(wpfPixel.Green - avaloniaPixel.Green) +
                Math.Abs(wpfPixel.Blue - avaloniaPixel.Blue)) / 3.0;
            total += delta;
            deltas.Add(delta);
            if (delta > 8)
                changed++;
        }

        deltas.Sort();
        var p95Index = (int)Math.Min(deltas.Count - 1, deltas.Count * .95);
        return new IndependentMetrics(
            count,
            changed,
            count == 0 ? 1 : (double)changed / count,
            count == 0 ? 0 : total / count,
            count == 0 ? 0 : deltas[p95Index]);
    }

    private static SKBitmap DecodeAndScale(CapturePixels capture)
    {
        using var source = SKBitmap.Decode(capture.Path)
            ?? throw new InvalidDataException($"Cannot decode {capture.Path}");
        var result = new SKBitmap(Math.Max(1, capture.LogicalWidth), Math.Max(1, capture.LogicalHeight));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, new SKRect(0, 0, result.Width, result.Height));
        return result;
    }

    private static void AssertHeatmap(SKBitmap wpf, SKBitmap avalonia, string heatmapPath)
    {
        using var heatmap = SKBitmap.Decode(heatmapPath)
            ?? throw new InvalidDataException($"Cannot decode {heatmapPath}");
        heatmap.Width.Should().Be(Math.Min(wpf.Width, avalonia.Width));
        heatmap.Height.Should().Be(Math.Min(wpf.Height, avalonia.Height));
        for (var y = 0; y < heatmap.Height; y++)
        {
            for (var x = 0; x < heatmap.Width; x++)
            {
                var wpfPixel = wpf.GetPixel(x, y);
                var avaloniaPixel = avalonia.GetPixel(x, y);
                var delta = Math.Clamp((
                    Math.Abs(wpfPixel.Red - avaloniaPixel.Red) +
                    Math.Abs(wpfPixel.Green - avaloniaPixel.Green) +
                    Math.Abs(wpfPixel.Blue - avaloniaPixel.Blue)) / 3, 0, 255);
                var expected = new SKColor(
                    (byte)delta,
                    (byte)Math.Max(0, 80 - delta / 3),
                    (byte)Math.Max(0, 255 - delta),
                    255);
                if (heatmap.GetPixel(x, y) != expected)
                    throw new InvalidDataException($"Heatmap mismatch at ({x}, {y}) in {heatmapPath}");
            }
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
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Evidence path must be relative: {relativePath}");
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Evidence path escapes its root: {relativePath}");
        return fullPath;
    }

    private static JsonDocument LoadJson(string path) => JsonDocument.Parse(File.ReadAllText(path));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string ArtifactRoot() => EvidencePath(ArtifactDirectory);

    private static string EvidenceRoot() => RepositoryPath(EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar));

    private static string EvidencePath(string relativePath) =>
        RepositoryPath(EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), relativePath);

    private static string RepositoryPath(params string[] parts) =>
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));

    private sealed record CapturePixels(string Path, int LogicalWidth, int LogicalHeight);

    private sealed record IndependentMetrics(
        int ComparedPixels,
        long ChangedPixels,
        double ChangedRatio,
        double MeanAbsoluteChannelDelta,
        double P95AbsoluteChannelDelta);
}
