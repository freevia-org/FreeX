using FluentAssertions;
using Free.Shared.Pdf.Skia;
using SkiaSharp;

namespace Free.Shared.Pdf.Tests;

public sealed class PdfRenderGeometryTests
{
    [Fact]
    public void CoordinatePlan_MapsPdfUserSpaceToTopDownCanvasSpace()
    {
        PdfRenderGeometry.ToCanvasY(540, 100).Should().Be(440);
        PdfRenderGeometry.ToCanvasTop(540, 100, 40).Should().Be(400);
    }

    [Fact]
    public void ImageSourcePlan_IsSharedBySkiaAndPortableAdapters()
    {
        PdfRenderGeometry.TryGetImageSourceRect(
                16,
                16,
                new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375),
                out var expected)
            .Should()
            .BeTrue();
        expected.Should().Be(new PdfImagePixelRect(4, 2, 8, 8));

        using var bitmap = new SKBitmap(16, 16);
        using var image = SKImage.FromBitmap(bitmap);
        SkiaPdfWriter.TryGetSourceRect(
                image,
                new PdfImageSourceCrop(0.25, 0.125, 0.25, 0.375),
                out var skiaRect)
            .Should()
            .BeTrue();
        skiaRect.Should().Be(SKRect.Create(expected.X, expected.Y, expected.Width, expected.Height));
    }

    [Theory]
    [InlineData(PdfImageClipKind.Triangle, 3)]
    [InlineData(PdfImageClipKind.Diamond, 4)]
    [InlineData(PdfImageClipKind.Parallelogram, 4)]
    [InlineData(PdfImageClipKind.Hexagon, 6)]
    [InlineData(PdfImageClipKind.Chevron, 6)]
    public void PresetClipPlan_IsIndependentOfBackendTypes(PdfImageClipKind kind, int pointCount)
    {
        var points = PdfRenderGeometry.GetPresetClipPolygonPoints(10, 30, 20, 10, kind);

        points.Should().HaveCount(pointCount);
        points.Should().AllSatisfy(point =>
        {
            point.X.Should().BeInRange(10, 30);
            point.Y.Should().BeInRange(30, 40);
        });

        using var path = SkiaPdfWriter.CreatePresetClipPath(kind, new SKRect(10, 20, 30, 40));
        path.Bounds.Left.Should().Be(10);
        path.Bounds.Right.Should().Be(30);
        path.Bounds.Top.Should().Be(20);
        path.Bounds.Bottom.Should().Be(40);
    }

    [Fact]
    public void GradientPlan_NormalizesStopsOnceForBothAdapters()
    {
        var gradient = new PdfLinearGradient(
            10,
            20,
            80,
            70,
            [
                new PdfGradientStop(0.5, new PdfColor(0x44, 0x55, 0x66)),
                new PdfGradientStop(2, new PdfColor(0xAA, 0xBB, 0xCC)),
                new PdfGradientStop(-1, new PdfColor(0x11, 0x22, 0x33)),
            ]);

        PdfRenderGeometry.TryNormalizeGradient(gradient, out var stops).Should().BeTrue();
        stops.Select(stop => stop.Position).Should().Equal(0, 0.5, 1);
    }

    [Fact]
    public void AdapterSources_UseNeutralGeometryAndRetainNativePaintBoundaries()
    {
        var root = FindWorkspaceRoot();
        var portable = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Pdf", "PortablePdfWriter.cs"));
        var skia = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Pdf.Skia", "SkiaPdfWriter.cs"));
        var wpf = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Pdf.Wpf", "WpfRasterPdfWriter.cs"));

        portable.Should().Contain("PdfRenderGeometry.TryGetImageSourceRect");
        portable.Should().Contain("PdfRenderGeometry.GetPresetClipPolygonPoints");
        portable.Should().Contain("PdfRenderGeometry.TryNormalizeGradient");
        portable.Should().NotContain("private static PdfPathPoint[] GetPresetClipPolygonPoints");
        portable.Should().NotContain("private static bool TryNormalizeGradient");

        skia.Should().Contain("PdfRenderGeometry.ToCanvasTop");
        skia.Should().Contain("PdfRenderGeometry.ToCanvasY");
        skia.Should().Contain("PdfRenderGeometry.TryGetImageSourceRect");
        skia.Should().Contain("PdfRenderGeometry.GetPresetClipPolygonPoints");
        skia.Should().Contain("PdfRenderGeometry.TryNormalizeGradient");
        skia.Should().NotContain("private static SKPoint[] GetPresetClipPolygonPoints");
        skia.Should().NotContain("private static bool TryNormalizeGradient");

        wpf.Should().Contain("XImage.FromBitmapSource");
        wpf.Should().Contain("PdfSharp.Drawing");
        wpf.Should().NotContain("SkiaSharp");
        wpf.Should().NotContain("? \"FreeX\"");
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
