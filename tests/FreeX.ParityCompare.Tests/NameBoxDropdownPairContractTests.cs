using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

public sealed class NameBoxDropdownPairContractTests
{
    [Fact]
    public void Validate_AcceptsOnlyTheExactCapturedPairFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "freex-wave69-namebox-contract-" + Guid.NewGuid().ToString("N"));
        var windowsDirectory = Path.Combine(root, "windows");
        var linuxDirectory = Path.Combine(root, "linux");
        Directory.CreateDirectory(windowsDirectory);
        Directory.CreateDirectory(linuxDirectory);

        try
        {
            WritePopup(windowsDirectory);
            WritePopup(linuxDirectory);

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windowsDirectory),
                Manifest(linuxDirectory),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeTrue(string.Join(Environment.NewLine, result.Failures));
            result.Failures.Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Validate_RejectsMissingOrMisSizedSideWithoutFallingBackToVisualDiff()
    {
        var root = Path.Combine(Path.GetTempPath(), "freex-wave69-namebox-contract-" + Guid.NewGuid().ToString("N"));
        var windowsDirectory = Path.Combine(root, "windows");
        var linuxDirectory = Path.Combine(root, "linux");
        Directory.CreateDirectory(windowsDirectory);
        Directory.CreateDirectory(linuxDirectory);

        try
        {
            WritePopup(windowsDirectory);
            var linuxManifest = Manifest(linuxDirectory);
            linuxManifest.Surfaces[0].Width = 207;
            linuxManifest.Surfaces[0].Height = 136;

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windowsDirectory),
                linuxManifest,
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure => failure.Contains("Linux", StringComparison.Ordinal));
            result.Failures.Should().Contain(failure => failure.Contains("dimensions", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Validate_RejectsAValidSizedButBlankPopup()
    {
        var root = Path.Combine(Path.GetTempPath(), "freex-wave69-namebox-contract-" + Guid.NewGuid().ToString("N"));
        var windowsDirectory = Path.Combine(root, "windows");
        var linuxDirectory = Path.Combine(root, "linux");
        Directory.CreateDirectory(windowsDirectory);
        Directory.CreateDirectory(linuxDirectory);

        try
        {
            WritePopup(windowsDirectory);
            WritePopup(linuxDirectory);
            PngCodec.EncodeFile(
                PixelImage.Solid(NameBoxDropdownPairContract.Width, NameBoxDropdownPairContract.Height, 255, 255, 255, 255),
                Path.Combine(linuxDirectory, NameBoxDropdownPairContract.SurfaceId + ".png"));

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windowsDirectory),
                Manifest(linuxDirectory),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure => failure.Contains("uniformly white", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void WritePopup(string directory)
    {
        var image = PixelImage.Solid(
            NameBoxDropdownPairContract.Width,
            NameBoxDropdownPairContract.Height,
            255,
            255,
            255,
            255);
        for (var x = 0; x < image.Width; x++)
        {
            SetPixel(image, x, 0, 180, 180, 180);
            SetPixel(image, x, image.Height - 1, 180, 180, 180);
        }

        PngCodec.EncodeFile(image, Path.Combine(directory, NameBoxDropdownPairContract.SurfaceId + ".png"));
    }

    private static void SetPixel(PixelImage image, int x, int y, byte red, byte green, byte blue)
    {
        var offset = (y * image.Width + x) * 4;
        image.Pixels[offset] = blue;
        image.Pixels[offset + 1] = green;
        image.Pixels[offset + 2] = red;
    }

    private static CaptureManifest Manifest(string directory) => new()
    {
        Platform = "test",
        Shell = "test",
        Surfaces =
        {
            new CapturedSurface
            {
                Id = NameBoxDropdownPairContract.SurfaceId,
                Kind = NameBoxDropdownPairContract.Kind,
                Png = NameBoxDropdownPairContract.SurfaceId + ".png",
                Captured = true,
                Width = NameBoxDropdownPairContract.Width,
                Height = NameBoxDropdownPairContract.Height,
            },
        },
    };
}
