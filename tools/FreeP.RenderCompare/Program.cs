using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FreeP.RenderCompare;

/// <summary>
/// FreeP Render Compare — interop harness for Wave 1E.
///
/// Modes:
///   --powerpoint-export &lt;pptx&gt; &lt;outDir&gt; [--width W] [--height H]
///       Drive PowerPoint COM to export each slide to outDir/slide-NN.png.
///       Default size: 1280×720 (16:9).
///
///   --freep-render &lt;pptx&gt; &lt;outDir&gt;
///       STUB — reserved for FreeP renderer (Wave 1D).  Exits with code 3 and
///       prints a TODO message.  Wire in a follow-up once FreeP.App.Presentation
///       lands on the branch.
///
///   --diff &lt;a.png&gt; &lt;b.png&gt; [--heatmap &lt;out.png&gt;]
///       Pixel-diff two PNG files.  Reports mean channel diff % and max channel
///       diff value; optionally writes a false-colour heatmap PNG.
///
///   --generate-corpus &lt;outDir&gt;
///       Author four deterministic test decks via PowerPoint COM and save them to
///       outDir as *.pptx.  Also exports PowerPoint's own PNGs next to each deck.
/// </summary>
internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        // Pin culture for deterministic formatting
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        var mode = args[0].ToLowerInvariant();
        try
        {
            return mode switch
            {
                "--powerpoint-export" => RunPowerPointExport(args[1..]),
                "--freep-render"      => RunFreePRenderStub(args[1..]),
                "--diff"              => RunDiff(args[1..]),
                "--generate-corpus"   => RunGenerateCorpus(args[1..]),
                _                     => PrintUsageAndError($"Unknown mode: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // -----------------------------------------------------------------------
    // Mode: --powerpoint-export
    // -----------------------------------------------------------------------
    private static int RunPowerPointExport(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --powerpoint-export <pptx> <outDir> [--width W] [--height H]");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outDir   = Path.GetFullPath(args[1]);

        int width  = 1280;
        int height = 720;
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--width",  StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var w)) { width  = w; i++; }
            else if (args[i].Equals("--height", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var h)) { height = h; i++; }
        }

        if (!File.Exists(pptxPath))
        {
            Console.Error.WriteLine($"File not found: {pptxPath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        Console.WriteLine($"PowerPoint export");
        Console.WriteLine($"  input  : {pptxPath}");
        Console.WriteLine($"  outDir : {outDir}");
        Console.WriteLine($"  size   : {width}x{height}");

        return PowerPointInterop.ExportSlidesToPng(pptxPath, outDir, width, height);
    }

    // -----------------------------------------------------------------------
    // Mode: --freep-render  (STUB — Wave 1D seam)
    // -----------------------------------------------------------------------
    private static int RunFreePRenderStub(string[] args)
    {
        // TODO (Wave 1D follow-up): wire FreeP.App.Presentation compositor here.
        //
        // Contract the FreeP-render side must implement:
        //   1. Load the .pptx via FreeP.Core.IO.PptxPackageReader -> FreeP.Core.Model.Presentation.
        //   2. For each slide index N (1-based), call FreeP.App.Presentation.SlideRenderer
        //      (or equivalent) to produce a BitmapSource at the requested resolution.
        //   3. Save the BitmapSource as PNG to <outDir>/slide-NN.png (same naming as
        //      PowerPoint export so --diff can pair them by filename).
        //   4. Return 0 on full success, 1 on fatal failure, 2 on partial (some slides failed).
        //
        // Once wired, replace this method body with the real implementation and remove
        // this comment block.  The --diff mode is already fully functional and will
        // immediately start comparing the two sets.

        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --freep-render <pptx> <outDir>");
            return 2;
        }

        Console.Error.WriteLine("--freep-render is not yet implemented (Wave 1D stub).");
        Console.Error.WriteLine("TODO: wire FreeP.App.Presentation.SlideRenderer here.");
        Console.Error.WriteLine($"  input  : {args[0]}");
        Console.Error.WriteLine($"  outDir : {args[1]}");
        return 3;
    }

    // -----------------------------------------------------------------------
    // Mode: --diff
    // -----------------------------------------------------------------------
    private static int RunDiff(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --diff <a.png> <b.png> [--heatmap <out.png>]");
            return 2;
        }

        var pathA = Path.GetFullPath(args[0]);
        var pathB = Path.GetFullPath(args[1]);

        string? heatmapPath = null;
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--heatmap", StringComparison.OrdinalIgnoreCase))
            {
                heatmapPath = Path.GetFullPath(args[i + 1]);
                i++;
            }
        }

        if (!File.Exists(pathA)) { Console.Error.WriteLine($"File not found: {pathA}"); return 1; }
        if (!File.Exists(pathB)) { Console.Error.WriteLine($"File not found: {pathB}"); return 1; }

        Console.WriteLine($"Diff");
        Console.WriteLine($"  A : {pathA}");
        Console.WriteLine($"  B : {pathB}");

        var result = ImageDiff.Compare(pathA, pathB, heatmapPath);

        Console.WriteLine($"  dimensions A : {result.WidthA}x{result.HeightA}");
        Console.WriteLine($"  dimensions B : {result.WidthB}x{result.HeightB}");
        Console.WriteLine($"  mean channel diff : {result.MeanChannelDiffPercent:F4} %");
        Console.WriteLine($"  max  channel diff : {result.MaxChannelDiff} / 255");
        if (heatmapPath != null)
            Console.WriteLine($"  heatmap           : {heatmapPath}");

        return 0;
    }

    // -----------------------------------------------------------------------
    // Mode: --generate-corpus
    // -----------------------------------------------------------------------
    private static int RunGenerateCorpus(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --generate-corpus <outDir>");
            return 2;
        }

        var outDir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"Generate corpus -> {outDir}");
        return CorpusGenerator.Generate(outDir);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static void PrintUsage()
    {
        Console.WriteLine("FreeP.RenderCompare — PowerPoint interop harness (Wave 1E)");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  --powerpoint-export <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Export each slide via PowerPoint COM to PNG.");
        Console.WriteLine();
        Console.WriteLine("  --freep-render <pptx> <outDir>   (stub — Wave 1D seam)");
        Console.WriteLine("      Render via FreeP renderer. Not yet wired.");
        Console.WriteLine();
        Console.WriteLine("  --diff <a.png> <b.png> [--heatmap <out.png>]");
        Console.WriteLine("      Pixel-diff two PNGs; report mean/max channel diff.");
        Console.WriteLine();
        Console.WriteLine("  --generate-corpus <outDir>");
        Console.WriteLine("      Author test .pptx decks via PowerPoint COM.");
    }

    private static int PrintUsageAndError(string error)
    {
        Console.Error.WriteLine($"Error: {error}");
        PrintUsage();
        return 2;
    }
}
