namespace Free.Shared.AppServices.Tests;

public sealed class AutosaveRecoveryWorkflowCoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "SharedAutosaveRecoveryWorkflowTests_" + Guid.NewGuid().ToString("N"));

    public AutosaveRecoveryWorkflowCoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task RunAsync_SequencesOffersAndUsesCurrentWindowOnlyForFirstAcceptance()
    {
        var recoveries = new[] { Plan("Alpha"), Plan("Beta"), Plan("Gamma") };
        var responses = new Queue<bool>([false, true, true]);
        var remainingCounts = new List<int>();
        var completions = new List<(string DisplayName, bool UseCurrentWindow)>();

        var result = await AutosaveRecoveryWorkflow.RunAsync(
            recoveries,
            AutosaveRecoveryPromptMode.Manual,
            (recovery, remainingCount) => new TestOffer(recovery, remainingCount),
            offer =>
            {
                remainingCounts.Add(offer.RemainingCount);
                return new ValueTask<bool>(responses.Dequeue());
            },
            (recovery, useCurrentWindow) =>
            {
                completions.Add((recovery.DisplayName, useCurrentWindow));
                return new ValueTask<bool>(recovery.DisplayName == "Gamma");
            });

        result.Should().Be(new AutosaveRecoveryWorkflowResult(true, true));
        remainingCounts.Should().Equal(3, 2, 1);
        completions.Should().Equal(("Beta", true), ("Gamma", false));
    }

    [Theory]
    [InlineData(AutosaveRecoveryPromptMode.Startup, false)]
    [InlineData(AutosaveRecoveryPromptMode.StartupQuotedDisplayName, false)]
    [InlineData(AutosaveRecoveryPromptMode.Manual, true)]
    public async Task RunAsync_DeclineRetentionFollowsPromptMode(
        AutosaveRecoveryPromptMode promptMode,
        bool candidateShouldRemain)
    {
        var plan = Plan(promptMode.ToString(), createFiles: true);

        await AutosaveRecoveryWorkflow.RunAsync(
            new[] { plan },
            promptMode,
            (recovery, remainingCount) => new TestOffer(recovery, remainingCount),
            _ => new ValueTask<bool>(false),
            (_, _) => throw new InvalidOperationException("declines must not restore"));

        File.Exists(plan.Candidate.SnapshotPath).Should().Be(candidateShouldRemain);
        File.Exists(plan.Candidate.SidecarPath).Should().Be(candidateShouldRemain);
    }

    [Theory]
    [InlineData(
        AutosaveRecoveryPromptMode.Startup,
        2,
        "Product found unsaved changes to Draft from a previous session (2 unsaved items found). Recover this one?")]
    [InlineData(
        AutosaveRecoveryPromptMode.StartupQuotedDisplayName,
        1,
        "Product found unsaved changes to \"Draft\" from a previous session. Recover them?")]
    [InlineData(
        AutosaveRecoveryPromptMode.Manual,
        2,
        "Recover unsaved changes to Draft? (2 unsaved items found.)")]
    public void PromptFormatter_UsesProductAndDocumentText(
        AutosaveRecoveryPromptMode promptMode,
        int remainingCount,
        string expected)
    {
        AutosaveRecoveryPromptFormatter.Format(
                "Draft",
                remainingCount,
                promptMode,
                new AutosaveRecoveryPromptText("Product", "items"))
            .Should().Be(expected);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private TestPlan Plan(string displayName, bool createFiles = false)
    {
        var snapshotPath = Path.Combine(_directory, displayName + ".fxl");
        var sidecarPath = Path.Combine(_directory, displayName + ".sidecar.json");
        var sidecar = new AutosaveSidecar { DisplayName = displayName };
        if (createFiles)
        {
            File.WriteAllText(snapshotPath, "snapshot");
            File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));
        }

        return new TestPlan(
            new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar),
            displayName);
    }

    private sealed record TestPlan(
        AutosaveRecoveryCandidate Candidate,
        string DisplayName) : IAutosaveRecoveryPlan;

    private sealed record TestOffer(TestPlan Recovery, int RemainingCount);
}
