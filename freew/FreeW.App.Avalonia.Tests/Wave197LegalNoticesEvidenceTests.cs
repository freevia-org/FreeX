using System.Security.Cryptography;
using System.Text.Json;

namespace FreeW.App.Avalonia.Tests;

public sealed class Wave197LegalNoticesEvidenceTests
{
    private const string EvidenceDirectory = "freew/docs/parity/evidence";
    private const string BundleFile = "wave197-freew-legal-notices-template-candidates.json";
    private const string SourceFile = "shared/Free.Shared.Shell.Avalonia/AvaloniaLegalNoticesDialog.cs";

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
    public void Wave197_evidence_bundle_has_required_tracked_files()
    {
        var root = FindRepositoryRoot();
        var evidence = Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar));
        new[] { "README.md", "SHA256SUMS.txt", BundleFile }
            .Select(file => Path.Combine(evidence, file))
            .Should().OnlyContain(file => File.Exists(file));

        var sums = File.ReadAllLines(Path.Combine(evidence, "SHA256SUMS.txt"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => new { Hash = parts[0], Path = parts[1] })
            .ToArray();
        sums.Should().ContainSingle(entry => entry.Path == "README.md");
        sums.Should().ContainSingle(entry => entry.Path == BundleFile);

        foreach (var entry in sums.Where(entry => entry.Path is "README.md" or BundleFile))
        {
            var path = Path.Combine(evidence, entry.Path);
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
                .Should().Be(entry.Hash);
        }
    }

    [Fact]
    public void Wave197_evidence_records_all_baseline_and_rejected_candidate_metrics()
    {
        using var bundle = LoadBundle();
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
    }

    [Fact]
    public void Wave197_evidence_records_capture_provenance_and_checksums()
    {
        using var bundle = LoadBundle();
        var provenance = bundle.RootElement.GetProperty("provenance");
        var inventory = provenance.GetProperty("inventory");
        inventory.GetProperty("sha256").GetString().Should().HaveLength(64);
        inventory.GetProperty("sha256Of").GetString().Should().Be("disposable-inventory-json-content");
        inventory.GetProperty("generatedFromSha256").GetString().Should().HaveLength(64);
        inventory.GetProperty("generatedFromSha256Of").GetString().Should().Be("inventory-generator-input-content");
        var manifests = provenance.GetProperty("captureManifests").EnumerateArray().ToArray();
        manifests.Should().HaveCount(4);
        manifests.Should().OnlyContain(manifest =>
            manifest.GetProperty("schema").GetString() == "freew.dialog-capture-manifest.v1" &&
            manifest.GetProperty("captureCount").GetInt32() == 6 &&
            manifest.GetProperty("contentValidatedCount").GetInt32() == 6 &&
            manifest.GetProperty("sha256Of").GetString() == "disposable-capture-manifest-json-content" &&
            HasLength64(manifest.GetProperty("sha256").GetString()));

        var reports = provenance.GetProperty("comparisonReports").EnumerateArray().ToArray();
        reports.Should().HaveCount(3);
        reports.Should().OnlyContain(report =>
            report.GetProperty("sha256Of").GetString() == "disposable-comparison-report-json-content" &&
            report.GetProperty("generatedFromSha256Of").GetString() == "disposable-comparison-input-content" &&
            HasLength64(report.GetProperty("sha256").GetString()) &&
            HasLength64(report.GetProperty("generatedFromSha256").GetString()));
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
        var restoredHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        foreach (var mutation in sourceMutations)
        {
            mutation.GetProperty("sourceFile").GetString().Should().Be(SourceFile);
            mutation.GetProperty("baselineSha256").GetString().Should().Be(restoredHash);
            mutation.GetProperty("restoredSha256").GetString().Should().Be(restoredHash);
            source.Should().Contain(mutation.GetProperty("baselineValue").GetString());
            source.Should().NotContain(mutation.GetProperty("candidateValue").GetString());
            mutation.GetProperty("candidateSha256").GetString().Should().HaveLength(64);
        }
    }

    private static JsonDocument LoadBundle()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar),
            BundleFile);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool HasLength64(string? value) => value?.Length == 64;

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
}
