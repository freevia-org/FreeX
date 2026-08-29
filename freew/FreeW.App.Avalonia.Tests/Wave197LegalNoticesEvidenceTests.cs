using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeW.App.Avalonia.Tests;

public sealed class Wave197LegalNoticesEvidenceTests
{
    private const string EvidenceDirectory = "freew/docs/parity/evidence";
    private const string BundleFile = "wave197-freew-legal-notices-template-candidates.json";
    private const string RawEvidenceFile = "wave197-freew-legal-notices-raw-evidence.json";
    private const string SourceFile = "shared/Free.Shared.Shell.Avalonia/AvaloniaLegalNoticesDialog.cs";
    private const string SourceHashAlgorithm = "sha256-normalized-lf-utf8-source-text";

    private static readonly string[] ScenarioIds =
    new[]
    {
        "legal-notices.initial",
        "legal-notices.tab-project-license",
        "legal-notices.tab-legal-notices",
        "legal-notices.tab-privacy-notice",
        "legal-notices.tab-third-party-license-texts",
        "legal-notices.tab-third-party-notices",
    };

    [Fact]
    public void Wave197_evidence_bundle_recomputes_every_tracked_checksum()
    {
        var root = FindRepositoryRoot();
        var evidence = EvidencePath(root);
        new[] { "README.md", "SHA256SUMS.txt", BundleFile, RawEvidenceFile }
            .Select(file => Path.Combine(evidence, file))
            .Should().OnlyContain(file => File.Exists(file));

        var sums = File.ReadAllLines(Path.Combine(evidence, "SHA256SUMS.txt"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(ParseChecksum)
            .ToArray();
        sums.Select(entry => entry.Path).Should().BeEquivalentTo(new[] { "README.md", BundleFile, RawEvidenceFile });
        sums.Should().OnlyContain(entry =>
            entry.Path == Path.GetFileName(entry.Path) && !Path.IsPathRooted(entry.Path));

        foreach (var entry in sums)
        {
            var path = Path.Combine(evidence, entry.Path);
            File.Exists(path).Should().BeTrue($"checksum entry {entry.Path} must be tracked in the evidence root");
            Sha256(path).Should().Be(entry.Hash);
        }
    }

    [Fact]
    public void Wave197_evidence_records_all_baseline_and_rejected_candidate_metrics()
    {
        using var bundle = LoadBundle();
        using var raw = LoadRawEvidence();
        var scope = bundle.RootElement.GetProperty("scope");
        scope.GetProperty("route").GetString().Should().Be("legal-notices");
        scope.GetProperty("logicalSize").GetString().Should().Be("620x600");
        scope.GetProperty("scenarioIds").EnumerateArray().Select(value => value.GetString())
            .Should().Equal(ScenarioIds);

        var metrics = bundle.RootElement.GetProperty("metrics");
        var baseline = metrics.GetProperty("baseline");
        var surface = metrics.GetProperty("surface-margin-candidate");
        var linebox = metrics.GetProperty("linebox-candidate");
        foreach (var scenarioId in ScenarioIds)
        {
            baseline.GetProperty(scenarioId).GetProperty("changedPixels").GetInt32()
                .Should().Be(scenarioId switch
                {
                    "legal-notices.initial" or "legal-notices.tab-project-license" => 31491,
                    "legal-notices.tab-legal-notices" => 69858,
                    "legal-notices.tab-privacy-notice" => 59025,
                    "legal-notices.tab-third-party-license-texts" => 66445,
                    "legal-notices.tab-third-party-notices" => 59886,
                    _ => throw new InvalidOperationException(scenarioId),
                });
            surface.GetProperty(scenarioId).GetProperty("changedPixels").GetInt32()
                .Should().Be(scenarioId switch
                {
                    "legal-notices.initial" or "legal-notices.tab-project-license" => 31928,
                    "legal-notices.tab-legal-notices" => 70297,
                    "legal-notices.tab-privacy-notice" => 59465,
                    "legal-notices.tab-third-party-license-texts" => 66884,
                    "legal-notices.tab-third-party-notices" => 60325,
                    _ => throw new InvalidOperationException(scenarioId),
                });
            linebox.GetProperty(scenarioId).GetProperty("changedPixels").GetInt32()
                .Should().Be(scenarioId switch
                {
                    "legal-notices.initial" or "legal-notices.tab-project-license" => 31491,
                    "legal-notices.tab-legal-notices" => 69050,
                    "legal-notices.tab-privacy-notice" => 63164,
                    "legal-notices.tab-third-party-license-texts" => 63353,
                    "legal-notices.tab-third-party-notices" => 65338,
                    _ => throw new InvalidOperationException(scenarioId),
                });
        }

        surface.GetProperty("legal-notices.tab-legal-notices").GetProperty("changedPixels").GetInt32()
            .Should().BeGreaterThan(baseline.GetProperty("legal-notices.tab-legal-notices").GetProperty("changedPixels").GetInt32());
        linebox.GetProperty("legal-notices.tab-privacy-notice").GetProperty("changedPixels").GetInt32()
            .Should().BeGreaterThan(baseline.GetProperty("legal-notices.tab-privacy-notice").GetProperty("changedPixels").GetInt32());

        AssertBundleMetricsMatchRaw(bundle.RootElement, raw.RootElement);
    }

    [Fact]
    public void Wave197_evidence_provenance_matches_tracked_raw_evidence()
    {
        using var bundle = LoadBundle();
        using var raw = LoadRawEvidence();
        var root = FindRepositoryRoot();
        var provenance = bundle.RootElement.GetProperty("provenance");
        var rawEvidence = provenance.GetProperty("rawEvidence");
        rawEvidence.GetProperty("path").GetString().Should().Be(RawEvidenceFile);
        rawEvidence.GetProperty("schema").GetString().Should().Be(raw.RootElement.GetProperty("schema").GetString());
        Sha256(Path.Combine(EvidencePath(root), RawEvidenceFile))
            .Should().Be(rawEvidence.GetProperty("sha256").GetString());

        var extraction = raw.RootElement.GetProperty("extraction");
        extraction.GetProperty("kind").GetString().Should().Be("lossless-route-local");
        extraction.GetProperty("routeId").GetString().Should().Be("legal-notices");
        extraction.GetProperty("scenarioIds").EnumerateArray().Select(value => value.GetString())
            .Should().Equal(ScenarioIds);
        extraction.GetProperty("omittedFields").GetProperty("captureManifest").EnumerateArray()
            .Select(value => value.GetString()).Should().BeEquivalentTo("captureRoot", "fullPngPath", "targetPngPath");

        var inventory = provenance.GetProperty("inventory");
        var rawInventory = raw.RootElement.GetProperty("inventory");
        inventory.GetProperty("sourcePath").GetString().Should().Be(rawInventory.GetProperty("originalPath").GetString());
        inventory.GetProperty("originalSha256").GetString().Should().Be(rawInventory.GetProperty("originalSha256").GetString());
        inventory.GetProperty("generatedFromSha256").GetString().Should().Be(rawInventory.GetProperty("generatedFromSha256").GetString());
        inventory.GetProperty("rawEvidenceId").GetString().Should().Be("inventory");
        rawInventory.GetProperty("evidence").GetProperty("scenarios").EnumerateArray()
            .Should().HaveCount(ScenarioIds.Length * 2);
        var rawInventoryScenarioIds = rawInventory.GetProperty("evidence").GetProperty("scenarios")
            .EnumerateArray()
            .Select(scenario =>
            {
                scenario.GetProperty("routeId").GetString().Should().Be("legal-notices");
                return scenario.GetProperty("id").GetString()!;
            })
            .ToArray();
        rawInventoryScenarioIds.Should().OnlyHaveUniqueItems();
        foreach (var host in new[] { "avalonia", "wpf" })
        {
            var hostScenarioIds = rawInventoryScenarioIds
                .Where(id => id.StartsWith(host + ".", StringComparison.Ordinal))
                .Select(id => id[(host.Length + 1)..])
                .ToArray();
            hostScenarioIds.Should().OnlyHaveUniqueItems();
            hostScenarioIds.Should().BeEquivalentTo(ScenarioIds);
        }

        var manifests = provenance.GetProperty("captureManifests").EnumerateArray().ToArray();
        manifests.Should().HaveCount(4);
        foreach (var manifest in manifests)
        {
            var id = manifest.GetProperty("rawEvidenceId").GetString()!;
            var rawManifest = FindById(raw.RootElement.GetProperty("captureManifests"), id);
            manifest.GetProperty("sourcePath").GetString().Should().Be(rawManifest.GetProperty("originalPath").GetString());
            manifest.GetProperty("originalSha256").GetString().Should().Be(rawManifest.GetProperty("originalSha256").GetString());
            manifest.GetProperty("schema").GetString().Should().Be("freew.dialog-capture-manifest.v1");
            manifest.GetProperty("captureCount").GetInt32().Should().Be(6);
            manifest.GetProperty("contentValidatedCount").GetInt32().Should().Be(6);
            manifest.GetProperty("sha256Of").GetString().Should().Be("disposable-capture-manifest-json-content");

            var evidence = rawManifest.GetProperty("evidence");
            evidence.GetProperty("schema").GetString().Should().Be(manifest.GetProperty("schema").GetString());
            evidence.GetProperty("schemaVersion").GetInt32().Should().Be(manifest.GetProperty("schemaVersion").GetInt32());
            var host = manifest.GetProperty("host").GetString()!;
            evidence.GetProperty("host").GetString().Should().Be(host);
            var captures = evidence.GetProperty("captures").EnumerateArray().ToArray();
            captures.Should().HaveCount(ScenarioIds.Length);
            var captureScenarioIds = captures.Select(capture => capture.GetProperty("scenarioId").GetString()!).ToArray();
            captureScenarioIds.Should().OnlyHaveUniqueItems();
            captureScenarioIds.Should().BeEquivalentTo(ScenarioIds.Select(scenarioId => host + "." + scenarioId));
            captures.Should().OnlyContain(capture =>
                capture.GetProperty("host").GetString() == host &&
                capture.GetProperty("routeId").GetString() == "legal-notices" &&
                capture.GetProperty("status").GetString() == "captured" &&
                capture.GetProperty("logicalWidth").GetInt32() == 620 &&
                capture.GetProperty("logicalHeight").GetInt32() == 600 &&
                capture.GetProperty("actualWidth").GetInt32() == 620 &&
                capture.GetProperty("actualHeight").GetInt32() == 600 &&
                capture.GetProperty("fullPixelContent").GetProperty("passesContentGate").GetBoolean() &&
                capture.GetProperty("targetPixelContent").GetProperty("passesContentGate").GetBoolean());
        }

        var reports = provenance.GetProperty("comparisonReports").EnumerateArray().ToArray();
        reports.Should().HaveCount(3);
        foreach (var report in reports)
        {
            var id = report.GetProperty("rawEvidenceId").GetString()!;
            var rawReport = FindById(raw.RootElement.GetProperty("comparisonReports"), id);
            report.GetProperty("sourcePath").GetString().Should().Be(rawReport.GetProperty("originalPath").GetString());
            report.GetProperty("originalSha256").GetString().Should().Be(rawReport.GetProperty("originalSha256").GetString());
            report.GetProperty("generatedFromSha256").GetString().Should().Be(rawReport.GetProperty("generatedFromSha256").GetString());
            report.GetProperty("sha256Of").GetString().Should().Be("disposable-comparison-report-json-content");
            report.GetProperty("generatedFromSha256Of").GetString().Should().Be("disposable-comparison-input-content");
            rawReport.GetProperty("evidence").GetProperty("rows").EnumerateArray().Should().HaveCount(ScenarioIds.Length);
        }
    }

    [Fact]
    public void Wave197_evidence_proves_source_mutations_were_reverted()
    {
        using var bundle = LoadBundle();
        var sourceMutations = bundle.RootElement.GetProperty("sourceMutations").EnumerateArray().ToArray();
        sourceMutations.Should().HaveCount(2);
        sourceMutations.Should().OnlyContain(mutation => mutation.GetProperty("reverted").GetBoolean());

        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, SourceFile.Replace('/', Path.DirectorySeparatorChar));
        var source = File.ReadAllText(sourcePath);
        bundle.RootElement.GetProperty("sourceHashAlgorithm").GetString().Should().Be(SourceHashAlgorithm);
        var restoredHash = Sha256NormalizedUtf8Text(source);
        Sha256NormalizedUtf8Text(source).Should().Be(
            Sha256NormalizedUtf8Text(source.Replace("\r\n", "\n", StringComparison.Ordinal)));
        foreach (var mutation in sourceMutations)
        {
            mutation.GetProperty("sourceFile").GetString().Should().Be(SourceFile);
            mutation.GetProperty("sha256Of").GetString().Should().Be(SourceHashAlgorithm);
            mutation.GetProperty("baselineSha256").GetString().Should().Be(restoredHash);
            mutation.GetProperty("restoredSha256").GetString().Should().Be(restoredHash);
            var baselineValue = mutation.GetProperty("baselineValue").GetString()!;
            var candidateValue = mutation.GetProperty("candidateValue").GetString()!;
            source.Should().Contain(baselineValue);
            source.Should().NotContain(candidateValue);
            mutation.GetProperty("candidateSha256").GetString()
                .Should().Be(Sha256NormalizedUtf8Text(source.Replace(baselineValue, candidateValue, StringComparison.Ordinal)));
        }
    }

    private static void AssertBundleMetricsMatchRaw(JsonElement bundle, JsonElement raw)
    {
        var bundleMetrics = bundle.GetProperty("metrics");
        bundleMetrics.GetProperty("comparedPixels").GetInt32().Should().Be(372000);
        foreach (var reportId in new[] { "baseline", "surface-margin-candidate", "linebox-candidate" })
        {
            var rawReport = FindById(raw.GetProperty("comparisonReports"), reportId);
            foreach (var scenarioId in ScenarioIds)
            {
                var rawRow = rawReport.GetProperty("evidence").GetProperty("rows").EnumerateArray()
                    .Single(row => row.GetProperty("scenarioId").GetString() == scenarioId);
                var rawMetrics = rawRow.GetProperty("metrics");
                var recorded = bundleMetrics.GetProperty(reportId).GetProperty(scenarioId);
                recorded.GetProperty("changedPixels").GetInt32().Should().Be(rawMetrics.GetProperty("changedPixels").GetInt32());
                recorded.GetProperty("changedRatio").GetDouble().Should().Be(rawMetrics.GetProperty("changedRatio").GetDouble());
                recorded.GetProperty("meanChannelDelta").GetDouble().Should().Be(rawMetrics.GetProperty("meanAbsoluteChannelDelta").GetDouble());
                rawMetrics.GetProperty("comparedPixels").GetInt32().Should().Be(bundleMetrics.GetProperty("comparedPixels").GetInt32());
            }
        }
    }

    private static JsonDocument LoadBundle() => LoadEvidenceJson(BundleFile);

    private static JsonDocument LoadRawEvidence() => LoadEvidenceJson(RawEvidenceFile);

    private static JsonDocument LoadEvidenceJson(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(EvidencePath(FindRepositoryRoot()), file)));

    private static JsonElement FindById(JsonElement array, string id) =>
        array.EnumerateArray().Single(element => element.GetProperty("id").GetString() == id);

    private static (string Hash, string Path) ParseChecksum(string line)
    {
        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        parts.Should().HaveCount(2);
        HasLength64(parts[0]).Should().BeTrue();
        return (parts[0], parts[1]);
    }

    private static string EvidencePath(string root) =>
        Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar));

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Sha256NormalizedUtf8Text(string value) =>
        Sha256Text(value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal));

    private static bool HasLength64(string? value) => value?.Length == 64;

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
