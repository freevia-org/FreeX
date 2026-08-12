using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWRecoveryWorkflowTests
{
    [Theory]
    [InlineData(
        FreeWRecoveryPromptMode.Startup,
        "FreeW found unsaved changes to Alpha from a previous session (3 unsaved documents found). Recover this one?",
        "FreeW found unsaved changes to Gamma from a previous session. Recover them?")]
    [InlineData(
        FreeWRecoveryPromptMode.StartupQuotedDisplayName,
        "FreeW found unsaved changes to \"Alpha\" from a previous session (3 unsaved documents found). Recover this one?",
        "FreeW found unsaved changes to \"Gamma\" from a previous session. Recover them?")]
    [InlineData(
        FreeWRecoveryPromptMode.Manual,
        "Recover unsaved changes to Alpha? (3 unsaved documents found.)",
        "Recover unsaved changes to Gamma?")]
    public async Task RunAsync_PreservesPromptModeAndUsesCurrentWindowOnlyForFirstAcceptance(
        FreeWRecoveryPromptMode promptMode,
        string firstPrompt,
        string lastPrompt)
    {
        var recoveries = new[] { Recovery("Alpha"), Recovery("Beta"), Recovery("Gamma") };
        var responses = new Queue<bool>([false, true, true]);
        var offers = new List<FreeWRecoveryOffer>();
        var completions = new List<(string DisplayName, bool UseCurrentWindow)>();

        var result = await FreeWRecoveryWorkflow.RunAsync(
            recoveries,
            promptMode,
            offer =>
            {
                offers.Add(offer);
                return new ValueTask<bool>(responses.Dequeue());
            },
            (recovery, useCurrentWindow) =>
            {
                completions.Add((recovery.DisplayName, useCurrentWindow));
                return new ValueTask<bool>(recovery.DisplayName == "Gamma");
            });

        result.AnyAccepted.Should().BeTrue();
        result.AnyRecovered.Should().BeTrue();
        offers.Select(offer => offer.RemainingCount).Should().Equal(3, 2, 1);
        offers[0].Prompt.Should().Be(firstPrompt);
        offers[2].Prompt.Should().Be(lastPrompt);
        completions.Should().Equal(("Beta", true), ("Gamma", false));
    }

    [Fact]
    public async Task RunAsync_WithNoCandidates_DoesNotInvokeNativePorts()
    {
        var promptCalls = 0;
        var completionCalls = 0;

        var result = await FreeWRecoveryWorkflow.RunAsync(
            [],
            FreeWRecoveryPromptMode.Startup,
            _ =>
            {
                promptCalls++;
                return new ValueTask<bool>(true);
            },
            (_, _) =>
            {
                completionCalls++;
                return new ValueTask<bool>(true);
            });

        result.AnyAccepted.Should().BeFalse();
        result.AnyRecovered.Should().BeFalse();
        promptCalls.Should().Be(0);
        completionCalls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenAcceptedRecoveryFails_ReportsAcceptedWithoutRecovered()
    {
        var result = await FreeWRecoveryWorkflow.RunAsync(
            [Recovery("Alpha")],
            FreeWRecoveryPromptMode.Manual,
            _ => new ValueTask<bool>(true),
            (_, _) => new ValueTask<bool>(false));

        result.AnyAccepted.Should().BeTrue();
        result.AnyRecovered.Should().BeFalse();
    }

    private static AutosaveRecoveryPlan Recovery(string displayName) =>
        new(
            new AutosaveRecoveryCandidate(
                displayName + ".docx",
                displayName + ".sidecar.json",
                new AutosaveSidecar { DisplayName = displayName }),
            displayName);
}
