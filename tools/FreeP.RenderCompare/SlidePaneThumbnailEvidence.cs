using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeP.App.Compositor;

namespace FreeP.RenderCompare;

internal sealed record SlidePaneThumbnailEvidencePlan(
    string DeckPath,
    string OutputDirectory,
    int RenderWidth,
    int RenderHeight,
    double PaneThumbnailWidth,
    double PaneThumbnailHeight,
    string WpfDirectory,
    string AvaloniaDirectory,
    string PowerPointDirectory,
    string DiffDirectory)
{
    internal bool RequiresPowerPointBaseline => true;
}

internal sealed record SlidePaneThumbnailEvidenceFileSet(
    string SlideId,
    bool HasWpf,
    bool HasAvalonia,
    bool HasPowerPoint);

internal static class SlidePaneThumbnailEvidence
{
    internal const int DefaultRenderWidth = 320;
    internal const int DefaultRenderHeight = 180;

    internal static SlidePaneThumbnailEvidencePlan CreatePlan(
        string deckPath,
        string outputDirectory,
        int width = DefaultRenderWidth,
        int height = DefaultRenderHeight)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        return new SlidePaneThumbnailEvidencePlan(
            Path.GetFullPath(deckPath),
            fullOutputDirectory,
            width,
            height,
            SlidePanePlanner.DefaultThumbnailWidth,
            SlidePanePlanner.DefaultThumbnailHeight,
            Path.Combine(fullOutputDirectory, "wpf-slide-pane-thumbnails"),
            Path.Combine(fullOutputDirectory, "avalonia-slide-pane-thumbnails"),
            Path.Combine(fullOutputDirectory, "powerpoint-slide-pane-thumbnails"),
            Path.Combine(fullOutputDirectory, "slide-pane-thumbnail-diffs"));
    }

    internal static int Run(string deckPath, string outputDirectory, int width, int height)
    {
        var plan = CreatePlan(deckPath, outputDirectory, width, height);
        if (!File.Exists(plan.DeckPath))
        {
            Console.Error.WriteLine($"File not found: {plan.DeckPath}");
            return 1;
        }

        Directory.CreateDirectory(plan.OutputDirectory);
        Directory.CreateDirectory(plan.WpfDirectory);
        Directory.CreateDirectory(plan.AvaloniaDirectory);
        Directory.CreateDirectory(plan.PowerPointDirectory);
        Directory.CreateDirectory(plan.DiffDirectory);

        Console.WriteLine("Slide-pane thumbnail evidence");
        Console.WriteLine($"  input       : {plan.DeckPath}");
        Console.WriteLine($"  outDir      : {plan.OutputDirectory}");
        Console.WriteLine($"  render size : {plan.RenderWidth}x{plan.RenderHeight}");
        Console.WriteLine($"  pane size   : {plan.PaneThumbnailWidth:F3}x{plan.PaneThumbnailHeight:F3} DIP");

        Console.WriteLine();
        Console.WriteLine("=== Step 1: FreeP WPF thumbnail render ===");
        var wpfExitCode = FreePRenderer.Render(plan.DeckPath, plan.WpfDirectory, plan.RenderWidth, plan.RenderHeight);
        if (wpfExitCode == 1)
        {
            Console.Error.WriteLine("WPF thumbnail render failed fatally.");
            return wpfExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 2: FreeP Avalonia thumbnail render ===");
        var avaloniaExitCode = FreePAvaloniaRenderer.Render(plan.DeckPath, plan.AvaloniaDirectory, plan.RenderWidth, plan.RenderHeight);
        if (avaloniaExitCode == 1)
        {
            Console.Error.WriteLine("Avalonia thumbnail render failed fatally.");
            return avaloniaExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 3: PowerPoint thumbnail baseline export ===");
        var powerPoint = PowerPointInterop.ExportSlidesToPngDetailed(
            plan.DeckPath,
            plan.PowerPointDirectory,
            plan.RenderWidth,
            plan.RenderHeight);
        if (powerPoint.ExitCode != 0)
        {
            Console.Error.WriteLine(
                $"PowerPoint thumbnail baseline unavailable ({powerPoint.FailureKind}); " +
                "WPF/Avalonia thumbnail diffs will still be reported, but PowerPoint-backed rows are n/a.");
        }

        Console.WriteLine();
        Console.WriteLine("=== Step 4: Thumbnail evidence diffs ===");
        PrintDiffTable(plan);

        Console.WriteLine();
        Console.WriteLine($"Output directory: {plan.OutputDirectory}");
        return RenderCompareExitCodes.Combine(wpfExitCode, avaloniaExitCode, powerPoint.ExitCode);
    }

    internal static IReadOnlyList<SlidePaneThumbnailEvidenceFileSet> CollectFileSets(SlidePaneThumbnailEvidencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var slideIds = EnumerateSlideIds(plan.WpfDirectory)
            .Concat(EnumerateSlideIds(plan.AvaloniaDirectory))
            .Concat(EnumerateSlideIds(plan.PowerPointDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return slideIds
            .Select(id => new SlidePaneThumbnailEvidenceFileSet(
                id,
                File.Exists(SlidePath(plan.WpfDirectory, id)),
                File.Exists(SlidePath(plan.AvaloniaDirectory, id)),
                File.Exists(SlidePath(plan.PowerPointDirectory, id))))
            .ToList();
    }

    private static void PrintDiffTable(SlidePaneThumbnailEvidencePlan plan)
    {
        var fileSets = CollectFileSets(plan);

        Console.WriteLine();
        Console.WriteLine($"{"Slide",-10} {"WPF-Av%",10} {"WPF-PP%",10} {"Av-PP%",10}");
        Console.WriteLine(new string('-', 46));

        foreach (var fileSet in fileSets)
        {
            var wpfAvalonia = DiffIfPresent(
                fileSet.HasWpf,
                fileSet.HasAvalonia,
                SlidePath(plan.WpfDirectory, fileSet.SlideId),
                SlidePath(plan.AvaloniaDirectory, fileSet.SlideId),
                Path.Combine(plan.DiffDirectory, $"diff-wpf-av-{fileSet.SlideId[6..]}.png"));
            var wpfPowerPoint = DiffIfPresent(
                fileSet.HasWpf,
                fileSet.HasPowerPoint,
                SlidePath(plan.WpfDirectory, fileSet.SlideId),
                SlidePath(plan.PowerPointDirectory, fileSet.SlideId),
                Path.Combine(plan.DiffDirectory, $"diff-wpf-pp-{fileSet.SlideId[6..]}.png"));
            var avaloniaPowerPoint = DiffIfPresent(
                fileSet.HasAvalonia,
                fileSet.HasPowerPoint,
                SlidePath(plan.AvaloniaDirectory, fileSet.SlideId),
                SlidePath(plan.PowerPointDirectory, fileSet.SlideId),
                Path.Combine(plan.DiffDirectory, $"diff-av-pp-{fileSet.SlideId[6..]}.png"));

            Console.WriteLine(
                $"{fileSet.SlideId,-10} {FormatPercent(wpfAvalonia),10} {FormatPercent(wpfPowerPoint),10} {FormatPercent(avaloniaPowerPoint),10}");
        }

        Console.WriteLine(new string('-', 46));
    }

    private static double? DiffIfPresent(
        bool hasLeft,
        bool hasRight,
        string leftPath,
        string rightPath,
        string heatmapPath)
    {
        if (!hasLeft || !hasRight)
            return null;

        return ImageDiff.Compare(leftPath, rightPath, heatmapPath).MeanChannelDiffPercent;
    }

    private static string FormatPercent(double? value) =>
        value is double actual
            ? actual.ToString("F4")
            : "n/a";

    private static IEnumerable<string> EnumerateSlideIds(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "slide-*.png", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
            : Enumerable.Empty<string>();

    private static string SlidePath(string directory, string slideId) =>
        Path.Combine(directory, slideId + ".png");
}
