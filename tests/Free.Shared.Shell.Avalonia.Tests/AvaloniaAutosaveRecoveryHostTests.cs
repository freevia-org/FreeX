using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class AvaloniaAutosaveRecoveryHostTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    private static readonly AvaloniaAutosaveRecoveryMessages Messages = new(
        "Recovery",
        "Nothing to recover",
        "Recovery failed: {0}");

    [Fact]
    public Task OfferStartup_IsBestEffortAndSilentWhenPlanningFails() => OnUiThread(async owner =>
    {
        var result = await AvaloniaAutosaveRecoveryHost.OfferStartupAsync<TestPlan, string>(
            owner,
            currentWindowHasExplicitDocument: static () => false,
            planRecoveries: static () => throw new InvalidOperationException("broken store"),
            createOffer: static (_, _) => "offer",
            promptAsync: static _ => ValueTask.FromResult(true),
            recoverInCurrentWindow: static _ => true,
            recoverInNewWindowAsync: static _ => Task.FromResult(true),
            completeRecoveryResult: static (_, _, _) => { });

        result.Should().BeFalse();
    });

    [Fact]
    public Task OfferStartup_RoutesEveryAcceptedCandidateAwayFromAnExplicitDocument() => OnUiThread(async owner =>
    {
        var routes = new List<string>();
        var explicitDocumentChecks = 0;
        var plans = CreatePlans("first", "second");

        var result = await AvaloniaAutosaveRecoveryHost.OfferStartupAsync(
            owner,
            currentWindowHasExplicitDocument: () =>
            {
                explicitDocumentChecks++;
                return true;
            },
            planRecoveries: () => plans,
            createOffer: static (plan, remaining) => $"{plan.DisplayName}:{remaining}",
            promptAsync: static _ => ValueTask.FromResult(true),
            recoverInCurrentWindow: _ =>
            {
                routes.Add("current");
                return true;
            },
            recoverInNewWindowAsync: _ =>
            {
                routes.Add("new");
                return Task.FromResult(true);
            },
            completeRecoveryResult: static (_, _, _) => { });

        result.Should().BeTrue();
        explicitDocumentChecks.Should().Be(1);
        routes.Should().Equal("new", "new");
    });

    [Fact]
    public Task OfferStartup_FreshWindowUsesCurrentWindowForOnlyTheFirstAcceptedCandidate() => OnUiThread(async owner =>
    {
        var answers = new Queue<bool>([false, true, true]);
        var routes = new List<string>();

        var result = await AvaloniaAutosaveRecoveryHost.OfferStartupAsync(
            owner,
            currentWindowHasExplicitDocument: static () => false,
            planRecoveries: () => CreatePlans("declined", "accepted", "next"),
            createOffer: static (plan, _) => plan.DisplayName,
            promptAsync: _ => ValueTask.FromResult(answers.Dequeue()),
            recoverInCurrentWindow: _ =>
            {
                routes.Add("current");
                return true;
            },
            recoverInNewWindowAsync: _ =>
            {
                routes.Add("new");
                return Task.FromResult(true);
            },
            completeRecoveryResult: static (_, _, _) => { });

        result.Should().BeTrue();
        routes.Should().Equal("current", "new");
    });

    [Fact]
    public Task RecoverManually_ShowsTheLocalizedEmptyState() => OnUiThread(async owner =>
    {
        var dialogs = new RecordingDialogs();

        var result = await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync<TestPlan, string>(
            owner,
            Messages,
            planRecoveries: static () => [],
            createOffer: static (_, _) => "offer",
            promptAsync: static _ => ValueTask.FromResult(true),
            confirmDiscardOrSaveAsync: null,
            recoverInCurrentWindow: static _ => true,
            recoverInNewWindowAsync: static _ => Task.FromResult(true),
            completeRecoveryResult: static (_, _, _) => { },
            dialogs);

        result.Should().BeFalse();
        dialogs.Events.Should().Equal("none:Nothing to recover|Recovery");
    });

    [Fact]
    public Task RecoverManually_DeclinedDirtyGateKeepsTheCandidateAndSkipsRestore() => OnUiThread(async owner =>
    {
        var completions = new List<(bool Accepted, bool Recovered)>();
        var currentRestoreCalls = 0;

        var result = await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync(
            owner,
            Messages,
            planRecoveries: () => CreatePlans("draft"),
            createOffer: static (plan, _) => plan.DisplayName,
            promptAsync: static _ => ValueTask.FromResult(true),
            confirmDiscardOrSaveAsync: static () => Task.FromResult(false),
            recoverInCurrentWindow: _ =>
            {
                currentRestoreCalls++;
                return true;
            },
            recoverInNewWindowAsync: static _ => Task.FromResult(true),
            completeRecoveryResult: (_, accepted, recovered) => completions.Add((accepted, recovered)),
            new RecordingDialogs());

        result.Should().BeFalse();
        currentRestoreCalls.Should().Be(0);
        completions.Should().Equal((false, false));
    });

    [Fact]
    public Task RecoverManually_GatesOnlyTheCurrentWindowAndRoutesLaterCandidatesToNewWindows() => OnUiThread(async owner =>
    {
        var gateCalls = 0;
        var routes = new List<string>();
        var completions = new List<(string Name, bool Accepted, bool Recovered)>();

        var result = await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync(
            owner,
            Messages,
            planRecoveries: () => CreatePlans("first", "second"),
            createOffer: static (plan, remaining) => $"{plan.DisplayName}:{remaining}",
            promptAsync: static _ => ValueTask.FromResult(true),
            confirmDiscardOrSaveAsync: () =>
            {
                gateCalls++;
                return Task.FromResult(true);
            },
            recoverInCurrentWindow: plan =>
            {
                routes.Add($"current:{plan.DisplayName}");
                return true;
            },
            recoverInNewWindowAsync: plan =>
            {
                routes.Add($"new:{plan.DisplayName}");
                return Task.FromResult(true);
            },
            completeRecoveryResult: (plan, accepted, recovered) =>
                completions.Add((plan.DisplayName, accepted, recovered)),
            new RecordingDialogs());

        result.Should().BeTrue();
        gateCalls.Should().Be(1);
        routes.Should().Equal("current:first", "new:second");
        completions.Should().Equal(("second", true, true));
    });

    [Fact]
    public Task RecoverManually_FormatsAndDisplaysFailures() => OnUiThread(async owner =>
    {
        var dialogs = new RecordingDialogs();

        var result = await AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync<TestPlan, string>(
            owner,
            Messages,
            planRecoveries: static () => throw new InvalidOperationException("broken store"),
            createOffer: static (_, _) => "offer",
            promptAsync: static _ => ValueTask.FromResult(true),
            confirmDiscardOrSaveAsync: null,
            recoverInCurrentWindow: static _ => true,
            recoverInNewWindowAsync: static _ => Task.FromResult(true),
            completeRecoveryResult: static (_, _, _) => { },
            dialogs);

        result.Should().BeFalse();
        dialogs.Events.Should().Equal("failure:Recovery failed: broken store|Recovery");
    });

    [Fact]
    public void FreePAndFreeWAdaptersDelegateRecoveryHostPolicyToTheSharedRuntime()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("AvaloniaAutosaveRecoveryHost.OfferStartupAsync(")
                .And.Contain("AvaloniaAutosaveRecoveryHost.RecoverManuallyAsync(")
                .And.NotContain("RecoveryWorkflow.RunAsync(")
                .And.NotContain("RecoverIntoCurrentWindowGatedAsync");
        }
    }

    private static Task OnUiThread(Func<Window, Task> action) => Session.Dispatch(async () =>
    {
        var owner = new Window();
        await action(owner);
        return true;
    }, CancellationToken.None);

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

    private sealed class RecordingDialogs : IAvaloniaAutosaveRecoveryDialogs
    {
        public List<string> Events { get; } = [];

        public Task ShowNoCandidatesAsync(Window owner, string message, string title)
        {
            Events.Add($"none:{message}|{title}");
            return Task.CompletedTask;
        }

        public Task ShowFailureAsync(Window owner, string message, string title)
        {
            Events.Add($"failure:{message}|{title}");
            return Task.CompletedTask;
        }
    }
}
