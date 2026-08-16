using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeX.ParityCompare.Core;

var options = ChromeCompareOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(ChromeCompareOptions.HelpText);
    return 0;
}

var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
var excelDirectory = Resolve(repoRoot, options.ExcelDirectory ?? "tools/screenshots_excel");
var wpfDirectory = Resolve(repoRoot, options.WpfDirectory ?? "tools/screenshots");
var outputDirectory = Resolve(repoRoot, options.OutputDirectory ?? "artifacts/parity/freex-excel-chrome");

var excel = RibbonManifest.Load(Path.Combine(excelDirectory, "screenshot_manifest.json"));
var wpf = RibbonManifest.Load(Path.Combine(wpfDirectory, "screenshot_manifest.json"));
Directory.CreateDirectory(outputDirectory);

var report = BuildReport(repoRoot, excelDirectory, wpfDirectory, excel, wpf);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, jsonOptions) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(report));

Console.WriteLine($"Excel ribbon rows: {report.Summary.ExcelCapturedRows}");
Console.WriteLine($"WPF pair coverage: {report.Summary.WpfPairedRows}");
Console.WriteLine($"DPI-normalized provisional comparisons: {report.Summary.ProvisionalPixelComparisons}");
Console.WriteLine($"Coverage-only rows: {report.Summary.CoverageOnlyRows}");
Console.WriteLine($"Avalonia app-chrome comparisons: {report.Summary.AvaloniaComparableRows} (not captured by the canonical dialog manifest)");
Console.WriteLine($"Report: {Path.Combine(outputDirectory, "report.md")}");

return 0;

