using FreeP.App.Avalonia.Smoke;

namespace FreeP.App.Avalonia.Tests;

public sealed class SharedLaunchSmokeBootstrapTests
{
    private static readonly AvaloniaLaunchSmokeBootstrapTestSpec Spec = new(
        "FreeP.slnx",
        "freep",
        "FreeP",
        "sample.pptx",
        Parse);

    [Fact]
    public void LaunchSmokeOptions_uses_shared_parser_and_preserves_startup_arguments() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertLaunchSmokeOptions(Spec);

    [Fact]
    public void App_and_launch_smoke_sources_use_shared_sister_helpers() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertAppAndLaunchSmokeSources(Spec);

    private static AvaloniaLaunchSmokeParseResult Parse(IReadOnlyList<string> args)
    {
        var result = LaunchSmokeOptions.TryParse(args, out var options, out var startupArguments, out var error);
        return new(result, options?.ReportPath, options?.DiagnosticsDirectory, startupArguments, error);
    }
}
