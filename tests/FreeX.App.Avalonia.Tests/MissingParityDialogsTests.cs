using System.Buffers.Binary;
using System.IO;
using System.Threading;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class MissingParityDialogsTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public void DialogRoutes_ResolveFormerlyMissingSurfacesToProductionOpeners()
    {
        var routes = MainWindow.ParityInteractionDialogRoutes.ToDictionary(route => route.CatalogId);

        routes["dialog.ChartStyleDialog"].Should().BeEquivalentTo(new
        {
            SurfaceId = "dialog.ChartStyle",
            AvaloniaProductionSurface = "ShowChartStyleDialogAsync",
            IsMissing = false,
        });
        routes["dialog.HeaderFooterPictureFormatDialog"].Should().BeEquivalentTo(new
        {
            SurfaceId = "dialog.HeaderFooterPictureFormat",
            AvaloniaProductionSurface = "ShowHeaderFooterPictureFormatDialogAsync",
            IsMissing = false,
        });
        routes["dialog.UnhideWindowDialog"].Should().BeEquivalentTo(new
        {
            SurfaceId = "dialog.UnhideWindow",
            AvaloniaProductionSurface = "ShowUnhideWindowDialogAsync",
            IsMissing = false,
        });
        routes.Values.Should().NotContain(route => route.IsMissing);
    }

    [Theory]
    [InlineData("dialog.ChartStyleDialog", "dialog.ChartStyle", 480, 350)]
    [InlineData("dialog.HeaderFooterPictureFormatDialog", "dialog.HeaderFooterPictureFormat", 360, 270)]
    [InlineData("dialog.UnhideWindowDialog", "dialog.UnhideWindow", 340, 160)]
    public async Task CaptureParitySurfaces_OpensAndRendersFormerlyMissingProductionDialog(
        string catalogId,
        string expectedSurfaceId,
        int expectedWidth,
        int expectedHeight)
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-missing-dialog-parity-" + Guid.NewGuid().ToString("N"));

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
                        targetSurfaceId: catalogId);

                    results.Should().ContainSingle();
                    results[0].Id.Should().Be(expectedSurfaceId);
                    results[0].Captured.Should().BeTrue(results[0].Note);
                    var pngPath = Path.Combine(outputDirectory, results[0].PngFileName);
                    File.Exists(pngPath).Should().BeTrue();
                    ParityCaptureOutputGuard.ValidatePngOutput(pngPath).Should().BeNull();
                    new FileInfo(pngPath).Length.Should().BeGreaterThan(2_048,
                        "a populated dialog capture should not collapse to a near-blank PNG");
                    ReadPngDimensions(pngPath).Should().Be((expectedWidth, expectedHeight),
                        "the complete fixed-size dialog client area should be captured without edge clipping");
                    window.OwnedWindows.Should().BeEmpty(
                        "a completed parity capture must release its modal before the next theory row starts");
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                        owned.Close();

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort on Windows while the headless compositor releases images.
        }
    }
}
