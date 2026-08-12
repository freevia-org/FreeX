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
///   --slide-pane-thumbnail-compare &lt;deck.pptx&gt; &lt;outDir&gt; [--width W] [--height H] [--allow-missing-powerpoint]
///       Render WPF/Avalonia slide-pane thumbnail bitmaps, try PowerPoint COM
///       thumbnail references, and diff every available paired slide.
///       With --allow-missing-powerpoint, COM-unavailable PowerPoint baselines
///       remain n/a but do not fail the run when WPF/Avalonia rendering succeeds.
///
///   --notes-page-preview-evidence &lt;deck.pptx&gt; &lt;outDir&gt;
///       Build the shared notes-page PDF render plan, write a portable PDF plus
///       CSV evidence rows, and report WPF/Avalonia parity without requiring
///       PowerPoint COM.
///
///   --corpus-summary &lt;corpusDir&gt; [--refs &lt;refsDir&gt;] [--manifest &lt;out.json&gt;]
///       Print compact per-deck status, PowerPoint reference PNG availability,
///       and the local PowerPoint COM prerequisite state.
///
///   --export-backstage-evidence &lt;deck.pptx&gt; &lt;outDir&gt;
///       Build shared Backstage export/print evidence rows for WPF/Avalonia
///       package handoff paths and mark PowerPoint baselines n/a/deferred.
///
///   --generate-corpus &lt;outDir&gt;
///       Author four deterministic test decks via PowerPoint COM and save them to
///       outDir as *.pptx.  Also exports PowerPoint's own PNGs next to each deck.
///
///   --generate-presenter-ink-probe &lt;output.pptx&gt;
///       Generate one deterministic presenter-ink deck through the shared persistence
///       planner for PowerPoint COM open/export validation.
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
                "--powerpoint-export-one" => RunPowerPointExportOne(args[1..]),
                "--powerpoint-notes-export" => RunPowerPointNotesExport(args[1..]),
                "--freep-render"      => RunFreePRender(args[1..]),
                "--avalonia-render"   => RunAvaloniaRender(args[1..]),
                "--diff"              => RunDiff(args[1..]),
                "--compare"           => RunCompare(args[1..]),
                "--avalonia-compare"  => RunAvaloniaCompare(args[1..]),
                "--slide-pane-thumbnail-compare" => RunSlidePaneThumbnailCompare(args[1..]),
                "--notes-page-preview-evidence" => RunNotesPagePreviewEvidence(args[1..]),
                "--export-backstage-evidence" => RunExportBackstageEvidence(args[1..]),
                "--dialog-pane-visual-evidence" => RunDialogPaneVisualEvidence(args[1..]),
                "--dialog-pane-visual-report" => RunDialogPaneVisualReport(args[1..]),
                "--whole-window-visual-evidence" => RunWholeWindowVisualEvidence(args[1..]),
                "--whole-window-visual-report" => RunWholeWindowVisualReport(args[1..]),
                "--corpus-summary"    => RunCorpusSummary(args[1..]),
                "--powerpoint-corpus-validate" => RunPowerPointCorpusValidation(args[1..]),
                "--powerpoint-corpus-capture-refs" => RunPowerPointCorpusCaptureReferences(args[1..]),
                "--generate-corpus"           => RunGenerateCorpus(args[1..]),
                "--generate-presenter-ink-probe" => RunGeneratePresenterInkProbe(args[1..]),
                "--patch-chart-labels-19"     => RunPatchChartLabels19(args[1..]),
                "--generate-smartart-fixture" => RunGenerateSmartArtFixture(args[1..]),
                _                             => PrintUsageAndError($"Unknown mode: {args[0]}")
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
    // Mode: --powerpoint-export-one
    // -----------------------------------------------------------------------
    private static int RunPowerPointExportOne(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --powerpoint-export-one <pptx> <outDir> [--width W] [--height H] --result <jsonPath>");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetFullPath(args[1]);
        var resultPath = ReadOption(args, "--result");
        if (resultPath is null)
        {
            Console.Error.WriteLine("Missing required --result path.");
            return 2;
        }

        var (width, height) = ParseWidthHeight(args[2..], 1280, 720);
        var result = PowerPointInterop.ExportSlidesToPngDetailed(pptxPath, outputDirectory, width, height);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
        File.WriteAllText(
            resultPath,
            System.Text.Json.JsonSerializer.Serialize(
                new PowerPointCorpusProcessExporter.ChildResult(
                    result.ExitCode,
                    result.FailureKind,
                    result.ExportedSlides,
                    result.TotalSlides)));
        return result.ExitCode;
    }

    // -----------------------------------------------------------------------
    // Mode: --dialog-pane-visual-evidence
    // -----------------------------------------------------------------------
    private static int RunDialogPaneVisualEvidence(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --dialog-pane-visual-evidence <outDir> [--wpf-exe <path>] [--avalonia-exe <path>] [--timeout-seconds N]");
            return 2;
        }

        var outputDirectory = Path.GetFullPath(args[0]);
        var wpfExecutable = ReadOption(args, "--wpf-exe") ?? Path.GetFullPath(
            Path.Combine("freep", "TestSupport", "VisualEvidence.Wpf", "bin", "Release", "net10.0-windows10.0.19041.0", "FreeP.VisualEvidence.Wpf.exe"));
        var avaloniaExecutable = ReadOption(args, "--avalonia-exe") ?? Path.GetFullPath(
            Path.Combine("freep", "TestSupport", "VisualEvidence.Avalonia", "bin", "Release", "net10.0-windows10.0.19041.0", "FreeP.VisualEvidence.Avalonia.exe"));
        var timeoutSeconds = int.TryParse(ReadOption(args, "--timeout-seconds"), out var parsedTimeout)
            ? parsedTimeout
            : 30;
        return DialogPaneVisualEvidence.Run(outputDirectory, wpfExecutable, avaloniaExecutable, TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static int RunDialogPaneVisualReport(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: --dialog-pane-visual-report <evidenceDir>");
            return 2;
        }

        return DialogPaneVisualEvidence.RegenerateReports(args[0]);
    }

    private static int RunWholeWindowVisualEvidence(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --whole-window-visual-evidence <outDir> [--wpf-exe <path>] [--avalonia-exe <path>] [--timeout-seconds N]");
            return 2;
        }

        var outputDirectory = Path.GetFullPath(args[0]);
        var wpfExecutable = ReadOption(args, "--wpf-exe") ?? Path.GetFullPath(
            Path.Combine("freep", "TestSupport", "VisualEvidence.Wpf", "bin", "Release", "net10.0-windows10.0.19041.0", "FreeP.VisualEvidence.Wpf.exe"));
        var avaloniaExecutable = ReadOption(args, "--avalonia-exe") ?? Path.GetFullPath(
            Path.Combine("freep", "TestSupport", "VisualEvidence.Avalonia", "bin", "Release", "net10.0-windows10.0.19041.0", "FreeP.VisualEvidence.Avalonia.exe"));
        var timeoutSeconds = int.TryParse(ReadOption(args, "--timeout-seconds"), out var parsedTimeout)
            ? parsedTimeout
            : 30;
        return WholeWindowVisualEvidence.Run(outputDirectory, wpfExecutable, avaloniaExecutable, TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static int RunWholeWindowVisualReport(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: --whole-window-visual-report <evidenceDir>");
            return 2;
        }

        return WholeWindowVisualEvidence.RegenerateReports(args[0]);
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

    private static int RunPowerPointNotesExport(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --powerpoint-notes-export <pptx> <out.pdf>");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        if (!File.Exists(pptxPath))
        {
            Console.Error.WriteLine($"File not found: {pptxPath}");
            return 1;
        }

        Console.WriteLine("PowerPoint notes-page export");
        Console.WriteLine($"  input : {pptxPath}");
        Console.WriteLine($"  output: {outputPath}");
        return PowerPointInterop.ExportNotesPagesToPdfDetailed(pptxPath, outputPath).ExitCode;
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
    // Mode: --avalonia-render
    // -----------------------------------------------------------------------
    private static int RunAvaloniaRender(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --avalonia-render <pptx> <outDir> [--width W] [--height H]");
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

        return FreePAvaloniaRenderer.Render(pptxPath, outDir, width, height);
    }

    // -----------------------------------------------------------------------
    // Mode: --avalonia-compare  (Avalonia render + WPF render + per-slide diff)
    // -----------------------------------------------------------------------
    private static int RunAvaloniaCompare(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --avalonia-compare <deck.pptx> <outDir> [--width W] [--height H]");
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

        var wpfDir      = Path.Combine(outDir, "wpf");
        var avaloniaDir = Path.Combine(outDir, "avalonia");
        var ppDir       = Path.Combine(outDir, "powerpoint");
        Directory.CreateDirectory(wpfDir);
        Directory.CreateDirectory(avaloniaDir);

        Console.WriteLine("=== Step 1: FreeP WPF render ===");
        int rc1 = FreePRenderer.Render(pptxPath, wpfDir, width, height);
        if (rc1 == 1) { Console.Error.WriteLine("WPF render failed fatally."); return rc1; }

        Console.WriteLine();
        Console.WriteLine("=== Step 2: FreeP Avalonia render ===");
        int rc2 = FreePAvaloniaRenderer.Render(pptxPath, avaloniaDir, width, height);
        if (rc2 == 1) { Console.Error.WriteLine("Avalonia render failed fatally."); return rc2; }

        Console.WriteLine();
        Console.WriteLine("=== Step 3: PowerPoint export (ground truth) ===");
        Directory.CreateDirectory(ppDir);
        var powerPoint = PowerPointInterop.ExportSlidesToPngDetailed(pptxPath, ppDir, width, height);
        int rc3 = powerPoint.ExitCode;
        if (rc3 != 0)
        {
            Console.Error.WriteLine(
                $"PowerPoint export did not complete ({powerPoint.FailureKind}); PowerPoint-backed diffs will be n/a and the final exit code will be nonzero.");
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 4: Per-slide diffs ===");

        var wpfFiles = Directory.GetFiles(wpfDir, "slide-*.png")
            .OrderBy(f => f).ToList();

        Console.WriteLine();
        Console.WriteLine($"{"Slide",-10} {"WPF%",10} {"Av%",10} {"Av-vs-PP%",12}");
        Console.WriteLine(new string('-', 50));

        var rows = new List<(string slide, double wpf, double av, double avpp)>();

        foreach (var wpfFile in wpfFiles)
        {
            string name       = Path.GetFileName(wpfFile);
            string slideId    = Path.GetFileNameWithoutExtension(name);
            string avFile     = Path.Combine(avaloniaDir, name);
            string ppFile     = Path.Combine(ppDir, name);

            double wpfMean = -1, avMean = -1, avppMean = -1;

            // WPF vs Avalonia
            if (File.Exists(avFile))
            {
                string heatmap = Path.Combine(outDir, $"diff-wpf-av-{slideId[6..]}.png");
                avMean = ImageDiff.Compare(wpfFile, avFile, heatmap).MeanChannelDiffPercent;
            }

            // Avalonia vs PowerPoint
            if (File.Exists(ppFile) && File.Exists(avFile))
            {
                string heatmap = Path.Combine(outDir, $"diff-av-pp-{slideId[6..]}.png");
                avppMean = ImageDiff.Compare(avFile, ppFile, heatmap).MeanChannelDiffPercent;
            }

            // WPF vs PowerPoint (reference)
            if (File.Exists(ppFile))
            {
                string heatmap = Path.Combine(outDir, $"diff-wpf-pp-{slideId[6..]}.png");
                wpfMean = ImageDiff.Compare(wpfFile, ppFile, heatmap).MeanChannelDiffPercent;
            }

            rows.Add((slideId, wpfMean, avMean, avppMean));
            Console.WriteLine($"{slideId,-10} {(wpfMean >= 0 ? $"{wpfMean:F4}" : "n/a"),10} {(avMean >= 0 ? $"{avMean:F4}" : "n/a"),10} {(avppMean >= 0 ? $"{avppMean:F4}" : "n/a"),12}");
        }

        Console.WriteLine(new string('-', 50));
        if (rows.Count > 0)
        {
            var validWpf  = rows.Where(r => r.wpf  >= 0).ToList();
            var validAv   = rows.Where(r => r.av   >= 0).ToList();
            var validAvpp = rows.Where(r => r.avpp >= 0).ToList();
            Console.WriteLine($"{"AVG",-10} {(validWpf.Count  > 0 ? $"{validWpf.Average(r => r.wpf):F4}"   : "n/a"),10} " +
                              $"{(validAv.Count   > 0 ? $"{validAv.Average(r => r.av):F4}"     : "n/a"),10} " +
                              $"{(validAvpp.Count > 0 ? $"{validAvpp.Average(r => r.avpp):F4}" : "n/a"),12}");
        }
        Console.WriteLine();
        Console.WriteLine($"Output directory: {outDir}");

        return RenderCompareExitCodes.Combine(rc1, rc2, rc3);
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
        var powerPoint = PowerPointInterop.ExportSlidesToPngDetailed(pptxPath, ppDir, width, height);
        if (powerPoint.ExitCode != 0)
        {
            Console.Error.WriteLine($"PowerPoint export failed ({powerPoint.FailureKind}, rc={powerPoint.ExitCode}). Aborting.");
            return powerPoint.ExitCode;
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
    // Mode: --slide-pane-thumbnail-compare
    // -----------------------------------------------------------------------
    private static int RunSlidePaneThumbnailCompare(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --slide-pane-thumbnail-compare <deck.pptx> <outDir> [--width W] [--height H] [--allow-missing-powerpoint]");
            return 2;
        }

        var pptxPath = Path.GetFullPath(args[0]);
        var outDir = Path.GetFullPath(args[1]);

        (int width, int height) = ParseWidthHeight(
            args[2..],
            SlidePaneThumbnailEvidence.DefaultRenderWidth,
            SlidePaneThumbnailEvidence.DefaultRenderHeight);
        var allowMissingPowerPoint = HasFlag(args[2..], "--allow-missing-powerpoint");

        return SlidePaneThumbnailEvidence.Run(pptxPath, outDir, width, height, allowMissingPowerPoint);
    }

    // -----------------------------------------------------------------------
    // Mode: --notes-page-preview-evidence
    // -----------------------------------------------------------------------
    private static int RunNotesPagePreviewEvidence(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --notes-page-preview-evidence <deck.pptx> <outDir>");
            return 2;
        }

        return NotesPagePreviewEvidence.Run(args[0], args[1]);
    }

    // -----------------------------------------------------------------------
    // Mode: --export-backstage-evidence
    // -----------------------------------------------------------------------
    private static int RunExportBackstageEvidence(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --export-backstage-evidence <deck.pptx> <outDir>");
            return 2;
        }

        return ExportBackstageEvidence.Run(args[0], args[1]);
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
    // Mode: --generate-presenter-ink-probe
    // -----------------------------------------------------------------------
    private static int RunGeneratePresenterInkProbe(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --generate-presenter-ink-probe <output.pptx>");
            return 2;
        }

        return PresenterInkProbe.Generate(args[0]);
    }

    // -----------------------------------------------------------------------
    // Mode: --corpus-summary
    // -----------------------------------------------------------------------
    private static int RunCorpusSummary(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --corpus-summary <corpusDir> [--refs <refsDir>] [--manifest <out.json>] [--require-complete-refs] [--allow-missing-powerpoint]");
            return 2;
        }

        var corpusDir = Path.GetFullPath(args[0]);
        var refsDir = Path.Combine(corpusDir, "pptx-ref");
        string? manifestPath = null;
        var requireCompleteReferences = false;
        var allowMissingPowerPoint = false;

        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--refs", StringComparison.OrdinalIgnoreCase))
            {
                refsDir = Path.GetFullPath(args[i + 1]);
                i++;
            }
            else if (args[i].Equals("--manifest", StringComparison.OrdinalIgnoreCase))
            {
                manifestPath = Path.GetFullPath(args[i + 1]);
                i++;
            }
        }

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].Equals("--require-complete-refs", StringComparison.OrdinalIgnoreCase))
                requireCompleteReferences = true;
            else if (args[i].Equals("--allow-missing-powerpoint", StringComparison.OrdinalIgnoreCase))
                allowMissingPowerPoint = true;
        }

        if (!Directory.Exists(corpusDir))
        {
            Console.Error.WriteLine($"Corpus directory not found: {corpusDir}");
            return 1;
        }

        var summary = CorpusSummary.Create(corpusDir, refsDir);
        var powerPoint = PowerPointInterop.CheckAvailability();
        summary.Print(Console.Out);
        summary.PrintBaselineVerification(
            Console.Out,
            powerPoint,
            requireCompleteReferences,
            allowMissingPowerPoint);

        if (manifestPath is not null)
        {
            CorpusSummary.WriteManifest(manifestPath, summary.CreateManifest(powerPoint));
            Console.WriteLine($"  manifest             : {manifestPath}");
        }

        return summary.GetBaselineVerificationExitCode(
            powerPoint,
            requireCompleteReferences,
            allowMissingPowerPoint);
    }

    // -----------------------------------------------------------------------
    // Mode: --powerpoint-corpus-validate
    // -----------------------------------------------------------------------
    private static int RunPowerPointCorpusValidation(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --powerpoint-corpus-validate <corpusDir> <outDir> [--refs <refsDir>] [--width W] [--height H]");
            return 2;
        }

        var corpusDirectory = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetFullPath(args[1]);
        var referenceDirectory = ReadOption(args, "--refs");
        if (referenceDirectory is not null)
            referenceDirectory = Path.GetFullPath(referenceDirectory);

        if (!Directory.Exists(corpusDirectory))
        {
            Console.Error.WriteLine($"Corpus directory not found: {corpusDirectory}");
            return 1;
        }

        if (referenceDirectory is not null && !Directory.Exists(referenceDirectory))
        {
            Console.Error.WriteLine($"Reference directory not found: {referenceDirectory}");
            return 1;
        }

        var (width, height) = ParseWidthHeight(args[2..], 1280, 720);
        var timeoutSeconds = int.TryParse(ReadOption(args, "--deck-timeout-seconds"), out var parsedTimeout)
            ? parsedTimeout
            : (int)PowerPointCorpusProcessExporter.DefaultDeckTimeout.TotalSeconds;
        if (timeoutSeconds <= 0)
        {
            Console.Error.WriteLine("--deck-timeout-seconds must be greater than zero.");
            return 2;
        }

        var deckFilter = ParseDeckFilter(ReadOption(args, "--decks"));

        var result = PowerPointCorpusValidator.Validate(
            corpusDirectory,
            outputDirectory,
            referenceDirectory,
            width,
            height,
            deckTimeout: TimeSpan.FromSeconds(timeoutSeconds),
            deckFilter: deckFilter,
            onDeckCompleted: deck => Console.WriteLine(
                $"  [{deck.DeckName}] export={deck.FailureKind} " +
                $"slides={deck.GeneratedSlides}/{deck.TotalSlides} " +
                $"refs={deck.MatchingSlides}/{deck.ComparedSlides} " +
                $"missing={deck.MissingReferences} diff={deck.MismatchedReferences}"));
        result.Print(Console.Out);
        return result.ExitCode;
    }

    // -----------------------------------------------------------------------
    // Mode: --powerpoint-corpus-capture-refs
    // -----------------------------------------------------------------------
    private static int RunPowerPointCorpusCaptureReferences(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --powerpoint-corpus-capture-refs <corpusDir> <refsDir> [--force] [--width W] [--height H]");
            return 2;
        }

        var corpusDirectory = Path.GetFullPath(args[0]);
        var referenceDirectory = Path.GetFullPath(args[1]);
        if (!Directory.Exists(corpusDirectory))
        {
            Console.Error.WriteLine($"Corpus directory not found: {corpusDirectory}");
            return 1;
        }

        if (Directory.Exists(referenceDirectory) &&
            Directory.EnumerateFileSystemEntries(referenceDirectory).Any() &&
            !HasFlag(args, "--force"))
        {
            Console.Error.WriteLine(
                $"Reference directory is not empty: {referenceDirectory}. Pass --force to overwrite captured slides.");
            return 2;
        }

        var (width, height) = ParseWidthHeight(args[2..], 1280, 720);
        var timeoutSeconds = int.TryParse(ReadOption(args, "--deck-timeout-seconds"), out var parsedTimeout)
            ? parsedTimeout
            : (int)PowerPointCorpusProcessExporter.DefaultDeckTimeout.TotalSeconds;
        if (timeoutSeconds <= 0)
        {
            Console.Error.WriteLine("--deck-timeout-seconds must be greater than zero.");
            return 2;
        }

        var result = PowerPointCorpusValidator.CaptureReferences(
            corpusDirectory,
            referenceDirectory,
            width,
            height,
            deckTimeout: TimeSpan.FromSeconds(timeoutSeconds));
        result.PrintCapture(Console.Out);
        return result.ExitCode;
    }

    // -----------------------------------------------------------------------
    // Mode: --patch-chart-labels-19
    //   Patches 19-chart-labels.pptx chart XML (injects c:dLbls + secondary valAx).
    //   Run --powerpoint-export on the result to generate reference PNGs.
    //   Usage: --patch-chart-labels-19 <pptxPath>
    // -----------------------------------------------------------------------
    private static int RunPatchChartLabels19(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --patch-chart-labels-19 <pptxPath>");
            return 2;
        }
        var pptxPath = Path.GetFullPath(args[0]);
        Console.WriteLine($"Patching chart labels XML in: {pptxPath}");
        CorpusGenerator.PatchChartLabels19(pptxPath);
        Console.WriteLine("XML patched successfully.");
        return 0;
    }

    // -----------------------------------------------------------------------
    // Mode: --generate-smartart-fixture
    // -----------------------------------------------------------------------
    private static int RunGenerateSmartArtFixture(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --generate-smartart-fixture <outPath.pptx>");
            return 2;
        }

        var outputPath = Path.GetFullPath(args[0]);
        Console.WriteLine($"Generate SmartArt live fixture -> {outputPath}");

        SmartArtFixtureGenerator.Generate(outputPath);
        Console.WriteLine("Done.");
        return 0;
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

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(arg => arg.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? ReadOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IReadOnlySet<string>? ParseDeckFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var names = value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Count == 0 ? null : names;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FreeP.RenderCompare — PowerPoint interop harness (Wave 1F)");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  --powerpoint-export <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Export each slide via PowerPoint COM to PNG.");
        Console.WriteLine();
        Console.WriteLine("  --powerpoint-notes-export <pptx> <out.pdf>");
        Console.WriteLine("      Export PowerPoint's native notes-page print layout to PDF.");
        Console.WriteLine();
        Console.WriteLine("  --freep-render <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Render via FreeP WPF renderer (SlideCanvas) off-screen.");
        Console.WriteLine();
        Console.WriteLine("  --avalonia-render <pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Render via FreeP Avalonia renderer (SlideCanvas) headless.");
        Console.WriteLine();
        Console.WriteLine("  --diff <a.png> <b.png> [--heatmap <out.png>]");
        Console.WriteLine("      Pixel-diff two PNGs; report mean/max channel diff.");
        Console.WriteLine();
        Console.WriteLine("  --compare <deck.pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      Run both exporters (WPF + PowerPoint) + diff all slides; print parity table.");
        Console.WriteLine();
        Console.WriteLine("  --avalonia-compare <deck.pptx> <outDir> [--width W] [--height H]");
        Console.WriteLine("      WPF + Avalonia + PowerPoint renders + per-slide diff table.");
        Console.WriteLine();
        Console.WriteLine("  --slide-pane-thumbnail-compare <deck.pptx> <outDir> [--width W] [--height H] [--allow-missing-powerpoint]");
        Console.WriteLine("      WPF + Avalonia + PowerPoint slide-pane thumbnail renders + per-slide diff table.");
        Console.WriteLine("      --allow-missing-powerpoint treats COM-unavailable PowerPoint baselines as n/a while preserving WPF/Avalonia failures.");
        Console.WriteLine();
        Console.WriteLine("  --notes-page-preview-evidence <deck.pptx> <outDir>");
        Console.WriteLine("      Shared notes-page PDF render plan + portable PDF/CSV evidence; no PowerPoint COM required.");
        Console.WriteLine();
        Console.WriteLine("  --export-backstage-evidence <deck.pptx> <outDir>");
        Console.WriteLine("      Shared Backstage export/print evidence rows; PowerPoint COM baselines stay n/a/deferred.");
        Console.WriteLine();
        Console.WriteLine("  --dialog-pane-visual-evidence <outDir> [--wpf-exe <path>] [--avalonia-exe <path>] [--timeout-seconds N]");
        Console.WriteLine("  --dialog-pane-visual-report <evidenceDir>");
        Console.WriteLine("      Capture and compare paired WPF/Avalonia dialog, pane, and choice-overlay fixtures at 96 DPI.");
        Console.WriteLine();
        Console.WriteLine("  --whole-window-visual-evidence <outDir> [--wpf-exe <path>] [--avalonia-exe <path>] [--timeout-seconds N]");
        Console.WriteLine("  --whole-window-visual-report <evidenceDir>");
        Console.WriteLine("      Capture and compare independently activated WPF/Avalonia full application clients at 1280x760 and 96 DPI.");
        Console.WriteLine();
        Console.WriteLine("  --corpus-summary <corpusDir> [--refs <refsDir>] [--manifest <out.json>] [--require-complete-refs] [--allow-missing-powerpoint]");
        Console.WriteLine("      Print compact per-deck status and PowerPoint reference PNG availability.");
        Console.WriteLine("      --require-complete-refs fails when refs are missing unless --allow-missing-powerpoint is set and PowerPoint COM is unavailable.");
        Console.WriteLine();
        Console.WriteLine("  --powerpoint-corpus-validate <corpusDir> <outDir> [--refs <refsDir>] [--decks <name[,name...]>] [--width W] [--height H] [--deck-timeout-seconds N]");
        Console.WriteLine("      Open/export corpus decks through isolated PowerPoint workers, optionally filter by stem/filename, and verify slide hashes against references.");
        Console.WriteLine();
        Console.WriteLine("  --powerpoint-corpus-capture-refs <corpusDir> <refsDir> [--force] [--width W] [--height H] [--deck-timeout-seconds N]");
        Console.WriteLine("      Capture PowerPoint COM slide PNGs into a reference tree; --force permits overwriting an existing tree.");
        Console.WriteLine();
        Console.WriteLine("  --generate-corpus <outDir>");
        Console.WriteLine("      Author test .pptx decks via PowerPoint COM.");
        Console.WriteLine();
        Console.WriteLine("  --generate-presenter-ink-probe <output.pptx>");
        Console.WriteLine("      Generate a deterministic shared-planner presenter-ink deck for COM validation.");
        Console.WriteLine();
        Console.WriteLine("  --generate-smartart-fixture <outPath.pptx>");
        Console.WriteLine("      Generate 15-smartart-grouped-list.pptx (10 slides: Process/Hierarchy/Hierarchy3/Cycle/List/GroupedList/Relationship1/GridMatrix/IncreasingCircleProcess/VerticalArrowList).");
        Console.WriteLine("      Pure XML — no PowerPoint COM required. Use with --compare for parity.");
    }

    private static int PrintUsageAndError(string error)
    {
        Console.Error.WriteLine($"Error: {error}");
        PrintUsage();
        return 2;
    }
}
