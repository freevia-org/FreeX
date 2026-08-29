using System.Text.Json;

namespace FreeW.App.Avalonia.Tests;

public sealed class Wave198TablePropertiesEvidenceTests
{
    private const string EvidenceDirectory = "freew/docs/parity/evidence";
    private const string EvidenceFile = "wave198-freew-table-properties-tab-pane-raw-evidence.json";
    private const string SourceCommit = "dc187494872daf30b7a080cecd7874d34b9db84d";

    private static readonly string[] ScenarioIds =
    [
        "table-properties.initial",
        "table-properties.populated",
        "table-properties.tab-cell",
        "table-properties.tab-column",
        "table-properties.tab-row",
        "table-properties.tab-table",
        "table-properties.validation-error",
        "borders-and-shading.initial",
        "borders-and-shading.populated",
        "borders-and-shading.validation-error",
    ];

    [Fact]
    public void Wave198_raw_evidence_preserves_ids_metrics_totals_source_and_manifest_hashes()
    {
        using var document = LoadEvidence();
        var root = document.RootElement;

        root.GetProperty("schema").GetString().Should().Be("freew.wave198.tab-pane-raw-evidence.v1");
        root.GetProperty("route").GetString().Should().Be("table-properties");
        root.GetProperty("sourceCommit").GetString().Should().Be(SourceCommit);
        HasHexLength(root.GetProperty("sourceCommit").GetString(), 40).Should().BeTrue();
        root.GetProperty("sourceFiles").EnumerateArray().Select(value => value.GetString())
            .Should().BeEquivalentTo(
                "shared/Free.Shared.Shell.Avalonia/AvaloniaCompactDialogChrome.cs",
                "tests/Free.Shared.Shell.Avalonia.Tests/DialogTabChromeParityTests.cs");

        var scope = root.GetProperty("scope");
        scope.GetProperty("canonicalInventoryRows").GetInt32().Should().Be(291);
        scope.GetProperty("canonicalGenuineMismatches").GetInt32().Should().Be(141);
        scope.GetProperty("targetScenarioCount").GetInt32().Should().Be(7);
        scope.GetProperty("controlScenarioCount").GetInt32().Should().Be(3);
        scope.GetProperty("scenarioCount").GetInt32().Should().Be(ScenarioIds.Length);
        scope.GetProperty("scenarioIds").EnumerateArray().Select(value => value.GetString())
            .Should().Equal(ScenarioIds);

        var scenarios = root.GetProperty("scenarios").EnumerateArray().ToArray();
        scenarios.Should().HaveCount(ScenarioIds.Length);
        scenarios.Select(scenario => scenario.GetProperty("id").GetString())
            .Should().Equal(ScenarioIds);
        scenarios.Select(scenario => scenario.GetProperty("id").GetString())
            .Should().OnlyHaveUniqueItems();
        scenarios.Count(scenario => scenario.GetProperty("family").GetString() == "target").Should().Be(7);
        scenarios.Count(scenario => scenario.GetProperty("family").GetString() == "control").Should().Be(3);

        foreach (var scenario in scenarios)
        {
            foreach (var state in new[] { "before", "after" })
            {
                var metrics = scenario.GetProperty(state);
                metrics.GetProperty("comparedPixels").GetInt32().Should().Be(336000);
                metrics.GetProperty("changedPixels").GetInt32().Should().BeGreaterThanOrEqualTo(0);
                metrics.GetProperty("changedRatio").GetDouble().Should().BeInRange(0, 1);
                metrics.GetProperty("meanAbsoluteChannelDelta").GetDouble().Should().BeGreaterThanOrEqualTo(0);
                metrics.GetProperty("p95AbsoluteChannelDelta").GetDouble().Should().BeGreaterThanOrEqualTo(0);
                metrics.GetProperty("luminanceSimilarity").GetDouble().Should().BeInRange(0, 1);
                metrics.GetProperty("perceptualHashDistance").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            }
        }

        var totals = root.GetProperty("totals");
        totals.GetProperty("scenarioCount").GetInt32().Should().Be(scenarios.Length);
        totals.GetProperty("targetScenarioCount").GetInt32().Should().Be(7);
        totals.GetProperty("controlScenarioCount").GetInt32().Should().Be(3);
        AssertFamilyTotals(scenarios, "target", totals, "target");
        AssertFamilyTotals(scenarios, "control", totals, "control");

        var manifests = root.GetProperty("manifests");
        var manifestEntries = manifests.EnumerateObject()
            .SelectMany(family => family.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .ToArray();
        manifestEntries.Should().HaveCount(6);
        manifestEntries.Select(entry => entry.GetProperty("path").GetString())
            .Should().OnlyContain(path => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path));
        manifestEntries.Should().OnlyContain(entry =>
            entry.GetProperty("captureCount").GetInt32() > 0 &&
            (entry.GetProperty("host").GetString() == "wpf" || entry.GetProperty("host").GetString() == "avalonia") &&
            HasHexLength(entry.GetProperty("sha256").GetString(), 64));
        manifestEntries.Select(entry => entry.GetProperty("sha256").GetString())
            .Should().OnlyHaveUniqueItems();

        var boundary = root.GetProperty("linkageBoundary");
        boundary.GetProperty("claim").GetString().Should().Be("auditable metadata linkage only");
        boundary.GetProperty("canonicalInventory").GetString().Should().Contain("not rewritten");
        boundary.GetProperty("captureArtifacts").GetString().Should().Contain("disposable");
        boundary.GetProperty("captureArtifacts").GetString().Should().Contain("not tracked");
        boundary.GetProperty("independentCheck").GetString().Should().Contain("cannot independently inspect untracked pixels");
    }

    private static void AssertFamilyTotals(JsonElement[] scenarios, string family, JsonElement totals, string prefix)
    {
        var familyRows = scenarios.Where(scenario => scenario.GetProperty("family").GetString() == family).ToArray();
        var before = familyRows.Sum(scenario => scenario.GetProperty("before").GetProperty("changedPixels").GetInt32());
        var after = familyRows.Sum(scenario => scenario.GetProperty("after").GetProperty("changedPixels").GetInt32());
        totals.GetProperty(prefix + "BeforeChangedPixels").GetInt32().Should().Be(before);
        totals.GetProperty(prefix + "AfterChangedPixels").GetInt32().Should().Be(after);
        totals.GetProperty(prefix + "ChangedPixelsReduction").GetInt32().Should().Be(before - after);
    }

    private static JsonDocument LoadEvidence()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var path = Path.Combine(root, EvidenceDirectory.Replace('/', Path.DirectorySeparatorChar), EvidenceFile);
        File.Exists(path).Should().BeTrue();
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool HasHexLength(string? value, int length) =>
        value is not null && value.Length == length && value.All(Uri.IsHexDigit);
}
