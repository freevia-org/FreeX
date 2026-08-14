using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.RenderCompare;

/// <summary>
/// Off-screen WPF rasteriser: loads a .pptx via PptxPackageReader, then for each
/// slide renders it at the requested pixel size using SlideCanvas on an STA thread
/// and saves slide-NN.png files to the output directory.
/// </summary>
internal static class FreePRenderer
{
    /// <summary>
    /// Renders all slides in <paramref name="pptxPath"/> to PNG files in <paramref name="outDir"/>.
    /// </summary>
    /// <returns>0 on full success, 1 on fatal error, 2 on partial failure.</returns>
    internal static int Render(string pptxPath, string outDir, int width, int height) =>
        PresentationRenderBatchRunner.Render(
            "FreeP render",
            pptxPath,
            outDir,
            width,
            height,
            (presentation, slideIndex, renderWidth, renderHeight, outputPath) => RenderSlide(
                presentation,
                presentation.Slides[slideIndex],
                renderWidth,
                renderHeight,
                outputPath));

    /// <summary>
    /// Renders a single slide off-screen on an STA thread and saves to <paramref name="pngPath"/>.
    /// Must be called from any thread (spawns its own STA thread internally).
    /// </summary>
    private static void RenderSlide(
        Presentation presentation, Slide slide, int width, int height, string pngPath)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            RenderSlideOnSta(presentation, slide, width, height, pngPath);
            return;
        }

        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                RenderSlideOnSta(presentation, slide, width, height, pngPath);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException is not null)
            throw new InvalidOperationException($"STA render failed: {threadException.Message}", threadException);
    }

    private static void RenderSlideOnSta(
        Presentation presentation, Slide slide, int width, int height, string pngPath)
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var canvas = new SlideCanvas
        {
            Presentation = presentation,
            Slide = slide
        };

        // Draw directly into a visual so off-screen output does not depend on WPF layout retention.
        var visual = new DrawingVisual();
        using (var drawingContext = visual.RenderOpen())
        {
            // Match PowerPoint COM Slide.Export: fill the requested export
            // surface even when it differs from the deck's native aspect ratio.
            canvas.RenderToDrawingContext(drawingContext, width, height, preserveAspectRatio: false);
        }

        // Off-screen rasterisation at 96 DPI (device-independent pixels = physical pixels at 96 DPI).
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var stream = File.OpenWrite(pngPath);
        encoder.Save(stream);
    }
}
