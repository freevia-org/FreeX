using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalValidationSourceTests
{
    [Fact]
    public void Physical_validation_option_filters_only_its_control_arguments()
    {
        PhysicalValidationOptions.TryParse(
            ["--physical-validation", "/work/validation", "/documents/demo.pptx"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().NotBeNull();
        options!.OutputDirectory.Should().Be("/work/validation");
        startupArguments.Should().Equal("/documents/demo.pptx");
        error.Should().BeNull();
    }

    [Fact]
    public void Physical_validation_option_accepts_single_token_forwarding()
    {
        PhysicalValidationOptions.TryParse(
            ["--physical-validation=/work/validation", "--launch-smoke"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().NotBeNull();
        options!.OutputDirectory.Should().Be("/work/validation");
        startupArguments.Should().Equal("--launch-smoke");
        error.Should().BeNull();
    }

    [Fact]
    public void Physical_validation_source_wires_the_real_output_and_slideshow_paths()
    {
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/PhysicalValidation.cs"));

        source.Should().Contain("window.ExecuteVideoExportAsync(");
        source.Should().Contain("window.ExecutePrintForPhysicalValidationAsync(");
        source.Should().Contain("window.DiscoverPrintersForPhysicalValidationAsync(");
        source.Should().Contain("new SlideShowWindow(");
        source.Should().Contain("ffprobe");
        source.Should().Contain("MediaPlaybackAvailabilityForTest");
        source.Should().Contain("cups-dry-run/last-submitted.pdf");
        source.Should().Contain("new SystemProcessRunner()");
        source.Should().Contain("new ProcessInvocation(");
        source.Should().NotContain("ProcessStartInfo");
        source.Should().NotContain("RunProcessAsync");
    }

    [Fact]
    public void Shell_shortcuts_tunnel_before_focused_slide_pane_controls()
    {
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/MainWindow.cs"));

        source.Should().Contain("InputElement.KeyDownEvent");
        source.Should().Contain("RoutingStrategies.Tunnel");
        source.Should().Contain("MainWindow_KeyDown(this, e)");
        source.Should().Contain("KeyDown += MainWindow_KeyDown;");
        source.Should().Contain("ShouldTunnelShellUndoRedo");
        source.Should().Contain("current is TextBox");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
