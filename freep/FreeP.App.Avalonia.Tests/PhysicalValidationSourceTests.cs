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
        source.Should().Contain("window.ExecuteNativePrintHandoffAsync(");
        source.Should().Contain("new SlideShowWindow(");
        source.Should().Contain("ffprobe");
        source.Should().Contain("MediaPlaybackAvailabilityForTest");
        source.Should().Contain("cups-dry-run/last-submitted.pdf");
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
}