static ChromeReport BuildReport(string repoRoot, string excelDirectory, string wpfDirectory, RibbonManifest excel, RibbonManifest wpf)
{
    var wpfByPairKey = wpf.Captures
        .Where(c => IsComplete(c.CaptureStatus))
        .GroupBy(c => c.PairKey, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    var rows = new List<ChromeComparisonRow>();
    foreach (var excelCapture in excel.Captures.Where(c => IsComplete(c.CaptureStatus)).OrderBy(c => c.CaptureSequence))
    {
        if (!wpfByPairKey.TryGetValue(excelCapture.PairKey, out var wpfCapture))
        {
            rows.Add(ChromeComparisonRow.WpfMissing(excelCapture));
            continue;
        }

        // "max" intentionally has no fixed logical width: two maximized windows are not
        // a shared viewport, so measuring their full top bands would produce a false metric.
        if (excelCapture.WindowLogicalWidth is null || wpfCapture.WindowLogicalWidth is null ||
            excelCapture.WindowLogicalWidth != wpfCapture.WindowLogicalWidth)
        {
            rows.Add(ChromeComparisonRow.CoverageOnly(excelCapture, wpfCapture,
                "maximized-window viewport is not a common logical rectangle"));
            continue;
        }

        var excelPath = Path.Combine(excelDirectory, excelCapture.FileName);
        var wpfPath = Path.Combine(wpfDirectory, wpfCapture.FileName);
        if (!File.Exists(excelPath) || !File.Exists(wpfPath))
        {
            rows.Add(ChromeComparisonRow.CoverageOnly(excelCapture, wpfCapture,
                "one or both retained PNG artifacts are absent"));
            continue;
        }

        int logicalWidth = excelCapture.WindowLogicalWidth.Value;
        int logicalHeight = SharedLogicalHeight(excel, wpf, excelCapture, wpfCapture);
        double pixelDiff = ImageDiff.LogicalViewportMeanPixelDiffPercent(
            PngCodec.DecodeFile(excelPath),
            PngCodec.DecodeFile(wpfPath),
            logicalWidth,
            logicalHeight);

        rows.Add(ChromeComparisonRow.Provisional(excelCapture, wpfCapture, logicalWidth, logicalHeight, pixelDiff));
    }

    foreach (var skipped in excel.SkippedCaptures.OrderBy(c => c.CaptureSequence))
    {
        rows.Add(new ChromeComparisonRow
        {
            PairKey = skipped.PairKey,
            Surface = skipped.Tab ?? skipped.CaptureKey,
            WidthLabel = skipped.WidthLabel,
            Status = "source-skipped",
            Reason = skipped.SkipReason ?? "Excel did not expose the requested surface during capture.",
            ExcelCaptureStatus = skipped.CaptureStatus,
            WpfCaptureStatus = "not-evaluated",
            AvaloniaStatus = "not-captured-app-chrome",
        });
    }

    var provisional = rows.Where(r => r.Status == "provisional-pixel-comparison").ToArray();
    var coverageOnly = rows.Count(r => r.Status != "provisional-pixel-comparison");
    return new ChromeReport
    {
        Schema = "freex-excel-chrome-comparison/v1",
        GeneratedAtUtc = DateTime.UtcNow,
        Inputs = new ComparisonInputs
        {
            ExcelManifest = Relative(repoRoot, Path.Combine(excelDirectory, "screenshot_manifest.json")),
            WpfManifest = Relative(repoRoot, Path.Combine(wpfDirectory, "screenshot_manifest.json")),
            AvaloniaScope = "The canonical Avalonia manifest covers dialogs, not a foreground desktop/ribbon top band. It is intentionally reported as unmatched rather than compared to WPF or Excel.",
        },
        Summary = new ChromeComparisonSummary
        {
            ExcelCapturedRows = excel.Captures.Count(c => IsComplete(c.CaptureStatus)),
            WpfPairedRows = rows.Count(r => r.WpfCaptureStatus == "complete"),
            ProvisionalPixelComparisons = provisional.Length,
            CoverageOnlyRows = coverageOnly,
            AvaloniaComparableRows = 0,
            ProvisionalMeanPixelDiffPercent = provisional.Length == 0 ? null : provisional.Average(r => r.DpiNormalizedMeanPixelDiffPercent!.Value),
            ProvisionalMaxPixelDiffPercent = provisional.Length == 0 ? null : provisional.Max(r => r.DpiNormalizedMeanPixelDiffPercent!.Value),
        },
        Rows = rows,
    };
}

static int SharedLogicalHeight(RibbonManifest excel, RibbonManifest wpf, RibbonCapture excelCapture, RibbonCapture wpfCapture)
{
    int excelHeight = ScaleToLogical(excelCapture.Height, excel.CapturePhysicalHeight, excel.CaptureLogicalHeight);
    int wpfHeight = ScaleToLogical(wpfCapture.Height, wpf.CapturePhysicalHeight, wpf.CaptureLogicalHeight);
    if (excelHeight != wpfHeight)
        throw new InvalidOperationException($"Pair '{excelCapture.PairKey}' does not have a shared logical height ({excelHeight} vs {wpfHeight}).");
    return excelHeight;
}

static int ScaleToLogical(int capturedPixels, double? physicalHeight, double? logicalHeight)
{
    if (capturedPixels <= 0 || physicalHeight is not > 0 || logicalHeight is not > 0)
        throw new InvalidOperationException("Capture manifest has insufficient DPI metadata for a pixel comparison.");
    return (int)Math.Round(capturedPixels * logicalHeight.Value / physicalHeight.Value, MidpointRounding.AwayFromZero);
}

static bool IsComplete(string? captureStatus) => string.Equals(captureStatus, "complete", StringComparison.OrdinalIgnoreCase);

static string BuildMarkdown(ChromeReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# FreeX / Excel app-chrome comparison");
    sb.AppendLine();
    sb.AppendLine("> This is an evidence and triage report. A row with a pixel metric is a DPI-normalized image comparison, not a pass/fail claim of Excel visual parity. Coverage-only and unmatched rows are deliberately retained instead of being treated as passing.");
    sb.AppendLine();
    sb.AppendLine($"- Excel captured rows: {report.Summary.ExcelCapturedRows}");
    sb.AppendLine($"- WPF paired rows: {report.Summary.WpfPairedRows}");
    sb.AppendLine($"- Provisional DPI-normalized comparisons: {report.Summary.ProvisionalPixelComparisons}");
    sb.AppendLine($"- Coverage-only / source-skipped rows: {report.Summary.CoverageOnlyRows}");
    sb.AppendLine($"- Avalonia app-chrome rows: {report.Summary.AvaloniaComparableRows} (unmatched scope)");
    if (report.Summary.ProvisionalMeanPixelDiffPercent is not null)
    {
        sb.AppendLine($"- Provisional WPF mean/max pixel delta: {report.Summary.ProvisionalMeanPixelDiffPercent:0.000}% / {report.Summary.ProvisionalMaxPixelDiffPercent:0.000}%");
    }
    sb.AppendLine();
    sb.AppendLine("| Pair | Width | WPF result | Avalonia scope | DPI-normalized delta | Notes |");
    sb.AppendLine("|---|---:|---|---|---:|---|");
    foreach (var row in report.Rows)
    {
        string delta = row.DpiNormalizedMeanPixelDiffPercent is null ? "—" : $"{row.DpiNormalizedMeanPixelDiffPercent:0.000}%";
        sb.AppendLine($"| `{row.PairKey}` | {row.WidthLabel ?? "—"} | {row.Status} | {row.AvaloniaStatus} | {delta} | {EscapeTable(row.Reason)} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Interpretation");
    sb.AppendLine();
    sb.AppendLine("The existing WPF ribbon images predate the current Excel foreground run. Their metrics are therefore provisional and exist to identify visual review targets, not to certify the current product. Re-run `tools/screenshot_ribbon.ps1 -Widths max,1100,900,750` after the foreground lane is free, then re-run this tool.");
    sb.AppendLine();
    sb.AppendLine("The current Avalonia evidence contract captures deterministic dialog surfaces only. It has no foreground desktop/ribbon top-band image at the same viewport as Excel, so an Excel-to-Avalonia pixel delta would be fabricated. Add a foreground Avalonia ribbon capture with the same `PairKey` and logical width before enabling that comparison.");
    return sb.ToString();
}

static string EscapeTable(string? value) => (value ?? string.Empty).Replace("|", "\\|").Replace(Environment.NewLine, " ");

static string FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            return directory.FullName;
    }
    throw new DirectoryNotFoundException("Could not locate FreeX.slnx from the current directory.");
}

