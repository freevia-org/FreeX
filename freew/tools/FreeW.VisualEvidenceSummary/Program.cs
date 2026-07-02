using FreeW.App.Presentation.DocumentView;
using SkiaSharp;

var options = Parse(args);
if (options.ShowHelp || string.IsNullOrWhiteSpace(options.RunRoot) || options.ManifestPaths.Count == 0)
{
    PrintUsage();
    return options.ShowHelp ? 0 : 2;
}

var runRoot = Path.GetFullPath(options.RunRoot);
var jsonPath = string.IsNullOrWhiteSpace(options.OutputJson)
    ? Path.Combine(runRoot, FreeWVisualEvidenceManifestNormalizer.SummaryJsonFileName)
    : Path.GetFullPath(options.OutputJson);
var markdownPath = string.IsNullOrWhiteSpace(options.OutputMarkdown)
    ? Path.Combine(runRoot, FreeWVisualEvidenceManifestNormalizer.SummaryMarkdownFileName)
    : Path.GetFullPath(options.OutputMarkdown);

try
{
    var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
        options.ManifestPaths,
        runRoot);
    if (!string.IsNullOrWhiteSpace(options.WordBaselineDirectory))
    {
        var baselineRoot = Path.GetFullPath(options.WordBaselineDirectory);
        var tolerance = FreeWVisualBaselineComparisonPlanner.ResolveTolerance(options.BaselineToleranceName);
        var comparisons = BuildBaselineComparisons(summary, runRoot, baselineRoot, tolerance);
        summary = FreeWVisualEvidenceManifestNormalizer.WithBaselineComparisons(summary, comparisons);
    }

    FreeWVisualEvidenceManifestNormalizer.WriteSummaryFiles(summary, jsonPath, markdownPath);

    Console.WriteLine($"summary json: {jsonPath}");
    Console.WriteLine($"summary markdown: {markdownPath}");
    Console.WriteLine($"evidence rows: {summary.Evidence.Count}");
    if (summary.BaselineComparisons.Count > 0)
    {
        Console.WriteLine($"baseline comparisons: {summary.BaselineComparisons.Count}");
        Console.WriteLine($"baseline tolerance: {summary.BaselineComparisons[0].Tolerance.Name}");
    }
    Console.WriteLine($"trust: {(summary.Trust.Passed ? "passed" : "failed")}");

    if (!summary.Trust.Passed)
    {
        foreach (var failure in summary.Trust.Failures)
            Console.Error.WriteLine($"FAIL: {failure}");
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FreeW.VisualEvidenceSummary failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static Options Parse(string[] args)
{
    var options = new Options();
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--help":
            case "-h":
            case "/?":
                options.ShowHelp = true;
                break;
            case "--run-root":
                options.RunRoot = ReadValue(args, ref i, arg);
                break;
            case "--output-json":
                options.OutputJson = ReadValue(args, ref i, arg);
                break;
            case "--output-md":
            case "--output-markdown":
                options.OutputMarkdown = ReadValue(args, ref i, arg);
                break;
            case "--word-baseline-dir":
            case "--baseline-dir":
                options.WordBaselineDirectory = ReadValue(args, ref i, arg);
                break;
            case "--baseline-tolerance":
                options.BaselineToleranceName = ReadValue(args, ref i, arg);
                break;
            case "--manifest":
                options.ManifestPaths.Add(ReadValue(args, ref i, arg));
                break;
            default:
                if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    options.ManifestPaths.Add(arg);
                else if (string.IsNullOrWhiteSpace(options.RunRoot))
                    options.RunRoot = arg;
                else
                    throw new ArgumentException($"Unknown argument: {arg}");
                break;
        }
    }

    return options;
}

static string ReadValue(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"{option} requires a value.");

    index++;
    return args[index];
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: FreeW.VisualEvidenceSummary --run-root <dir> --manifest <manifest.json> [--manifest <manifest.json>] [--word-baseline-dir <dir>] [--baseline-tolerance <name>] [--output-json <path>] [--output-md <path>]");
    Console.Error.WriteLine("baseline tolerances: " + string.Join(", ", FreeWVisualBaselineComparisonTolerance.BuiltIn.Select(t => t.Name)));
}

