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
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-scenario-manager-render-"))
        {
            var outputDirectory = temporaryDirectory.Path;
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
                    // This used to require 600+ pixels of #DDDDDD. That colour has no WPF authority
                    // behind it -- nothing in the WPF shell or host defines it -- it is Avalonia's default
                    // theme button fill, and the assertion only ever passed because these buttons were
                    // unstyled. They are painted by the shared compact chrome now, so requiring that exact
                    // grey would mean restyling every dialog in all three apps to satisfy one test.
                    // Assert the thing the check was really protecting: the action row renders visible
                    // buttons rather than disappearing into the dialog background.
                    CountNonBackgroundPixels(image, minimumY: 370, maximumY: 415)
                        .Should().BeGreaterThan(150,
                            "the action row should render visible button surfaces, not blend into the background");
                    // #C8C8C8 == CompactDialogVisualTokens.BorderHex, the shared group-box border.
                    // 645bd68d04 deliberately moved GroupBoxBorderBrush off the old #C6D7E8 literal onto
                    // that token; this assertion was never updated because the test was async-void and
                    // swallowed its own failures until 87a7f11138.
                    CountExactColor(image, 200, 200, 200)
                        .Should().BeGreaterThan(150,
                            "the Add/Edit group border should render as a real compact-dialog frame");
                    FindOpaquePixelsOnBottomRows(image, 390)
                        .Should().BeTrue(
                            "the Close row must remain inside the canonical client frame instead of being clipped");
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
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

    private static int CountNonBackgroundPixels(PixelImage image, int minimumY, int maximumY)
    {
        var count = 0;
        for (var y = minimumY; y < Math.Min(maximumY, image.Height); y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * 4;
                if (image.Pixels[offset + 3] == 255 &&
                    (image.Pixels[offset] != 255 ||
                     image.Pixels[offset + 1] != 255 ||
                     image.Pixels[offset + 2] != 255))
                {
                    count++;
                }
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

}
