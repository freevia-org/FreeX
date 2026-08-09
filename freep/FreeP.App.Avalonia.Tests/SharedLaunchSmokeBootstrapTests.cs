using Free.Shared.Shell.Avalonia;

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
    public void SisterAppLaunchSmokeOptions_preserves_FreeP_startup_arguments() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertLaunchSmokeOptions(Spec);

    [Fact]
    public void App_and_launch_smoke_sources_use_shared_sister_helpers() =>
        AvaloniaLaunchSmokeBootstrapTestSupport.AssertAppAndLaunchSmokeSources(Spec);

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