static IReadOnlyList<FreeWVisualBaselineComparison> BuildBaselineComparisons(
    FreeWVisualEvidenceNormalizedSummary summary,
    string runRoot,
    string baselineRoot,
    FreeWVisualBaselineComparisonTolerance tolerance)
{
    var comparisons = new List<FreeWVisualBaselineComparison>();
    foreach (var row in summary.Evidence)
    {
        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        if (!policy.IsComparable)
        {
            comparisons.Add(FreeWVisualBaselineComparisonPlanner.BuildSkippedBaselineComparison(row, tolerance));
            continue;
        }

        var candidatePaths = FreeWVisualBaselineComparisonPlanner.BuildBaselineCandidateRelativePaths(row);
        var match = FindBaselinePath(baselineRoot, candidatePaths);
        if (match is null)
        {
            comparisons.Add(FreeWVisualBaselineComparisonPlanner.BuildMissingBaselineComparison(row, tolerance));
            continue;
        }

        try
        {
            var evidencePath = ResolveRelativePath(runRoot, row.OutputPath);
            var actual = DecodePng(evidencePath);
            var baselineOriginal = DecodePng(match.Value.FullPath);
            var baselineForComparison = baselineOriginal;
            var baselineResized = false;
            if (actual.Width != baselineOriginal.Width || actual.Height != baselineOriginal.Height)
            {
                baselineForComparison = DecodePng(match.Value.FullPath, actual.Width, actual.Height);
                baselineResized = true;
            }

            comparisons.Add(FreeWVisualBaselineComparisonPlanner.BuildBaselineComparison(
                row,
                match.Value.RelativePath,
                candidatePaths,
                tolerance,
                actual.Pixels,
                actual.Width,
                actual.Height,
                actual.Stride,
                FreeWVisualEvidencePixelFormat.Rgba32,
                baselineForComparison.Pixels,
                baselineForComparison.Width,
                baselineForComparison.Height,
                baselineForComparison.Stride,
                FreeWVisualEvidencePixelFormat.Rgba32,
                baselineSourceWidth: baselineOriginal.Width,
                baselineSourceHeight: baselineOriginal.Height,
                baselineResized: baselineResized));
        }
        catch (Exception ex)
        {
            comparisons.Add(FreeWVisualBaselineComparisonPlanner.BuildDecodeFailure(
                row,
                match.Value.RelativePath,
                candidatePaths,
                tolerance,
                $"could not decode visual evidence or Word baseline PNG for match key '{FreeWVisualBaselineComparisonPlanner.BuildBaselineMatchKey(row)}': {ex.GetType().Name}: {ex.Message}"));
        }
    }

    return comparisons;
}

static (string RelativePath, string FullPath)? FindBaselinePath(
    string baselineRoot,
    IReadOnlyList<string> candidatePaths)
{
    foreach (var candidatePath in candidatePaths)
    {
        var fullPath = ResolveRelativePath(baselineRoot, candidatePath);
        if (File.Exists(fullPath))
            return (candidatePath, fullPath);
    }

    return null;
}

static string ResolveRelativePath(string root, string relativePath)
{
    return Path.GetFullPath(Path.Combine(
        root,
        relativePath.Replace('/', Path.DirectorySeparatorChar)));
}

static DecodedPng DecodePng(string path, int? targetWidth = null, int? targetHeight = null)
{
    using var source = SKBitmap.Decode(path)
        ?? throw new InvalidOperationException($"PNG could not be decoded: {Path.GetFileName(path)}");
    var width = Math.Max(1, targetWidth ?? source.Width);
    var height = Math.Max(1, targetHeight ?? source.Height);
    using var normalized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(normalized);
    canvas.Clear(SKColors.Transparent);
    canvas.DrawBitmap(source, new SKRect(0, 0, width, height));
    return new DecodedPng(width, height, normalized.RowBytes, normalized.Bytes.ToArray());
}

sealed class Options
{
    public string? RunRoot { get; set; }
    public string? OutputJson { get; set; }
    public string? OutputMarkdown { get; set; }
    public string? WordBaselineDirectory { get; set; }
    public string? BaselineToleranceName { get; set; }
    public bool ShowHelp { get; set; }
    public List<string> ManifestPaths { get; } = [];
}

readonly record struct DecodedPng(int Width, int Height, int Stride, byte[] Pixels);
