using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class StartupFileOpenPlannerTests
{
    [Fact]
    public void Plan_routes_every_existing_argument_without_replacing_prior_workbooks()
    {
        var arguments = new[] { @"C:\work\one.xlsx", @"C:\work\two.xlsx", @"C:\work\three.xlsx" };

        var plan = StartupFileOpenPlanner.Plan(arguments, recoveryAccepted: false, fileExists: _ => true);

        plan.Entries.Should().Equal(
            new StartupFileOpenEntry(arguments[0], OpenInNewWindow: false),
            new StartupFileOpenEntry(arguments[1], OpenInNewWindow: true),
            new StartupFileOpenEntry(arguments[2], OpenInNewWindow: true));
        plan.FirstMissingPath.Should().BeNull();
        plan.HasOpenableFiles.Should().BeTrue();
        plan.ShouldReportMissingPath.Should().BeFalse();
        plan.ShouldPrewarm.Should().BeFalse();
    }

    [Fact]
    public void Plan_collapses_a_duplicate_command_line_argument_into_one_window()
    {
        // Windows delivers a multi-selected-and-dragged duplicate ("FreeX.exe A.xlsx A.xlsx") as
        // one process launch with the path repeated in argv. The second occurrence must not spawn
        // an independent second window on the same file -- two windows editing the same file with
        // separate dirty/undo state means whichever saves last silently overwrites the other.
        var plan = StartupFileOpenPlanner.Plan(
            [@"C:\work\one.xlsx", @"C:\work\one.xlsx"],
            recoveryAccepted: false,
            fileExists: _ => true);

        plan.Entries.Should().ContainSingle().Which.Should().Be(
            new StartupFileOpenEntry(@"C:\work\one.xlsx", OpenInNewWindow: false));
    }

    [Fact]
    public void Plan_routes_all_files_to_new_windows_after_recovery()
    {
        var arguments = new[] { @"C:\work\one.xlsx", @"C:\work\two.xlsx" };

        var plan = StartupFileOpenPlanner.Plan(arguments, recoveryAccepted: true, fileExists: _ => true);

        plan.Entries.Should().OnlyContain(entry => entry.OpenInNewWindow);
        plan.ShouldPrewarm.Should().BeFalse();
    }

    [Fact]
    public void Plan_reports_only_the_first_missing_argument_when_nothing_can_open()
    {
        var arguments = new[] { @"C:\work\missing.xlsx", @"C:\work\also-missing.xlsx" };

        var plan = StartupFileOpenPlanner.Plan(arguments, recoveryAccepted: false, fileExists: _ => false);

        plan.Entries.Should().BeEmpty();
        plan.FirstMissingPath.Should().Be(arguments[0]);
        plan.ShouldReportMissingPath.Should().BeTrue();
        plan.ShouldPrewarm.Should().BeTrue();
    }

    [Fact]
    public void Plan_normalizes_local_file_uris_before_existence_and_open_routing()
    {
        var path = Path.GetFullPath(Path.Combine("startup", "file with spaces.xlsx"));
        var uri = new Uri(path).AbsoluteUri;
        string? existenceCandidate = null;

        var plan = StartupFileOpenPlanner.Plan(
            [uri],
            recoveryAccepted: false,
            fileExists: candidate =>
            {
                existenceCandidate = candidate;
                return true;
            });

        existenceCandidate.Should().Be(path);
        plan.Entries.Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public void Plan_propagates_startup_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => StartupFileOpenPlanner.Plan(
            [@"C:\work\one.xlsx"],
            recoveryAccepted: false,
            fileExists: _ => true,
            cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void FreeX_hosts_realize_the_portable_startup_file_plan()
    {
        var plannerSource = File.ReadAllText(RepositoryFileLocator.Find(
            "shared",
            "Free.Shared.AppServices",
            "StartupFileOpenPlanner.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Host",
            "App.xaml.cs"));
        var avaloniaAppSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "App.cs"));
        var avaloniaWindowSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.cs"));

        foreach (var hostSource in new[] { wpfSource, avaloniaAppSource })
        {
            hostSource.Should().Contain("StartupFileOpenPlanner.Plan(");
            hostSource.Should().NotContain("PlanStartupFileOpens(");
        }

        avaloniaAppSource.Should().Contain("CompleteStartupAsync(mainWindow, snapshotStore, StartupArguments)");
        avaloniaAppSource.Should().Contain("deferStartupFileOpen: true");
        avaloniaWindowSource.Should().Contain("deferStartupFileOpen ? [] : startupArguments");

        plannerSource.Should().NotContain("System.Windows");
        plannerSource.Should().NotContain("Avalonia.");
        plannerSource.Should().NotContain("MainWindow");
    }
}
