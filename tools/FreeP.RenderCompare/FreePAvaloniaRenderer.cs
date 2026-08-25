using System.IO;
using Avalonia;
using Avalonia.Headless;
using FreeP.App.Rendering.Avalonia;

namespace FreeP.RenderCompare;

/// <summary>
/// Off-screen Avalonia rasteriser: loads a .pptx via PptxPackageReader, then for each
/// slide renders it at the requested pixel size using <see cref="SlideRenderer"/> under
/// Avalonia.Headless and saves slide-NN.png files to the output directory.
///
/// Unlike the WPF renderer, this does not require an STA thread. Avalonia.Headless
/// initialises its own single-threaded dispatcher internally.
/// </summary>
internal static class FreePAvaloniaRenderer
{
    private static bool _appInitialised;
    private static readonly object _appLock = new();

    internal static void EnsureAppInitialised()
    {
        lock (_appLock)
        {
            if (_appInitialised)
                return;
            AppBuilder.Configure<HeadlessFreePApp>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
            _appInitialised = true;
        }
    }

    /// <summary>
    /// Renders all slides in <paramref name="pptxPath"/> to PNG files in <paramref name="outDir"/>.
    /// Returns 0 on full success, 1 on fatal error, 2 on partial failure.
    /// </summary>
    internal static int Render(string pptxPath, string outDir, int width, int height)
    {
        EnsureAppInitialised();
        return PresentationRenderBatchRunner.Render(
            "FreeP Avalonia render",
            pptxPath,
            outDir,
            width,
            height,
            (presentation, slideIndex, renderWidth, renderHeight, outputPath) =>
                File.WriteAllBytes(
                    outputPath,
                    RenderOfficeSizedSlide(
                        presentation,
                        slideIndex,
                        renderWidth,
                        renderHeight)));
    }

    private static byte[] RenderOfficeSizedSlide(
        FreeP.Core.Model.Presentation presentation,
        int slideIndex,
        int targetWidth,
        int targetHeight)
    {
        var nativeSize = RenderCompareSurfaceScaler.ResolveNativeRenderSize(
            presentation,
            targetWidth,
            targetHeight);
        var aspectPreservingPng = SlideRenderer.RenderToBytes(
            presentation,
            slideIndex,
            nativeSize.Width,
            nativeSize.Height);
        return RenderCompareSurfaceScaler.StretchPngToSurface(
            aspectPreservingPng,
            targetWidth,
            targetHeight);
    }
}

/// <summary>
/// Minimal headless Avalonia application used by the RenderCompare tool.
/// </summary>
internal sealed class HeadlessFreePApp : Application
{
    public override void Initialize() { }
}
