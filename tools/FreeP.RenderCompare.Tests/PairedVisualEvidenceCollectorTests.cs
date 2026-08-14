using FreeP.VisualEvidence;

namespace FreeP.RenderCompare.Tests;

public sealed class PairedVisualEvidenceCollectorTests
{
    [Fact]
    public void CaptureHost_owns_scenario_process_manifest_limitation_and_artifact_flow()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-paired-collector-");
        var outputDirectory = Path.Combine(temporaryDirectory.Path, "evidence");
        var runRoot = Path.Combine(temporaryDirectory.Path, "runs");
        var executable = Path.Combine(temporaryDirectory.Path, "host.exe");
        File.WriteAllText(executable, string.Empty);
        var scenarios = new[] { new Scenario("first"), new Scenario("second") };
        var limitations = new List<string>();
        var plans = new List<(int TimeoutMilliseconds, string Description, string Arguments)>();
        var runIndex = 0;
        var profile = new PairedVisualEvidenceProfile<Scenario, Manifest, Capture>(
            FreePVisualEvidenceRoutes.DialogPane,
            scenarios,
            scenario => scenario.Id,
            manifest => manifest.Captures,
            capture => capture.ScenarioId,
            manifest => manifest.Limitations,
            "The host manifest contained no scenario capture.",
            "exact process tree",
            _ => { },
            context =>
            {
                PairedVisualEvidenceCollector.TryCopyArtifacts(
                    context.ScenarioRoot,
                    new PairedVisualEvidenceArtifact(
                        context.Capture.ImagePath,
                        context.FinalOutput.ImagePath!)).Should().BeTrue();
                return new(
                    context.Capture with { ImagePath = context.FinalOutput.ImageRelativePath! },
                    Array.Empty<string>());
            },
            (host, captures, hostLimitations) => new(host, captures, hostLimitations));

        var manifest = PairedVisualEvidenceCollector.CaptureHost(
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            executable,
            outputDirectory,
            runRoot,
            TimeSpan.FromSeconds(17),
            limitations,
            profile,
            plan =>
            {
                plans.Add((plan.TimeoutMilliseconds, plan.TimedOutProcessTreeDescription, plan.Arguments));
                var scenario = scenarios[runIndex++];
                var scenarioRoot = FreePVisualEvidenceCaptureOrchestration.CreateScenarioRunRoot(
                    runRoot,
                    FreePVisualEvidenceCaptureOrchestration.WpfHost,
                    scenario.Id);
                var output = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                    scenarioRoot,
                    FreePVisualEvidenceCaptureOrchestration.WpfHost,
                    scenario.Id,
                    FreePVisualEvidenceRoutes.DialogPane);
                Directory.CreateDirectory(Path.GetDirectoryName(output.HostManifestPath)!);
                if (scenario.Id == "first")
                {
                    File.WriteAllText(output.ImagePath!, "png");
                    FreePVisualEvidenceCaptureOrchestration.WriteManifest(
                        output.HostManifestPath,
                        new Manifest(
                            FreePVisualEvidenceCaptureOrchestration.WpfHost,
                            [new Capture(scenario.Id, output.ImageRelativePath!)],
                            ["host limitation"]),
                        FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
                }
                else
                {
                    FreePVisualEvidenceCaptureOrchestration.WriteManifest(
                        output.HostManifestPath,
                        new Manifest(FreePVisualEvidenceCaptureOrchestration.WpfHost, [], []),
                        FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
                }
                return $"run {scenario.Id}";
            });

        manifest.Captures.Should().Equal(new Capture("first", "wpf/first.png"));
        manifest.Limitations.Should().Equal("host limitation");
        File.ReadAllText(Path.Combine(outputDirectory, "wpf", "first.png")).Should().Be("png");
        limitations.Should().Equal(
            "wpf second: run second The host manifest contained no scenario capture.");
        plans.Select(plan => plan.TimeoutMilliseconds).Should().Equal(17_000, 17_000);
        plans.Select(plan => plan.Description).Should()
            .Equal("exact process tree", "exact process tree");
        plans[0].Arguments.Should().Contain("\"first\"");
        plans[1].Arguments.Should().Contain("\"second\"");
        File.Exists(Path.Combine(outputDirectory, "wpf", "manifest.json")).Should().BeTrue();
    }

    [Fact]
    public void ValidateExecutable_returns_full_path_and_preserves_missing_diagnostic()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-paired-collector-");
        var executable = Path.Combine(temporaryDirectory.Path, "host.exe");
        File.WriteAllText(executable, string.Empty);

        PairedVisualEvidenceCollector.ValidateExecutable(executable, "missing")
            .Should().Be(Path.GetFullPath(executable));

        var missing = Path.Combine(temporaryDirectory.Path, "missing.exe");
        Action validate = () => PairedVisualEvidenceCollector.ValidateExecutable(missing, "host missing");
        validate.Should().Throw<FileNotFoundException>()
            .WithMessage("host missing")
            .Where(exception => exception.FileName == Path.GetFullPath(missing));
    }

    private sealed record Scenario(string Id);
    private sealed record Capture(string ScenarioId, string ImagePath);
    private sealed record Manifest(
        string Host,
        IReadOnlyList<Capture> Captures,
        IReadOnlyList<string> Limitations);
}
