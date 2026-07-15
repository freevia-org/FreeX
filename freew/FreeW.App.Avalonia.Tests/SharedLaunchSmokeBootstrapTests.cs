using FreeW.App.Avalonia.Smoke;

namespace FreeW.App.Avalonia.Tests;

public sealed class SharedLaunchSmokeBootstrapTests
{
    [Fact]
    public void LaunchSmokeOptions_uses_shared_parser_and_preserves_startup_arguments()
    {
        var result = LaunchSmokeOptions.TryParse(
            [
                "sample.docx",
                "--launch-smoke",
                "report.txt",
                "--launch-smoke-diagnostics-dir",
                "diagnostics",
                "--other",
            ],
            out var options,
            out var startupArguments,
            out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
        options.Should().NotBeNull();
        options!.ReportPath.Should().Be("report.txt");
        options.DiagnosticsDirectory.Should().Be("diagnostics");
        startupArguments.Should().Equal("sample.docx", "--other");
    }

    [Fact]
    public void App_and_launch_smoke_sources_use_shared_sister_helpers()
    {
        var app = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "App.cs"));
        app.Should().Contain("using Free.Shared.Shell.Avalonia;");
        app.Should().Contain("using Free.Shared.Theme.Avalonia;");
        app.Should().Contain("AvaloniaThemeApplier.BuildResources(theme, \"FreeW\")");
        app.Should().Contain("SisterAvaloniaAppBootstrap.Initialize(");
        app.Should().Contain("new SisterAvaloniaAppBootstrapSpec<MainWindow>(");
        app.Should().NotContain("Styles.Add(new FluentTheme())");
        app.Should().NotContain("desktop.MainWindow = mainWindow;");

        var smoke = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "Smoke", "LaunchSmoke.cs"));
        smoke.Should().Contain("SisterAppLaunchSmokeOptions.TryParse(");
        smoke.Should().Contain("SisterAppLaunchSmokeCoordinator.Start(");
        smoke.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
        smoke.Should().NotContain("new DispatcherTimer");
        smoke.Should().NotContain("Application.Current?.ApplicationLifetime");
    }

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
