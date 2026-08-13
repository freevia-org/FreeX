using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.RenderCompare;

/// <summary>
/// Off-screen Avalonia rasteriser: loads a .pptx via PptxPackageReader, then for each
/// slide renders it at the requested pixel size using <see cref="SlideRenderer"/> under
/// Avalonia.Headless and saves slide-NN.png files to the output directory.
///
/// Unlike the WPF renderer, this does NOT require an STA thread — Avalonia.Headless
/// initialises its own single-threaded dispatcher internally.
/// </summary>
internal static class FreePAvaloniaRenderer
{
    // Lazily initialise the headless Avalonia app (only once per process).
    private static bool _appInitialised;
    private static readonly object _appLock = new();

    internal static void EnsureAppInitialised()
    {
        lock (_appLock)
        {
            if (_appInitialised) return;
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

        Presentation presentation;
        try
        {
            presentation = PptxPackageReader.Read(pptxPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load {pptxPath}: {ex.Message}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        Console.WriteLine("FreeP Avalonia render");
        Console.WriteLine($"  input    : {pptxPath}");
        Console.WriteLine($"  outDir   : {outDir}");
        Console.WriteLine($"  size     : {width}x{height}");
        Console.WriteLine($"  slides   : {presentation.Slides.Count}");

        // The Avalonia UI thread is managed by the headless platform; dispatch rendering to it.
        // With SetupWithoutStarting() the headless platform is synchronous on the current thread —
        // no dispatcher queuing is needed. Call SlideRenderer directly.
        int failCount = 0;
        for (int i = 0; i < presentation.Slides.Count; i++)
        {
            string outPath = Path.Combine(outDir, $"slide-{i + 1:D2}.png");
            try
            {
                File.WriteAllBytes(
                    outPath,
                    SlideRenderer.RenderToBytes(presentation, i, width, height));
                Console.WriteLine($"  slide-{i + 1:D2} -> {outPath}");
                var diversity = PixelDiversity.Analyze(outPath);
                Console.WriteLine($"    {diversity}");
                if (!diversity.IsTrustworthy)
                {
                    Console.Error.WriteLine($"    UNTRUSTWORTHY: {diversity.FailureReason}");
                    failCount++;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  slide-{i + 1:D2} FAILED: {ex.Message}");
                failCount++;
            }
        }

        if (failCount == 0)          return 0;
        if (failCount == presentation.Slides.Count) return 1;
        return 2;
    }
}

/// <summary>
/// Minimal headless Avalonia application used by the RenderCompare tool.
/// </summary>
internal sealed class HeadlessFreePApp : Application
{
    public override void Initialize() { /* no styles required for off-screen rendering */ }
}
