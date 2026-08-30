using System.IO;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed class StartupArgumentResolverTests
{
    [Fact]
    public void Resolve_UsesOriginalLaunchWorkbookPathWhenWpfStartupArgumentsAreEmpty()
    {
        var workbookPath = Path.GetFullPath("launch-book.xlsx");

        var resolved = StartupArgumentResolver.Resolve(
            [],
            [workbookPath],
            ["FreeX.App.Host.exe"]);
        var plan = StartupFileOpenPlanner.Plan(resolved, recoveryAccepted: false, fileExists: path => path == workbookPath);

        resolved.Should().Equal(workbookPath);
        plan.Entries.Should().ContainSingle().Which.Should().Be(
            new StartupFileOpenEntry(workbookPath, OpenInNewWindow: false));
        plan.ShouldReportMissingPath.Should().BeFalse();
    }

    [Fact]
    public void Resolve_PreservesMissingLaunchPathForTheExistingStartupWarningFlow()
    {
        const string missingPath = @"C:\missing\launch-book.xlsx";

        var resolved = StartupArgumentResolver.Resolve(
            [],
            [missingPath],
            ["FreeX.App.Host.exe"]);
        var plan = StartupFileOpenPlanner.Plan(resolved, recoveryAccepted: false, fileExists: _ => false);

        resolved.Should().Equal(missingPath);
        plan.Entries.Should().BeEmpty();
        plan.FirstMissingPath.Should().Be(missingPath);
        plan.ShouldReportMissingPath.Should().BeTrue();
    }

    [Fact]
    public void Resolve_FallsBackToEnvironmentCommandLineArgumentsWhenNoLaunchArgumentsWereCaptured()
    {
        var resolved = StartupArgumentResolver.Resolve(
            [],
            [],
            ["FreeX.App.Host.exe", "fallback-book.xlsx"]);

        resolved.Should().Equal("fallback-book.xlsx");
    }
}
