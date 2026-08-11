using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// R133 remediation companion to
/// <c>FreeP.App.Host.Tests.PresentationRasterPdfImageDiagnosticsTests</c> (the WPF side): proves the
/// Avalonia raster export path -- <see cref="SlideRenderer.RenderToBytes(Presentation, int, int, int)"/>,
/// the exact renderer <c>FreeP.App.Avalonia.MainWindow.ExportPdfRasterBytes</c> calls -- also surfaces
/// a picture <see cref="SlideCanvas"/> drops because it cannot decode, via the same
/// <see cref="SlideImageRenderDiagnostics"/> ambient capture used on the WPF side. Runs under this
/// project's established real-Skia headless session (<see cref="SlideHeadlessApp"/>,
/// <c>UseHeadlessDrawing: false</c>, same as <c>SlideCanvasAvaloniaTests</c>) so the picture bytes are
/// genuinely decoded through Skia, not stubbed out by a no-op headless renderer.
/// </summary>
public sealed class SlideCanvasRasterPdfImageDiagnosticsTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    private static Task Run(Action action) => Session.Dispatch(action, CancellationToken.None);

    private static Presentation DeckWithPicture(byte[] pictureBytes)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();

        var slide = new Slide { Title = "Slide with picture" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture = new ImagePart { Bytes = pictureBytes, ContentType = "image/png" },
        });
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public async Task RenderToBytes_SurfacesImageDiagnostics_WhenSlidePictureBytesAreUndecodable()
    {
        // Same composition MainWindow.ExportPdfRasterBytes uses: SlideRenderer.RenderToBytes as the
        // renderer, with SlideImageRenderDiagnostics.Capture installed around the render.
        var deck = DeckWithPicture([0x00, 0x01, 0x02, 0x03, 0x04]);
        var diagnostics = new List<string>();
        byte[]? bytes = null;

        await Run(() =>
        {
            using var scope = SlideImageRenderDiagnostics.Capture(diagnostics);
            bytes = SlideRenderer.RenderToBytes(deck, 0, 960, 540);
        });

        bytes.Should().NotBeNull();
        bytes!.Length.Should().BeGreaterThan(0);
        diagnostics.Should().NotBeEmpty(
            "the undecodable slide picture must be surfaced through the Avalonia raster export path, not silently dropped");
    }

    [Fact]
    public async Task RenderToBytes_NoImageDiagnostics_WhenSlidePictureIsDecodable()
    {
        // Sibling no-regression: a valid embedded picture must not spuriously report an image warning.
        var deck = DeckWithPicture(MinimalPngBytes());
        var diagnostics = new List<string>();

        await Run(() =>
        {
            using var scope = SlideImageRenderDiagnostics.Capture(diagnostics);
            SlideRenderer.RenderToBytes(deck, 0, 960, 540);
        });

        diagnostics.Should().BeEmpty();
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