static string Resolve(string root, string path) => Path.IsPathFullyQualified(path) ? path : Path.Combine(root, path);
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

file sealed class ChromeCompareOptions
{
    public string? ExcelDirectory { get; private set; }
    public string? WpfDirectory { get; private set; }
    public string? OutputDirectory { get; private set; }
    public bool ShowHelp { get; private set; }

    public static string HelpText => """
        FreeX.ExcelChromeCompare
          --excel-dir <dir>  Excel screenshot directory (default: tools/screenshots_excel)
          --wpf-dir <dir>    FreeX WPF screenshot directory (default: tools/screenshots)
          --out <dir>        Report directory (default: artifacts/parity/freex-excel-chrome)

        The command does not launch Excel or either app. It reads retained capture manifests,
        compares only fixed logical ribbon widths after DPI normalization, and records all other
        surfaces as coverage-only or unmatched.
        """;

    public static ChromeCompareOptions Parse(string[] args)
    {
        var result = new ChromeCompareOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value after {arg}.");
            switch (arg)
            {
                case "--excel-dir": result.ExcelDirectory = Next(); break;
                case "--wpf-dir": result.WpfDirectory = Next(); break;
                case "--out": result.OutputDirectory = Next(); break;
                case "--help" or "-h": result.ShowHelp = true; break;
                default: throw new ArgumentException($"Unknown argument '{arg}'. Use --help.");
            }
        }
        return result;
    }
}

file sealed class RibbonManifest
{
    public double? CaptureLogicalHeight { get; init; }
    public double? CapturePhysicalHeight { get; init; }
    public List<RibbonCapture> Captures { get; init; } = [];
    public List<RibbonCapture> SkippedCaptures { get; init; } = [];

    public static RibbonManifest Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Ribbon capture manifest was not found.", path);
        return JsonSerializer.Deserialize<RibbonManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException($"Ribbon manifest '{path}' deserialized to null.");
    }
}

