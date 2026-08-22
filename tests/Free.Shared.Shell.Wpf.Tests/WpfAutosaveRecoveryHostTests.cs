using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfAutosaveRecoveryHostTests
{
    private static readonly WpfAutosaveRecoveryMessages Messages = new(
        "Recovery",
        "Nothing to recover",
        "Recovery failed: {0}");

    [Fact]
    public void OfferStartup_IsBestEffortAndSilentWhenPlanningFails()
    {
        var dialogs = new RecordingDialogs();

        var result = WpfAutosaveRecoveryHost.OfferStartup<TestPlan>(
            owner: null,
            Messages,
            currentWindowHasExplicitDocument: () => false,
            planRecoveries: () => throw new InvalidOperationException("broken store"),
            createPrompt: static (_, _) => "offer",
            completeRecovery: static (_, _) => true,
            dialogs);

        result.Should().BeFalse();
        dialogs.Events.Should().BeEmpty();
    }

    [Fact]
    public void OfferStartup_RoutesAllAcceptedCandidatesAwayFromAnExplicitDocument()
    {
        var plans = CreatePlans("first", "second");
        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([true, true]) };
        var routes = new List<bool>();
        var explicitDocumentChecks = 0;

        var result = WpfAutosaveRecoveryHost.OfferStartup(
            owner: null,
            Messages,
            currentWindowHasExplicitDocument: () =>
            {
                explicitDocumentChecks++;
                return true;
            },
            planRecoveries: () => plans,
            createPrompt: static (plan, remaining) => $"{plan.DisplayName}:{remaining}",
            completeRecovery: (_, useCurrentWindow) =>
            {
                routes.Add(useCurrentWindow);
                return true;
            },
            dialogs);

        result.Should().BeTrue("startup reports whether any offer was accepted");
        explicitDocumentChecks.Should().Be(1);
        routes.Should().Equal(false, false);
        dialogs.Events.Should().Equal(
            "startup:first:2|Recovery",
            "startup:second:1|Recovery");
    }

    [Fact]
    public void OfferStartup_FreshWindowUsesCurrentWindowOnlyForFirstAcceptedCandidate()
    {
        var plans = CreatePlans("declined", "accepted", "next");
        var dialogs = new RecordingDialogs { StartupAnswers = new Queue<bool>([false, true, true]) };
        var routes = new List<bool>();

        var result = WpfAutosaveRecoveryHost.OfferStartup(
            owner: null,
            Messages,
            currentWindowHasExplicitDocument: () => false,
            planRecoveries: () => plans,
            createPrompt: static (plan, _) => plan.DisplayName,
            completeRecovery: (_, useCurrentWindow) =>
            {
                routes.Add(useCurrentWindow);
                return false;
            },
            dialogs);

        result.Should().BeTrue("acceptance, not restore success, is the startup projection");
        routes.Should().Equal(true, false);
    }

    [Fact]
    public void RecoverManually_ShowsLocalizedInformationWhenNoCandidatesExist()
    {
        var dialogs = new RecordingDialogs();

        var result = WpfAutosaveRecoveryHost.RecoverManually<TestPlan>(
            owner: null,
            Messages,
            planRecoveries: static () => [],
            createPrompt: static (_, _) => "offer",
            completeRecovery: static (_, _) => true,
            dialogs);

        result.Should().BeFalse();
        dialogs.Events.Should().Equal("none:Nothing to recover|Recovery");
    }

    [Fact]
    public void RecoverManually_UsesOkCancelSemanticsAndProjectsRecoverySuccess()
    {
        var plans = CreatePlans("first", "second");
        var dialogs = new RecordingDialogs { ManualAnswers = new Queue<bool>([false, true]) };
        var routes = new List<bool>();

        var result = WpfAutosaveRecoveryHost.RecoverManually(
            owner: null,
            Messages,
            planRecoveries: () => plans,
            createPrompt: static (plan, remaining) => $"{plan.DisplayName}:{remaining}",
            completeRecovery: (_, useCurrentWindow) =>
            {
                routes.Add(useCurrentWindow);
                return true;
            },
            dialogs);

        result.Should().BeTrue("manual recovery reports actual restore success");
        routes.Should().Equal(true);
        dialogs.Events.Should().Equal(
            "manual:first:2|Recovery",
            "manual:second:1|Recovery");
    }

    [Fact]
    public void RecoverManually_DisplaysFormattedFailure()
    {
        var dialogs = new RecordingDialogs();

        var result = WpfAutosaveRecoveryHost.RecoverManually<TestPlan>(
            owner: null,
            Messages,
            planRecoveries: () => throw new InvalidOperationException("broken store"),
            createPrompt: static (_, _) => "offer",
            completeRecovery: static (_, _) => true,
            dialogs);

        result.Should().BeFalse();
        dialogs.Events.Should().Equal("failure:Recovery failed: broken store|Recovery");
    }

    private static IReadOnlyList<TestPlan> CreatePlans(params string[] displayNames) =>
        displayNames.Select((displayName, index) => new TestPlan(
            new AutosaveRecoveryCandidate(
                $"snapshot-{index}",
                $"sidecar-{index}",
                new AutosaveSidecar()),
            displayName)).ToArray();

    private sealed record TestPlan(
        AutosaveRecoveryCandidate Candidate,
        string DisplayName) : IAutosaveRecoveryPlan;

    private sealed class RecordingDialogs : IWpfAutosaveRecoveryDialogs
    {
        public Queue<bool> StartupAnswers { get; init; } = new();

        public Queue<bool> ManualAnswers { get; init; } = new();

        public List<string> Events { get; } = [];

        public bool AskStartup(Window? owner, string prompt, string title)
        {
            Events.Add($"startup:{prompt}|{title}");
            return StartupAnswers.Dequeue();
        }

        public bool AskManual(Window? owner, string prompt, string title)
        {
            Events.Add($"manual:{prompt}|{title}");
            return ManualAnswers.Dequeue();
        }

        public void ShowNoCandidates(Window? owner, string message, string title) =>
            Events.Add($"none:{message}|{title}");

        public void ShowFailure(Window? owner, string message, string title) =>
            Events.Add($"failure:{message}|{title}");
    }
}
