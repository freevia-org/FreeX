using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class SisterAppPackagingSmokeTests
{
    [Fact]
    public void HasArgument_DetectsPackagingSmokeCaseInsensitively()
    {
        var found = SisterAppPackagingSmoke.HasArgument(["--other", "--PACKAGING-SMOKE"]);

        found.Should().BeTrue();
    }

    [Fact]
    public void FindReportPath_ReturnsValueAfterPackagingSmokeArgument()
    {
        var path = SisterAppPackagingSmoke.FindReportPath(["--other", "ignored", "--packaging-smoke", "report.txt"]);

        path.Should().Be("report.txt");
    }

    [Fact]
    public void FindReportPath_ReturnsNullWhenPackagingSmokeHasNoValue()
    {
        var path = SisterAppPackagingSmoke.FindReportPath(["--other", "--packaging-smoke"]);

        path.Should().BeNull();
    }

    [Fact]
    public void RemoveArgumentTokens_PreservesNonSmokeArgumentsInOrder()
    {
        var filtered = SisterAppPackagingSmoke.RemoveArgumentTokens(
            ["Book.csv", "--PACKAGING-SMOKE", "--other", "--packaging-smoke"]);

        filtered.Should().Equal("Book.csv", "--other");
    }

    [Fact]
    public void AppSmokeAdaptersConsumeSharedDefaultsAndArgumentScanning()
    {
        var root = FindRepositoryRoot();
        var freeP = Read(root, "freep", "FreeP.App.Avalonia", "Smoke", "LaunchSmoke.cs");
        var freeW = Read(root, "freew", "FreeW.App.Avalonia", "Smoke", "LaunchSmoke.cs");
        var freeX = Read(root, "src", "FreeX.App.Services", "WorkbookStartupSmokeService.cs");

        foreach (var launchSmoke in new[] { freeP, freeW })
        {
            launchSmoke.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(")
                .And.NotContain("MaxAttempts")
                .And.NotContain("PollMilliseconds");
        }

        freeX.Should().Contain("public const string Argument = SisterAppPackagingSmoke.Argument;")
            .And.Contain("SisterAppPackagingSmoke.HasArgument(args)")
            .And.Contain("SisterAppPackagingSmoke.RemoveArgumentTokens(args)")
            .And.NotContain("public const string Argument = \"--packaging-smoke\"");
    }

    [Fact]
    public void WriteReport_CreatesParentDirectoryAndWritesContent()
    {
        using var temp = new TestTemporaryDirectory();
        using var errors = new StringWriter();
        var report = Path.Combine(temp.Path, "nested", "packaging-smoke.txt");

        SisterAppPackagingSmoke.WriteReport(report, "packaging_smoke_status=passed\n", errors);

        File.ReadAllText(report).Should().Be("packaging_smoke_status=passed\n");
        errors.ToString().Should().BeEmpty();
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts]));

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
