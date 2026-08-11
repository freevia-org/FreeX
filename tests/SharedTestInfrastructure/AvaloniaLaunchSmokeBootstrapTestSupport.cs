using System.IO;
using FluentAssertions;

internal static class AvaloniaLaunchSmokeBootstrapTestSupport
{
    public static void AssertLaunchSmokeOptions(AvaloniaLaunchSmokeBootstrapTestSpec spec)
    {
        var result = spec.Parse(
        [
            spec.SampleArgument,
            "--launch-smoke",
            "report.txt",
            "--launch-smoke-diagnostics-dir",
            "diagnostics",
            "--other",
        ]);

        result.Success.Should().BeTrue();
        result.Error.Should().BeEmpty();
        result.ReportPath.Should().Be("report.txt");
        result.DiagnosticsDirectory.Should().Be("diagnostics");
        result.StartupArguments.Should().Equal(spec.SampleArgument, "--other");
    }

    public static void AssertAppAndLaunchSmokeSources(AvaloniaLaunchSmokeBootstrapTestSpec spec)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory(spec.SolutionFileName);
        var app = File.ReadAllText(Path.Combine(
            root,
            spec.ProductDirectoryName,
            $"{spec.ProductName}.App.Avalonia",
            "App.cs"));
        app.Should().Contain("using Free.Shared.Shell.Avalonia;");
        app.Should().Contain("using Free.Shared.Theme.Avalonia;");
        app.Should().Contain($"AvaloniaThemeApplier.Apply(this, theme, \"{spec.ProductName}\")");
        app.Should().Contain("SisterAvaloniaAppBootstrap.Initialize(");
        app.Should().Contain("new SisterAvaloniaAppBootstrapSpec<MainWindow>(");
        app.Should().NotContain("Styles.Add(new FluentTheme())");
        app.Should().NotContain("desktop.MainWindow = mainWindow;");

        var smoke = File.ReadAllText(Path.Combine(
            root,
            spec.ProductDirectoryName,
            $"{spec.ProductName}.App.Avalonia",
            "Smoke",
            "LaunchSmoke.cs"));
        smoke.Should().Contain("SisterAppLaunchSmokeOptions.TryParse(");
        smoke.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(");
        smoke.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
        smoke.Should().NotContain("new DispatcherTimer");
        smoke.Should().NotContain("Application.Current?.ApplicationLifetime");
    }
}

internal sealed record AvaloniaLaunchSmokeBootstrapTestSpec(
    string SolutionFileName,
    string ProductDirectoryName,
    string ProductName,
    string SampleArgument,
    Func<IReadOnlyList<string>, AvaloniaLaunchSmokeParseResult> Parse);

internal readonly record struct AvaloniaLaunchSmokeParseResult(
    bool Success,
    string? ReportPath,
    string? DiagnosticsDirectory,
    IReadOnlyList<string> StartupArguments,
    string Error);
