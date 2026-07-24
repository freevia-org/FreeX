using System.IO;
using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class WpfVideoExportAdapterTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "FreeP.WpfVideoExportTests", Guid.NewGuid().ToString("N"));

    public WpfVideoExportAdapterTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void EncoderProbe_SelectsPreferredSoftwareEncoder()
    {
        WpfVideoEncoderCapabilityDetector.SelectSoftwareEncoder(
                " V....D libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10\n" +
                " V..... mpeg4 MPEG-4 part 2")
            .Should().Be("libx264");
    }

    [Fact]
    public async Task Export_ExtractsFramesAndAcceptsValidMp4Output()
    {
        var output = Path.Combine(_tempDirectory, "deck.mp4");
        var runner = new SuccessfulVideoProcessRunner(output);
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "libx264", "ready"),
            runner);

        var result = await adapter.ExportAsync(BuildPackage(), output);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.EncoderName.Should().Be("libx264");
        result.ByteCount.Should().BeGreaterThan(0);
        runner.Arguments.Should().ContainInOrder("-f", "concat", "-safe", "0", "-c:v", "libx264");
        runner.Arguments.Should().Contain(argument => argument.EndsWith("frames.txt", StringComparison.OrdinalIgnoreCase));
        File.Exists(output).Should().BeTrue();
    }

    [Fact]
    public async Task Export_RemovesInvalidEncoderOutput()
    {
        var output = Path.Combine(_tempDirectory, "invalid.mp4");
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "mpeg4", "ready"),
            new InvalidVideoProcessRunner(output));

        var result = await adapter.ExportAsync(BuildPackage(), output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty MP4");
        File.Exists(output).Should().BeFalse();
    }

    private static PresentationVideoFramePackage BuildPackage() =>
        PresentationVideoFramePackageExecutor.BuildPackage(
            Presentation.CreateEmpty(),
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private sealed class SuccessfulVideoProcessRunner(string outputPath) : IWpfVideoProcessRunner
    {
        public List<string> Arguments { get; } = [];

        public Task<WpfVideoProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Arguments.AddRange(arguments);
            File.WriteAllBytes(outputPath, Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat"));
            return Task.FromResult(new WpfVideoProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class InvalidVideoProcessRunner(string outputPath) : IWpfVideoProcessRunner
    {
        public Task<WpfVideoProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            File.WriteAllText(outputPath, "not an mp4");
            return Task.FromResult(new WpfVideoProcessResult(0, string.Empty, string.Empty, false));
        }
    }
}