file sealed class RibbonCapture
{
    public int CaptureSequence { get; init; }
    public string CaptureKey { get; init; } = "";
    public string PairKey { get; init; } = "";
    public string? Tab { get; init; }
    public string? WidthLabel { get; init; }
    public int? WindowLogicalWidth { get; init; }
    public string CaptureStatus { get; init; } = "";
    public string FileName { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string? SkipReason { get; init; }
}

file sealed class ChromeReport
{
    public string Schema { get; init; } = "";
    public DateTime GeneratedAtUtc { get; init; }
    public ComparisonInputs Inputs { get; init; } = new();
    public ChromeComparisonSummary Summary { get; init; } = new();
    public List<ChromeComparisonRow> Rows { get; init; } = [];
}

file sealed class ComparisonInputs
{
    public string ExcelManifest { get; init; } = "";
    public string WpfManifest { get; init; } = "";
    public string AvaloniaScope { get; init; } = "";
}

file sealed class ChromeComparisonSummary
{
    public int ExcelCapturedRows { get; init; }
    public int WpfPairedRows { get; init; }
    public int ProvisionalPixelComparisons { get; init; }
    public int CoverageOnlyRows { get; init; }
    public int AvaloniaComparableRows { get; init; }
    public double? ProvisionalMeanPixelDiffPercent { get; init; }
    public double? ProvisionalMaxPixelDiffPercent { get; init; }
}

file sealed class ChromeComparisonRow
{
    public string PairKey { get; init; } = "";
    public string Surface { get; init; } = "";
    public string? WidthLabel { get; init; }
    public string Status { get; init; } = "";
    public string Reason { get; init; } = "";
    public string ExcelCaptureStatus { get; init; } = "";
    public string WpfCaptureStatus { get; init; } = "";
    public string AvaloniaStatus { get; init; } = "";
    public int? LogicalWidth { get; init; }
    public int? LogicalHeight { get; init; }
    public double? DpiNormalizedMeanPixelDiffPercent { get; init; }

    public static ChromeComparisonRow WpfMissing(RibbonCapture excel) => new()
    {
        PairKey = excel.PairKey, Surface = excel.Tab ?? excel.CaptureKey, WidthLabel = excel.WidthLabel,
        Status = "coverage-only-wpf-missing", Reason = "No complete WPF capture shares the Excel pair key.",
        ExcelCaptureStatus = excel.CaptureStatus, WpfCaptureStatus = "missing", AvaloniaStatus = "not-captured-app-chrome",
    };

    public static ChromeComparisonRow CoverageOnly(RibbonCapture excel, RibbonCapture wpf, string reason) => new()
    {
        PairKey = excel.PairKey, Surface = excel.Tab ?? excel.CaptureKey, WidthLabel = excel.WidthLabel,
        Status = "coverage-only", Reason = reason,
        ExcelCaptureStatus = excel.CaptureStatus, WpfCaptureStatus = wpf.CaptureStatus, AvaloniaStatus = "not-captured-app-chrome",
    };

    public static ChromeComparisonRow Provisional(RibbonCapture excel, RibbonCapture wpf, int logicalWidth, int logicalHeight, double pixelDiff) => new()
    {
        PairKey = excel.PairKey, Surface = excel.Tab ?? excel.CaptureKey, WidthLabel = excel.WidthLabel,
        Status = "provisional-pixel-comparison",
        Reason = "Matched fixed logical viewport; WPF capture provenance predates the Excel run, so this is triage evidence rather than a parity pass/fail.",
        ExcelCaptureStatus = excel.CaptureStatus, WpfCaptureStatus = wpf.CaptureStatus, AvaloniaStatus = "not-captured-app-chrome",
        LogicalWidth = logicalWidth, LogicalHeight = logicalHeight, DpiNormalizedMeanPixelDiffPercent = pixelDiff,
    };
}
