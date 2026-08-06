using System.Buffers.Binary;
using Avalonia.Headless;
using FreeX.ParityCompare.Core;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class ScenarioManagerDialogRenderParityTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task ScenarioManager_CapturesCanonicalFrameWithWpfButtonSurfaceAndNoBottomClip()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-scenario-manager-render-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    window.Measure(new global::Avalonia.Size(1120, 720));
                    window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                    window.UpdateLayout();

                    var results = await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        targetSurfaceId: "dialog.ScenarioManager");

                    results.Should().ContainSingle();
                    results[0].Captured.Should().BeTrue(results[0].Note);
                    var pngPath = Path.Combine(outputDirectory, results[0].PngFileName);
                    var dimensions = ReadPngDimensions(pngPath);
                    dimensions.Should().Be((360, 420));

                    var image = PngCodec.DecodeFile(pngPath);
                    CountExactColor(image, 221, 221, 221)
                        .Should().BeGreaterThan(600,
                            "WPF-style neutral button surfaces should remain visible in the dialog capture");
                    CountExactColor(image, 198, 215, 232)
                        .Should().BeGreaterThan(150,
                            "the Add/Edit group border should render as a real compact-dialog frame");
                    FindOpaquePixelsOnBottomRows(image, 390)
                        .Should().BeTrue(
                            "the Close row must remain inside the canonical client frame instead of being clipped");
                }
                finally
                {
                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(outputDirectory);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan(0, 24);
        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static int CountExactColor(PixelImage image, byte red, byte green, byte blue)
    {
        var count = 0;
        for (var offset = 0; offset < image.Pixels.Length; offset += 4)
        {
            if (image.Pixels[offset] == blue &&
                image.Pixels[offset + 1] == green &&
                image.Pixels[offset + 2] == red &&
                image.Pixels[offset + 3] == 255)
            {
                count++;
            }
        }

        return count;
    }

    private static bool FindOpaquePixelsOnBottomRows(PixelImage image, int minimumY)
    {
        for (var y = minimumY; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                if (image.Pixels[offset + 3] == 255 &&
                    (image.Pixels[offset] != 255 ||
                     image.Pixels[offset + 1] != 255 ||
                     image.Pixels[offset + 2] != 255))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // The capture writer may still be releasing a file on a slower host.
        }
    }
}
