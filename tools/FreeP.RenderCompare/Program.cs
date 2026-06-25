using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FreeP.RenderCompare;

/// <summary>
/// FreeP Render Compare — interop harness for Wave 1F.
///
/// Modes:
///   --powerpoint-export &lt;pptx&gt; &lt;outDir&gt; [--width W] [--height H]
///       Drive PowerPoint COM to export each slide to outDir/slide-NN.png.
///       Default size: 1280×720 (16:9).
///
///   --freep-render &lt;pptx&gt; &lt;outDir&gt; [--width W] [--height H]
///       Render via FreeP WPF renderer (SlideCanvas) off-screen and save PNG per slide.
///
///   --diff &lt;a.png&gt; &lt;b.png&gt; [--heatmap &lt;out.png&gt;]
///       Pixel-diff two PNG files.  Reports mean channel diff % and max channel
///       diff value; optionally writes a false-colour heatmap PNG.
///
///   --compare &lt;deck.pptx&gt; &lt;outDir&gt; [--width W] [--height H]
///       Convenience: runs both --powerpoint-export and --freep-render, then
///       diffs every paired slide, prints a table, and writes diff heatmaps.
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
                "--freep-render"      => RunFreePRender(args[1..]),
                "--diff"              => RunDiff(args[1..]),
                "--compare"           => RunCompare(args[1..]),
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

        (int width, int height) = ParseWidthHeight(args[2..], 1280, 720);

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
    // Mode: --freep-render
    // -----------------------------------------------------------------------
    private static int RunFreePRender(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --freep-render <pptx> <outDir> [--width W] [--height H]");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outDir   = Path.GetFullPath(args[1]);

        (int width, int height) = ParseWidthHeight(args[2..], 1280, 720);

        if (!File.Exists(pptxPath))
        {
            Console.Error.WriteLine($"File not found: {pptxPath}");
            return 1;
        }

        return FreePRenderer.Render(pptxPath, outDir, width, height);
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
    // Mode: --compare  (convenience: pptx-export + freep-render + per-slide diff)
    // -----------------------------------------------------------------------
    private static int RunCompare(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --compare <deck.pptx> <outDir> [--width W] [--height H]");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outDir   = Path.GetFullPath(args[1]);

        (int width, int height) = ParseWidthHeight(args[2..], 1280, 720);

        if (!File.Exists(pptxPath))
        {
            Console.Error.WriteLine($"File not found: {pptxPath}");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var ppDir    = Path.Combine(outDir, "powerpoint");
        var freepDir = Path.Combine(outDir, "freep");
        Directory.CreateDirectory(ppDir);
        Directory.CreateDirectory(freepDir);

        Console.WriteLine("=== Step 1: PowerPoint export ===");
        int rc1 = PowerPointInterop.ExportSlidesToPng(pptxPath, ppDir, width, height);
        if (rc1 != 0)
        {
            Console.Error.WriteLine($"PowerPoint export failed (rc={rc1}). Aborting.");
            return rc1;
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 2: FreeP render ===");
        int rc2 = FreePRenderer.Render(pptxPath, freepDir, width, height);
        if (rc2 == 1)
        {
            Console.Error.WriteLine($"FreeP render failed fatally (rc={rc2}). Aborting.");
            return rc2;
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 3: Per-slide diff ===");

        // Enumerate matched slide pairs.
        var ppFiles = Directory.GetFiles(ppDir, "slide-*.png")
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"{"Slide",-8} {"Mean%",9} {"Max/255",9}");
        Console.WriteLine(new string('-', 32));

        var results = new List<(string slide, double mean, int max)>();

        foreach (var ppFile in ppFiles)
        {
            string name      = Path.GetFileName(ppFile);           // "slide-01.png"
            string slideId   = Path.GetFileNameWithoutExtension(name); // "slide-01"
            string freepFile = Path.Combine(freepDir, name);
            if (!File.Exists(freepFile))
            {
                Console.WriteLine($"{slideId,-8} {"(no FreeP output)",-20}");
                continue;
            }

            string heatmapPath = Path.Combine(outDir, $"diff-{slideId[6..]}.png"); // e.g. diff-01.png
            var diff = ImageDiff.Compare(ppFile, freepFile, heatmapPath);
            results.Add((slideId, diff.MeanChannelDiffPercent, diff.MaxChannelDiff));
            Console.WriteLine($"{slideId,-8} {diff.MeanChannelDiffPercent,9:F4} {diff.MaxChannelDiff,9}");
        }

        Console.WriteLine(new string('-', 32));
        if (results.Count > 0)
        {
            double avgMean = results.Average(r => r.mean);
            int    maxMax  = results.Max(r => r.max);
            Console.WriteLine($"{"AVG",-8} {avgMean,9:F4} {maxMax,9}");
        }

        Console.WriteLine();
        Console.WriteLine($"Output directory: {outDir}");

        return rc2; // 0 or 2 (partial)
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
    private static (int width, int height) ParseWidthHeight(string[] args, int defaultWidth, int defaultHeight)
    {
        int width = defaultWidth;
        int height = defaultHeight;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--width",  StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var w)) { width  = w; i++; }
            else if (args[i].Equals("--height", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var h)) { height = h; i++; }
        }
        return (width, height);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FreeP.RenderCompare — PowerPoint interop harness (Wave 1F)");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  --powerpoint-export <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Export each slide via PowerPoint COM to PNG.");
        Console.WriteLine();
        Console.WriteLine("  --freep-render <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Render via FreeP WPF renderer (SlideCanvas) off-screen.");
        Console.WriteLine();
        Console.WriteLine("  --diff <a.png> <b.png> [--heatmap <out.png>]");
        Console.WriteLine("      Pixel-diff two PNGs; report mean/max channel diff.");
        Console.WriteLine();
        Console.WriteLine("  --compare <deck.pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Run both exporters + diff all slides; print parity table.");
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
