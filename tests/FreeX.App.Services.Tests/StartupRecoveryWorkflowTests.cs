using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StartupRecoveryWorkflowTests
{
    [Fact]
    public async Task RunAsync_owns_offer_target_restore_and_delete_order()
    {
        var candidates = new[]
        {
            CreateCandidate("declined", "Declined"),
            CreateCandidate("primary", "Primary"),
            CreateCandidate("additional", "Additional")
        };
        var decisions = new Queue<bool>([false, true, true]);
        var events = new List<string>();

        var accepted = await StartupRecoveryWorkflow.RunAsync(
            candidates,
            CreateHost(
                events,
                offer => decisions.Dequeue(),
                execute: operation => operation()));

        accepted.Should().BeTrue();
        events.Should().Equal(
            "offer:declined",
            "delete:declined",
            "offer:primary",
            "execute",
            "restore:primary:primary",
            "delete:primary",
            "offer:additional",
            "execute",
            "create:additional-1",
            "restore:additional-1:additional",
            "delete:additional");
    }

    [Fact]
    public async Task RunAsync_supports_deferred_native_execution_without_early_deletion()
    {
        var candidates = new[]
        {
            CreateCandidate("first", "First"),
            CreateCandidate("second", "Second")
        };
        var events = new List<string>();
        var pending = new List<Func<Task>>();

        var accepted = await StartupRecoveryWorkflow.RunAsync(
            candidates,
            CreateHost(
                events,
                _ => true,
                execute: operation =>
                {
                    pending.Add(operation);
                    return Task.CompletedTask;
                }));

        accepted.Should().BeTrue();
        pending.Should().HaveCount(2);
        events.Should().Equal("offer:first", "execute", "offer:second", "execute");

        await pending[0]();
        await pending[1]();

        events.Should().Equal(
            "offer:first",
            "execute",
            "offer:second",
            "execute",
            "restore:primary:first",
            "delete:first",
            "create:additional-1",
            "restore:additional-1:second",
            "delete:second");
    }

    [Fact]
    public async Task RunAsync_retires_failed_restore_and_continues_with_later_offers()
    {
        var candidates = new[]
        {
            CreateCandidate("bad", "Bad"),
            CreateCandidate("good", "Good")
        };
        var events = new List<string>();
        var host = CreateHost(events, _ => true, operation => operation());
        host = host with
        {
            RestoreAsync = (target, candidate, _) =>
            {
                events.Add($"restore:{target}:{CandidateId(candidate)}");
                return CandidateId(candidate) == "bad"
                    ? Task.FromException(new InvalidDataException("bad snapshot"))
                    : Task.CompletedTask;
            }
        };

        var accepted = await StartupRecoveryWorkflow.RunAsync(candidates, host);

        accepted.Should().BeTrue();
        events.Should().ContainInOrder(
            "restore:primary:bad",
            "delete:bad",
            "create:additional-1",
            "restore:additional-1:good",
            "delete:good");
    }

    [Fact]
    public void FreeX_apps_keep_only_native_recovery_adapters()
    {
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "App.xaml.cs"));
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));

        foreach (var source in new[] { wpfSource, avaloniaSource })
        {
            source.Should().Contain("new StartupRecoveryWorkflowHost<MainWindow>(");
            source.Should().Contain("StartupRecoveryWorkflow.RunAsync(");
            source.Should().NotContain("foreach (var offer in offers)");
            source.Should().NotContain("AutosaveRecoveryOfferPlanner.PrepareOffers(");
        }

        wpfSource.Should().Contain("mainWindow.Dispatcher.BeginInvoke");
        wpfSource.Should().Contain("AskStartupYesNo(");
        avaloniaSource.Should().Contain("ShowRecoveryPromptAsync(");
    }

    [Fact]
    public void Portable_startup_workflows_do_not_depend_on_native_UI_types()
    {
        var recoverySource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "StartupRecoveryWorkflow.cs"));
        var cancellationSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "FileOperationCancellationSession.cs"));

        foreach (var source in new[] { recoverySource, cancellationSource })
        {
            source.Should().NotContain("System.Windows");
            source.Should().NotContain("Avalonia.");
            source.Should().NotContain("MainWindow");
        }
    }

    private static StartupRecoveryWorkflowHost<string> CreateHost(
        List<string> events,
        Func<AutosaveRecoveryOfferPlan, bool> offer,
        Func<Func<Task>, Task> execute)
    {
        var additionalTargetIndex = 0;
        return new StartupRecoveryWorkflowHost<string>(
            PrimaryTarget: "primary",
            OfferAsync: (plan, _) =>
            {
                events.Add("offer:" + CandidateId(plan.Candidate));
                return ValueTask.FromResult(offer(plan));
            },
            CreateAdditionalTargetAsync: _ =>
            {
                var target = "additional-" + ++additionalTargetIndex;
                events.Add("create:" + target);
                return ValueTask.FromResult(target);
            },
            RestoreAsync: (target, candidate, _) =>
            {
                events.Add($"restore:{target}:{CandidateId(candidate)}");
                return Task.CompletedTask;
            },
            ExecuteRestoreAsync: async (operation, _) =>
            {
                events.Add("execute");
                await execute(operation);
            },
            DeleteCandidate: candidate => events.Add("delete:" + CandidateId(candidate)));
    }

    private static AutosaveRecoveryCandidate CreateCandidate(string id, string displayName)
    {
        var snapshotPath = Path.Combine("recovery-tests", $"recovery-42-launch-{id}.fxl");
        return new AutosaveRecoveryCandidate(
            snapshotPath,
            snapshotPath + ".sidecar.json",
            new AutosaveSidecar
            {
                DisplayName = displayName,
                TimestampUtc = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero).ToString("O"),
                SnapshotId = "recovery-42-launch-" + id,
                DocumentId = "document-" + id
            });
    }

    private static string CandidateId(AutosaveRecoveryCandidate candidate) =>
        candidate.Sidecar.SnapshotId!["recovery-42-launch-".Length..];
}
