using FreeP.Validation.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class PhysicalValidationSourceTests
{
    [Theory]
    [InlineData("--physical-animation-pane-fixture", "AnimationPane")]
    [InlineData("--physical-smartart-text-pane-fixture", "SmartArtTextPane")]
    public void Physical_fixture_option_filters_selector_from_startup_arguments(
        string selector,
        string expectedKind)
    {
        PhysicalFixtureOptions.TryParse(
            [selector, "/documents/demo.pptx"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().NotBeNull();
        options!.Kind.ToString().Should().Be(expectedKind);
        options.OutputDirectory.Should().BeNull();
        startupArguments.Should().Equal("/documents/demo.pptx");
        error.Should().BeNull();
    }

    [Fact]
    public void Physical_hyperlink_fixture_option_accepts_single_token_output_directory()
    {
        PhysicalFixtureOptions.TryParse(
            ["--physical-internal-slide-hyperlink-fixture=/work/hyperlink"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().Be(new PhysicalFixtureOptions(
            PhysicalFixtureKind.InternalSlideHyperlink,
            "/work/hyperlink"));
        startupArguments.Should().BeEmpty();
        error.Should().BeNull();
    }

    [Fact]
    public void Physical_fixture_option_rejects_multiple_fixture_owners()
    {
        PhysicalFixtureOptions.TryParse(
            ["--physical-animation-pane-fixture", "--physical-smartart-text-pane-fixture"],
            out _,
            out _,
            out var error).Should().BeFalse();

        error.Should().Be("Exactly one physical fixture selector may be supplied.");
    }

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
        var source = File.ReadAllText(RepoFile("freep", "TestSupport", "Validation.Avalonia", "PhysicalValidation.cs"));
        var adapter = File.ReadAllText(RepoFile("freep", "TestSupport", "Validation.Avalonia", "MainWindow.ValidationAccessAdapter.cs"));
        var slideshow = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"));
        var program = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "Program.cs"));

        source.Should().Contain("access.ExecuteVideoExportAsync(");
        source.Should().Contain("access.ExecutePrintAsync(");
        source.Should().Contain("access.DiscoverPrintersAsync(");
        source.Should().Contain("access.ShowSlideShowAsync(");
        source.Should().Contain("ffprobe");
        source.Should().Contain("CaptureMediaPlayback");
        source.Should().Contain("cups-dry-run/last-submitted.pdf");
        source.Should().Contain("new SystemProcessRunner()");
        source.Should().Contain("new ProcessInvocation(");
        source.Should().NotContain("ProcessStartInfo");
        source.Should().NotContain("RunProcessAsync");
        adapter.Should().Contain("internal sealed class ValidationAccessAdapter");
        adapter.Should().Contain("new SlideShowWindow(");
        adapter.Should().NotContain("--physical-validation");
        adapter.Should().NotContain("JsonSerializer");
        adapter.Should().NotContain("SystemProcessRunner");
        slideshow.Should().NotContain("internal sealed class ValidationAccessAdapter");
        slideshow.Should().NotContain("ActiveMediaPlansForTest");
        slideshow.Should().NotContain("MediaPlaybackAvailabilityForTest");
        slideshow.Should().NotContain("LastMediaPlaybackFailureForTest");
        program.Should().NotContain("PhysicalValidationOptions");
        program.Should().NotContain("--physical-validation");
        File.Exists(Path.Combine(Path.GetDirectoryName(RepoFile(
            "freep", "FreeP.App.Avalonia", "Program.cs"))!, "PhysicalValidation.cs")).Should().BeFalse();
        File.ReadAllText(RepoFile("tools", "Run-FreePPhysicalLinuxValidation.ps1"))
            .Should().Contain("\"-Host\", \"Validation\"");
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

    private static string RepoFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.Find(relativeParts);
}
