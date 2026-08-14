using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.Core.Model;

namespace FreeP.RenderCompare.Tests;

public sealed class PresentationRenderBatchRunnerTests
{
    [Fact]
    public void Render_owns_slide_naming_diversity_and_partial_exit_policy()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-render-batch-");
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        presentation.Slides.Add(new Slide());
        var visited = new List<string>();

        var exitCode = PresentationRenderBatchRunner.Render(
            "test renderer",
            "fixture.pptx",
            temporaryDirectory.Path,
            8,
            6,
            (_, slideIndex, width, height, outputPath) =>
            {
                visited.Add(Path.GetFileName(outputPath));
                if (slideIndex == 1)
                    throw new InvalidOperationException("expected failure");
                WriteDiversePng(outputPath, width, height);
            },
            _ => presentation);

        exitCode.Should().Be(2);
        visited.Should().Equal("slide-01.png", "slide-02.png");
        PixelDiversity.Analyze(Path.Combine(temporaryDirectory.Path, "slide-01.png"))
            .IsTrustworthy.Should().BeTrue();
        File.Exists(Path.Combine(temporaryDirectory.Path, "slide-02.png")).Should().BeFalse();
    }

    [Fact]
    public void Render_returns_fatal_without_creating_output_when_package_load_fails()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-render-batch-");
        var outputDirectory = Path.Combine(temporaryDirectory.Path, "output");

        var exitCode = PresentationRenderBatchRunner.Render(
            "test renderer",
            "broken.pptx",
            outputDirectory,
            8,
            6,
            (_, _, _, _, _) => { },
            _ => throw new InvalidDataException("broken package"));

        exitCode.Should().Be(1);
        Directory.Exists(outputDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(3, 3, 1)]
    [InlineData(1, 3, 2)]
    internal void Exit_policy_preserves_success_fatal_and_partial_codes(
        int failures,
        int slides,
        int expected)
    {
        PresentationRenderBatchRunner.ClassifyExitCode(failures, slides).Should().Be(expected);
    }

    private static void WriteDiversePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var bright = (x + y) % 2 == 0;
                pixels[offset] = bright ? (byte)240 : (byte)20;
                pixels[offset + 1] = bright ? (byte)220 : (byte)40;
                pixels[offset + 2] = bright ? (byte)200 : (byte)60;
                pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
