using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class SharedLaunchSmokeBootstrapTests
{
    private static readonly AvaloniaLaunchSmokeBootstrapTestSpec Spec = new(
        "FreeW.slnx",
        "freew",
        "FreeW",
        "sample.docx",
        Parse);

    [Fact]
    public void SisterAppLaunchSmokeOptions_preserves_FreeW_startup_arguments() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertLaunchSmokeOptions(Spec);

    [Fact]
    public void App_and_launch_smoke_sources_use_shared_sister_helpers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var app = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "App.cs"));
        var smoke = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "TestSupport",
            "Validation.Avalonia",
            "LaunchSmoke.cs"));
        var program = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Program.cs"));
        var validationProgram = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "TestSupport",
            "Validation.Avalonia",
            "Program.cs"));

        app.Should().Contain("new SisterAvaloniaThemeStartupDescriptor<Theme>(");
        app.Should().Contain("FreeWApplicationStartup.Theme,");
        app.Should().Contain("AvaloniaThemeApplier.BuildResources(theme, resourceKeyPrefix)");
        app.Should().Contain("SisterAvaloniaStandardDesktopFactory.Initialize(this, DesktopProfile)");
        app.Should().NotContain("Styles.Add(new FluentTheme())");
        app.Should().NotContain("desktop.MainWindow = mainWindow;");

        app.Should().NotContain("LaunchSmokeOptions");
        program.Should().NotContain("--launch-smoke");
        program.Should().Contain("SisterAvaloniaStandardDesktopFactory.Run(args, App.DesktopProfile)");
        validationProgram.Should().Contain("ValidationHostCommandRouteExecutor.Parsed<SisterAppLaunchSmokeOptions>(");
        validationProgram.Should().Contain("SisterAppLaunchSmokeOptions.TryParse,");
        validationProgram.Should().Contain("FreeW.App.Avalonia.Program.RunToolHost(");
        smoke.Should().Contain("access.StartLaunchSmoke(");
        smoke.Should().Contain("new SisterAppLaunchSmokeReport(snapshot.IsPassed, snapshot.ToReport())");
    }

    private static AvaloniaLaunchSmokeParseResult Parse(IReadOnlyList<string> args)
    {
        var result = SisterAppLaunchSmokeOptions.TryParse(
            args,
            out var options,
            out var startupArguments,
            out var error);
        return new(result, options?.ReportPath, options?.DiagnosticsDirectory, startupArguments, error);
    }
}
