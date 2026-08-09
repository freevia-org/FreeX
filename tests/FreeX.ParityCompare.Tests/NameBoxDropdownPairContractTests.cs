using System.Text.Json;
using FluentAssertions;
using FreeX.ParityCompare.Core;

namespace FreeX.ParityCompare.Tests;

public sealed class NameBoxDropdownPairContractTests
{
    [Fact]
    public void Validate_AcceptsOnlyTheExactCapturedPairFrame()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            WriteNativePopupEvidence(linuxDirectory);

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                Manifest(windows: false),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeTrue(string.Join(Environment.NewLine, result.Failures));
            result.Failures.Should().BeEmpty();
        }
    }

    [Fact]
    public void Validate_RejectsMissingOrMisSizedSideWithoutFallingBackToVisualDiff()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            var linuxManifest = Manifest(windows: false);
            linuxManifest.Surfaces[0].Width = 207;
            linuxManifest.Surfaces[0].Height = 136;

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                linuxManifest,
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure => failure.Contains("Linux", StringComparison.Ordinal));
            result.Failures.Should().Contain(failure => failure.Contains("dimensions", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_RejectsAValidSizedButBlankPopup()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            WriteNativePopupEvidence(linuxDirectory);
            PngCodec.EncodeFile(
                PixelImage.Solid(NameBoxDropdownPairContract.Width, NameBoxDropdownPairContract.Height, 255, 255, 255, 255),
                Path.Combine(linuxDirectory, NameBoxDropdownPairContract.SurfaceId + ".png"));

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                Manifest(windows: false),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure => failure.Contains("uniformly white", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("managed-popup-diagnostic")]
    [InlineData("synthetic-stack-panel")]
    public void Validate_RejectsMissingManagedOrSyntheticAvaloniaProvenance(string? provenance)
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            WriteNativePopupEvidence(linuxDirectory);
            var linuxManifest = Manifest(windows: false);
            linuxManifest.Surfaces[0].EvidenceProvenance = provenance;

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                linuxManifest,
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure =>
                failure.Contains("managed/synthetic popup evidence is non-authoritative", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_RejectsNativeManifestWithoutMatchingGeometryEvidence()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            WriteNativePopupEvidence(linuxDirectory);
            var geometryPath = Path.Combine(linuxDirectory, "name-box-dropdown-parity-native.json");
            File.WriteAllText(geometryPath, """{"schemaVersion":1,"captured":true}""");

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                Manifest(windows: false),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure =>
                failure.Contains("geometry evidence could not be validated", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_RejectsNonBlankPopupThatDoesNotMatchTheNativeRootCrop()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-wave69-namebox-contract-"))
        {
            var windowsDirectory = Path.Combine(temporaryDirectory.Path, "windows");
            var linuxDirectory = Path.Combine(temporaryDirectory.Path, "linux");
            Directory.CreateDirectory(windowsDirectory);
            Directory.CreateDirectory(linuxDirectory);

            WritePopup(windowsDirectory);
            WriteNativePopupEvidence(linuxDirectory);
            var reconstructed = CreatePopup();
            SetPixel(reconstructed, 20, 20, 12, 34, 56);
            PngCodec.EncodeFile(
                reconstructed,
                Path.Combine(linuxDirectory, NameBoxDropdownPairContract.SurfaceId + ".png"));

            var result = NameBoxDropdownPairContract.Validate(
                Manifest(windows: true),
                Manifest(windows: false),
                windowsDirectory,
                linuxDirectory);

            result.IsValid.Should().BeFalse();
            result.Failures.Should().Contain(failure =>
                failure.Contains("do not exactly match the declared native root-screenshot crop", StringComparison.Ordinal));
        }
    }

    private static void WritePopup(string directory)
    {
        PngCodec.EncodeFile(
            CreatePopup(),
            Path.Combine(directory, NameBoxDropdownPairContract.SurfaceId + ".png"));
    }

    private static PixelImage CreatePopup()
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

        return image;
    }

    private static void WriteNativePopupEvidence(string directory)
    {
        var popup = CreatePopup();
        var root = PixelImage.Solid(320, 240, 244, 244, 244, 255);
        for (var y = 0; y < popup.Height; y++)
        {
            popup.Pixels.AsSpan(y * popup.Width * 4, popup.Width * 4)
                .CopyTo(root.Pixels.AsSpan(((72 + y) * root.Width + 41) * 4, popup.Width * 4));
        }
        PngCodec.EncodeFile(
            popup,
            Path.Combine(directory, NameBoxDropdownPairContract.SurfaceId + ".png"));
        PngCodec.EncodeFile(
            root,
            Path.Combine(directory, "name-box-dropdown-parity-open-root.png"));
        File.WriteAllText(
            Path.Combine(directory, "name-box-dropdown-parity-before-x11.txt"),
            "100|Book1 - FreeX|WINDOW=100 X=0 Y=0 WIDTH=1120 HEIGHT=720 SCREEN=0" + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(directory, "name-box-dropdown-parity-open-x11.txt"),
            "100|Book1 - FreeX|WINDOW=100 X=0 Y=0 WIDTH=1120 HEIGHT=720 SCREEN=0" + Environment.NewLine +
            "123||WINDOW=123 X=41 Y=72 WIDTH=208 HEIGHT=136 SCREEN=0" + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(directory, "name-box-dropdown-parity-native.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                captured = true,
                surfaceId = NameBoxDropdownPairContract.SurfaceId,
                evidenceProvenance = NameBoxDropdownPairContract.LinuxProvenance,
                sourcePng = "name-box-dropdown-parity-open-root.png",
                windowInventoryBefore = "name-box-dropdown-parity-before-x11.txt",
                windowInventoryOpen = "name-box-dropdown-parity-open-x11.txt",
                sourceWindow = new { id = "123", x = 41, y = 72, width = 208, height = 136 },
                crop = new { x = 41, y = 72, width = 208, height = 136 },
            }));
    }

    private static void SetPixel(PixelImage image, int x, int y, byte red, byte green, byte blue)
    {
        var offset = (y * image.Width + x) * 4;
        image.Pixels[offset] = blue;
        image.Pixels[offset + 1] = green;
        image.Pixels[offset + 2] = red;
    }

    private static CaptureManifest Manifest(bool windows) => new()
    {
        Platform = windows ? "windows" : "linux",
        Shell = windows ? "wpf" : "avalonia",
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
                EvidenceProvenance = windows
                    ? NameBoxDropdownPairContract.WindowsProvenance
                    : NameBoxDropdownPairContract.LinuxProvenance,
                SourcePng = windows ? null : "name-box-dropdown-parity-open-root.png",
                GeometryEvidence = windows ? null : "name-box-dropdown-parity-native.json",
                SourceX = windows ? null : 41,
                SourceY = windows ? null : 72,
                SourceWidth = windows ? null : 208,
                SourceHeight = windows ? null : 136,
            },
        },
    };
}
