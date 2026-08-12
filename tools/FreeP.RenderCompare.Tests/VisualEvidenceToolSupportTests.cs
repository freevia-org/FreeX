using System.Text.Json;
using FreeP.App.Compositor;
using FreeP.VisualEvidence;

namespace FreeP.RenderCompare.Tests;

public sealed class VisualEvidenceToolSupportTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Sha256_and_manifest_reader_share_deterministic_file_handling()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-tool-support-");
        var manifestPath = Path.Combine(temporaryDirectory.Path, "manifest.json");
        File.WriteAllText(manifestPath, "{\"name\":\"paired\"}");

        VisualEvidenceToolSupport.Sha256(manifestPath)
            .Should().Be("c22e607ca0f823f50c42a6653d1e62f635885473a4b24b4281479ad970aefc8a");
        VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
                manifestPath,
                JsonOptions,
                "missing",
                "invalid")
            .Should().Be(new ManifestStub("paired"));
    }

    [Fact]
    public void Manifest_reader_preserves_missing_and_null_manifest_diagnostics()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-tool-support-");
        var missingPath = Path.Combine(temporaryDirectory.Path, "missing.json");

        Action readMissing = () => VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
            missingPath,
            JsonOptions,
            "manifest missing",
            "manifest invalid");

        readMissing.Should().Throw<FileNotFoundException>()
            .WithMessage("manifest missing");

        var nullPath = Path.Combine(temporaryDirectory.Path, "null.json");
        File.WriteAllText(nullPath, "null");
        Action readNull = () => VisualEvidenceToolSupport.ReadManifest<ManifestStub>(
            nullPath,
            JsonOptions,
            "manifest missing",
            "manifest invalid");

        readNull.Should().Throw<InvalidDataException>()
            .WithMessage("manifest invalid");
    }

    [Fact]
    public void Capture_routes_parse_requests_with_exact_existing_diagnostics()
    {
        var knownScenarios = new[] { "startup.slide-pane.seeded" };

        FreePVisualEvidenceCaptureOrchestration.ParseRequest(
                ["--unrelated"],
                FreePVisualEvidenceRoutes.DialogPane,
                knownScenarios)
            .Should().Be(new VisualEvidenceCaptureRequest(false, null, null, null));

        FreePVisualEvidenceCaptureOrchestration.ParseRequest(
                [FreePVisualEvidenceRoutes.DialogPane.OutputArgument],
                FreePVisualEvidenceRoutes.DialogPane,
                knownScenarios)
            .Error.Should().Be("--dialog-pane-visual-evidence-output requires an output directory.");

        FreePVisualEvidenceCaptureOrchestration.ParseRequest(
                [
                    FreePVisualEvidenceRoutes.DialogPane.OutputArgument,
                    ".",
                    FreePVisualEvidenceRoutes.DialogPane.ScenarioArgument,
                ],
                FreePVisualEvidenceRoutes.DialogPane,
                knownScenarios)
            .Error.Should().Be("--dialog-pane-visual-evidence-scenario requires a scenario id.");

        FreePVisualEvidenceCaptureOrchestration.ParseRequest(
                [
                    FreePVisualEvidenceRoutes.WholeWindow.OutputArgument,
                    ".",
                    FreePVisualEvidenceRoutes.WholeWindow.ScenarioArgument,
                    "unknown",
                ],
                FreePVisualEvidenceRoutes.WholeWindow,
                knownScenarios)
            .Error.Should().Be("Unknown whole-window visual evidence scenario: unknown");
    }

    [Fact]
    public void Output_and_process_plans_preserve_routes_filenames_and_wait_policy()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-plan-");
        var dialogHost = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            FreePVisualEvidenceRoutes.DialogPane);
        var dialogScenario = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            "review.comments-pane.seeded",
            FreePVisualEvidenceRoutes.DialogPane);
        var wholeScenario = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
            "startup.slide",
            FreePVisualEvidenceRoutes.WholeWindow);

        dialogHost.ManifestPath.Should().Be(Path.Combine(temporaryDirectory.Path, "wpf", "manifest.json"));
        dialogHost.ProgressPath.Should().Be(Path.Combine(temporaryDirectory.Path, "wpf", "capture-progress.log"));
        dialogScenario.ImageRelativePath.Should().Be("wpf/review.comments-pane.seeded.png");
        dialogScenario.ComparisonImageRelativePath.Should().Be("wpf/targets/review.comments-pane.seeded.png");
        wholeScenario.FullImageRelativePath.Should().Be("avalonia/full/startup.slide.png");
        wholeScenario.ClientImageRelativePath.Should().Be("avalonia/client/startup.slide.png");

        var executable = Path.Combine(temporaryDirectory.Path, "FreeP host.exe");
        var scenarioRoot = Path.Combine(temporaryDirectory.Path, "scenario root");
        var process = FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(
            executable,
            scenarioRoot,
            FreePVisualEvidenceRoutes.DialogPane,
            "review.comments-pane.seeded",
            TimeSpan.FromSeconds(45),
            "exact process tree");

        process.WorkingDirectory.Should().Be(temporaryDirectory.Path);
        process.Arguments.Should().Be(
            $"\"--dialog-pane-visual-evidence-output\" \"{scenarioRoot}\" " +
            "\"--dialog-pane-visual-evidence-scenario\" \"review.comments-pane.seeded\"");
        process.TimeoutMilliseconds.Should().Be(45_000);
        process.TimedOutProcessTreeDescription.Should().Be("exact process tree");
    }

    [Fact]
    public void Capture_text_helpers_preserve_artifact_names_and_semantic_labels()
    {
        var currentScenarioIds = DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.Id)
            .Concat(WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id));
        foreach (var scenarioId in currentScenarioIds)
        {
            FreePVisualEvidenceCaptureOrchestration.ToSafeFileName(scenarioId)
                .Should().Be(scenarioId);
        }

        FreePVisualEvidenceCaptureOrchestration.ToSafeFileName("review/comments:pane")
            .Should().Be("review-comments-pane");

        FreePVisualEvidenceCaptureOrchestration.NormalizeLabel("  _Apply:  ", "ignored")
            .Should().Be("Apply");
        FreePVisualEvidenceCaptureOrchestration.NormalizeLabel("  ", "  _Apply to All:  ")
            .Should().Be("Apply to All");
        FreePVisualEvidenceCaptureOrchestration.NormalizeLabel(null)
            .Should().BeEmpty();

        FreePVisualEvidenceCaptureOrchestration.SemanticActionId("+ Add slide")
            .Should().Be("add-add-slide");
        FreePVisualEvidenceCaptureOrchestration.SemanticActionId("- Remove slide")
            .Should().Be("remove-remove-slide");
        FreePVisualEvidenceCaptureOrchestration.SemanticActionId("Apply to All")
            .Should().Be("apply-to-all");
    }

    [Fact]
    public void Scenario_manifest_validation_and_declared_path_handoff_are_shared()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-manifest-");
        var hostPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            FreePVisualEvidenceRoutes.DialogPane);
        hostPlan.EnsureDirectories();
        var scenario = new ScenarioCapture("review.comments-pane.seeded", "wpf/review.comments-pane.seeded.png");
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            hostPlan.ManifestPath,
            new ScenarioManifest([scenario]),
            FreePVisualEvidenceCaptureOrchestration.HostManifestJsonOptions);

        var result = FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<ScenarioManifest, ScenarioCapture>(
            hostPlan.ManifestPath,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
            scenario.ScenarioId,
            manifest => manifest.Captures,
            capture => capture.ScenarioId);

        result.Status.Should().Be(VisualEvidenceScenarioManifestStatus.Ready);
        result.Capture.Should().Be(scenario);
        FreePVisualEvidenceCaptureOrchestration.ResolveDeclaredPath(
                temporaryDirectory.Path,
                scenario.ImagePath)
            .Should().Be(Path.Combine(temporaryDirectory.Path, "wpf", "review.comments-pane.seeded.png"));
    }

    [Fact]
    public async Task Scenario_runner_preserves_order_progress_and_blocked_capture_projection()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-runner-");
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            FreePVisualEvidenceRoutes.DialogPane);
        var visited = new List<string>();
        var failures = new List<string>();
        var scenarios = new[] { new RunnerScenario("first"), new RunnerScenario("second"), new RunnerScenario("third") };

        var run = await FreePVisualEvidenceCaptureOrchestration.RunScenariosAsync(
            scenarios,
            scenarioId: null,
            scenario => scenario.Id,
            outputPlan,
            logProgress: true,
            scenario =>
            {
                visited.Add(scenario.Id);
                return scenario.Id == "second"
                    ? Task.FromException<RunnerCapture>(new InvalidOperationException("expected failure"))
                    : Task.FromResult(new RunnerCapture(scenario.Id, "complete"));
            },
            createBlockedCapture: (scenario, _) => new RunnerCapture(scenario.Id, "blocked"),
            createLimitation: (_, _) => null,
            reportFailure: (scenario, exception) => failures.Add($"{scenario.Id}: {exception.Message}"));

        visited.Should().Equal("first", "second", "third");
        run.Scenarios.Select(scenario => scenario.Id).Should().Equal("first", "second", "third");
        run.Captures.Should().Equal(
            new RunnerCapture("first", "complete"),
            new RunnerCapture("second", "blocked"),
            new RunnerCapture("third", "complete"));
        run.Limitations.Should().BeEmpty();
        failures.Should().Equal("second: expected failure");

        var progress = File.ReadAllText(outputPlan.ProgressPath!);
        progress.IndexOf("start first", StringComparison.Ordinal).Should().BeLessThan(
            progress.IndexOf("complete first", StringComparison.Ordinal));
        progress.IndexOf("complete first", StringComparison.Ordinal).Should().BeLessThan(
            progress.IndexOf("start second", StringComparison.Ordinal));
        progress.IndexOf("failed second", StringComparison.Ordinal).Should().BeLessThan(
            progress.IndexOf("start third", StringComparison.Ordinal));

        FreePVisualEvidenceCaptureOrchestration.FinalizeHostRun(
                outputPlan,
                run,
                (captures, limitations) => new RunnerManifest(captures, limitations))
            .Should().Be(0);
        FreePVisualEvidenceCaptureOrchestration.ReadManifest<RunnerManifest>(
                outputPlan.ManifestPath,
                FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
                "missing",
                "invalid")
            .Captures.Should().Equal(run.Captures);
    }

    [Fact]
    public async Task Scenario_runner_records_limitations_and_fails_when_capture_is_omitted()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-visual-evidence-runner-");
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            temporaryDirectory.Path,
            FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
            FreePVisualEvidenceRoutes.WholeWindow);
        var scenarios = new[] { new RunnerScenario("first"), new RunnerScenario("second") };

        var run = await FreePVisualEvidenceCaptureOrchestration.RunScenariosAsync(
            scenarios,
            scenarioId: null,
            scenario => scenario.Id,
            outputPlan,
            logProgress: false,
            scenario => scenario.Id == "second"
                ? Task.FromException<RunnerCapture>(new InvalidOperationException("expected failure"))
                : Task.FromResult(new RunnerCapture(scenario.Id, "complete")),
            createBlockedCapture: (_, _) => null,
            createLimitation: (scenario, exception) => $"{scenario.Id}: {exception.Message}");

        run.Captures.Should().Equal(new RunnerCapture("first", "complete"));
        run.Limitations.Should().Equal("second: expected failure");
        outputPlan.ProgressPath.Should().BeNull();
        FreePVisualEvidenceCaptureOrchestration.FinalizeHostRun(
                outputPlan,
                run,
                (captures, limitations) => new RunnerManifest(captures, limitations))
            .Should().Be(1);
    }

    private sealed record ManifestStub(string Name);
    private sealed record ScenarioManifest(IReadOnlyList<ScenarioCapture> Captures);
    private sealed record ScenarioCapture(string ScenarioId, string ImagePath);
    private sealed record RunnerScenario(string Id);
    private sealed record RunnerCapture(string ScenarioId, string Status);
    private sealed record RunnerManifest(
        IReadOnlyList<RunnerCapture> Captures,
        IReadOnlyList<string> Limitations);
}
