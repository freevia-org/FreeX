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
var avaloniaDirectory = Resolve(repoRoot, options.AvaloniaDirectory ?? "tools/screenshots_avalonia_ribbon");
var outputDirectory = Resolve(repoRoot, options.OutputDirectory ?? "artifacts/parity/freex-excel-chrome");

var excel = RibbonManifest.Load(Path.Combine(excelDirectory, "screenshot_manifest.json"));
var wpf = RibbonManifest.Load(Path.Combine(wpfDirectory, "screenshot_manifest.json"));
var avalonia = RibbonManifest.Load(Path.Combine(avaloniaDirectory, "screenshot_manifest.json"));
Directory.CreateDirectory(outputDirectory);

var report = BuildReport(repoRoot, excelDirectory, wpfDirectory, avaloniaDirectory, excel, wpf, avalonia);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, jsonOptions) + Environment.NewLine);
File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(report));

Console.WriteLine($"Excel ribbon rows: {report.Summary.ExcelCapturedRows}");
Console.WriteLine($"WPF pair coverage: {report.Summary.WpfPairedRows}");
Console.WriteLine($"DPI-normalized provisional comparisons: {report.Summary.ProvisionalPixelComparisons}");
Console.WriteLine($"Coverage-only rows: {report.Summary.CoverageOnlyRows}");
Console.WriteLine($"Avalonia fixed-viewport comparisons: {report.Summary.AvaloniaComparableRows}");
Console.WriteLine($"Report: {Path.Combine(outputDirectory, "report.md")}");

return 0;

static ChromeReport BuildReport(string repoRoot, string excelDirectory, string wpfDirectory, string avaloniaDirectory, RibbonManifest excel, RibbonManifest wpf, RibbonManifest avalonia)
{
    var wpfByPairKey = wpf.Captures
        .Where(c => IsComplete(c.CaptureStatus))
        .GroupBy(c => c.PairKey, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    var avaloniaByPairKey = avalonia.Captures
        .Where(c => IsComplete(c.CaptureStatus))
        .GroupBy(c => c.PairKey, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    var rows = new List<ChromeComparisonRow>();
    foreach (var excelCapture in excel.Captures.Where(c => IsComplete(c.CaptureStatus)).OrderBy(c => c.CaptureSequence))
    {
        wpfByPairKey.TryGetValue(excelCapture.PairKey, out var wpfCapture);
        avaloniaByPairKey.TryGetValue(excelCapture.PairKey, out var avaloniaCapture);
        var wpfComparison = Compare(excelDirectory, excel, excelCapture, wpfDirectory, wpf, wpfCapture);
        var avaloniaComparison = Compare(excelDirectory, excel, excelCapture, avaloniaDirectory, avalonia, avaloniaCapture);
        rows.Add(ChromeComparisonRow.From(excelCapture, wpfCapture, avaloniaCapture, wpfComparison, avaloniaComparison));
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
    var avaloniaProvisional = rows.Where(r => r.AvaloniaStatus == "provisional-pixel-comparison").ToArray();
    var coverageOnly = rows.Count(r => r.Status != "provisional-pixel-comparison" || r.AvaloniaStatus != "provisional-pixel-comparison");
    return new ChromeReport
    {
        Schema = "freex-excel-chrome-comparison/v1",
        GeneratedAtUtc = DateTime.UtcNow,
        Inputs = new ComparisonInputs
        {
            ExcelManifest = Relative(repoRoot, Path.Combine(excelDirectory, "screenshot_manifest.json")),
            WpfManifest = Relative(repoRoot, Path.Combine(wpfDirectory, "screenshot_manifest.json")),
            AvaloniaManifest = Relative(repoRoot, Path.Combine(avaloniaDirectory, "screenshot_manifest.json")),
            AvaloniaScope = "The Avalonia Windows foreground ribbon matrix has the same pair keys and logical viewport metadata as Excel and WPF. Only fixed logical widths are measured.",
        },
        Summary = new ChromeComparisonSummary
        {
            ExcelCapturedRows = excel.Captures.Count(c => IsComplete(c.CaptureStatus)),
            WpfPairedRows = rows.Count(r => r.WpfCaptureStatus == "complete"),
            ProvisionalPixelComparisons = provisional.Length,
            CoverageOnlyRows = coverageOnly,
            AvaloniaPairedRows = rows.Count(r => r.AvaloniaCaptureStatus == "complete"),
            AvaloniaComparableRows = avaloniaProvisional.Length,
            ProvisionalMeanPixelDiffPercent = provisional.Length == 0 ? null : provisional.Average(r => r.DpiNormalizedMeanPixelDiffPercent!.Value),
            ProvisionalMaxPixelDiffPercent = provisional.Length == 0 ? null : provisional.Max(r => r.DpiNormalizedMeanPixelDiffPercent!.Value),
            AvaloniaMeanPixelDiffPercent = avaloniaProvisional.Length == 0 ? null : avaloniaProvisional.Average(r => r.AvaloniaDpiNormalizedMeanPixelDiffPercent!.Value),
            AvaloniaMaxPixelDiffPercent = avaloniaProvisional.Length == 0 ? null : avaloniaProvisional.Max(r => r.AvaloniaDpiNormalizedMeanPixelDiffPercent!.Value),
        },
        Rows = rows,
    };
}

static ComparisonResult Compare(string excelDirectory, RibbonManifest excel, RibbonCapture excelCapture, string targetDirectory, RibbonManifest target, RibbonCapture? targetCapture)
{
    if (targetCapture is null)
        return new("coverage-only-missing", "No complete capture shares the Excel pair key.", null, null, null);

    // "max" intentionally has no fixed logical width: two maximized windows are
    // not a shared viewport, so measuring their full top bands would be false precision.
    if (excelCapture.WindowLogicalWidth is null || targetCapture.WindowLogicalWidth is null ||
        excelCapture.WindowLogicalWidth != targetCapture.WindowLogicalWidth)
        return new("coverage-only", "maximized-window viewport is not a common logical rectangle", null, null, null);

    var excelPath = Path.Combine(excelDirectory, excelCapture.FileName);
    var targetPath = Path.Combine(targetDirectory, targetCapture.FileName);
    if (!File.Exists(excelPath) || !File.Exists(targetPath))
        return new("coverage-only", "one or both retained PNG artifacts are absent", null, null, null);

    int logicalWidth = excelCapture.WindowLogicalWidth.Value;
    int logicalHeight = SharedLogicalHeight(excel, target, excelCapture, targetCapture);
    double pixelDiff = ImageDiff.LogicalViewportMeanPixelDiffPercent(
        PngCodec.DecodeFile(excelPath), PngCodec.DecodeFile(targetPath), logicalWidth, logicalHeight);
    return new("provisional-pixel-comparison", "Matched fixed logical foreground viewport; this is triage evidence, not a parity pass/fail.", logicalWidth, logicalHeight, pixelDiff);
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
    sb.AppendLine($"- Avalonia paired rows: {report.Summary.AvaloniaPairedRows}");
    sb.AppendLine($"- Avalonia fixed-viewport comparisons: {report.Summary.AvaloniaComparableRows}");
    if (report.Summary.ProvisionalMeanPixelDiffPercent is not null)
    {
        sb.AppendLine($"- Provisional WPF mean/max pixel delta: {report.Summary.ProvisionalMeanPixelDiffPercent:0.000}% / {report.Summary.ProvisionalMaxPixelDiffPercent:0.000}%");
    }
    if (report.Summary.AvaloniaMeanPixelDiffPercent is not null)
    {
        sb.AppendLine($"- Provisional Avalonia mean/max pixel delta: {report.Summary.AvaloniaMeanPixelDiffPercent:0.000}% / {report.Summary.AvaloniaMaxPixelDiffPercent:0.000}%");
    }
    sb.AppendLine();
    sb.AppendLine("| Pair | Width | WPF result | WPF delta | Avalonia result | Avalonia delta | Notes |");
    sb.AppendLine("|---|---:|---|---:|---|---:|---|");
    foreach (var row in report.Rows)
    {
        string wpfDelta = row.DpiNormalizedMeanPixelDiffPercent is null ? "—" : $"{row.DpiNormalizedMeanPixelDiffPercent:0.000}%";
        string avaloniaDelta = row.AvaloniaDpiNormalizedMeanPixelDiffPercent is null ? "—" : $"{row.AvaloniaDpiNormalizedMeanPixelDiffPercent:0.000}%";
        sb.AppendLine($"| `{row.PairKey}` | {row.WidthLabel ?? "—"} | {row.Status} | {wpfDelta} | {row.AvaloniaStatus} | {avaloniaDelta} | {EscapeTable(row.Reason)} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Interpretation");
    sb.AppendLine();
    sb.AppendLine("The WPF and Avalonia ribbon images are foreground capture evidence. Their metrics are deliberately provisional: they identify review targets but do not certify Excel visual parity or establish an acceptance threshold.");
    sb.AppendLine();
    sb.AppendLine("The deterministic Avalonia dialog corpus remains a separate evidence lane. This report only compares its new Windows foreground top-band matrix, using the same pair keys and fixed logical viewport metadata as Excel and WPF.");
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

file sealed record ComparisonResult(string Status, string Reason, int? LogicalWidth, int? LogicalHeight, double? PixelDiff);

file sealed class ChromeCompareOptions
{
    public string? ExcelDirectory { get; private set; }
    public string? WpfDirectory { get; private set; }
    public string? AvaloniaDirectory { get; private set; }
    public string? OutputDirectory { get; private set; }
    public bool ShowHelp { get; private set; }

    public static string HelpText => """
        FreeX.ExcelChromeCompare
          --excel-dir <dir>  Excel screenshot directory (default: tools/screenshots_excel)
          --wpf-dir <dir>    FreeX WPF screenshot directory (default: tools/screenshots)
          --avalonia-dir <dir> FreeX Avalonia foreground directory (default: tools/screenshots_avalonia_ribbon)
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
                case "--avalonia-dir": result.AvaloniaDirectory = Next(); break;
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
    public string AvaloniaManifest { get; init; } = "";
    public string AvaloniaScope { get; init; } = "";
}

file sealed class ChromeComparisonSummary
{
    public int ExcelCapturedRows { get; init; }
    public int WpfPairedRows { get; init; }
    public int ProvisionalPixelComparisons { get; init; }
    public int CoverageOnlyRows { get; init; }
    public int AvaloniaPairedRows { get; init; }
    public int AvaloniaComparableRows { get; init; }
    public double? ProvisionalMeanPixelDiffPercent { get; init; }
    public double? ProvisionalMaxPixelDiffPercent { get; init; }
    public double? AvaloniaMeanPixelDiffPercent { get; init; }
    public double? AvaloniaMaxPixelDiffPercent { get; init; }
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
    public string AvaloniaCaptureStatus { get; init; } = "";
    public string AvaloniaStatus { get; init; } = "";
    public int? LogicalWidth { get; init; }
    public int? LogicalHeight { get; init; }
    public double? DpiNormalizedMeanPixelDiffPercent { get; init; }
    public double? AvaloniaDpiNormalizedMeanPixelDiffPercent { get; init; }

    public static ChromeComparisonRow From(RibbonCapture excel, RibbonCapture? wpf, RibbonCapture? avalonia, ComparisonResult wpfComparison, ComparisonResult avaloniaComparison) => new()
    {
        PairKey = excel.PairKey, Surface = excel.Tab ?? excel.CaptureKey, WidthLabel = excel.WidthLabel,
        Status = wpfComparison.Status,
        Reason = $"WPF: {wpfComparison.Reason} Avalonia: {avaloniaComparison.Reason}",
        ExcelCaptureStatus = excel.CaptureStatus,
        WpfCaptureStatus = wpf?.CaptureStatus ?? "missing",
        AvaloniaCaptureStatus = avalonia?.CaptureStatus ?? "missing",
        AvaloniaStatus = avaloniaComparison.Status,
        LogicalWidth = wpfComparison.LogicalWidth,
        LogicalHeight = wpfComparison.LogicalHeight,
        DpiNormalizedMeanPixelDiffPercent = wpfComparison.PixelDiff,
        AvaloniaDpiNormalizedMeanPixelDiffPercent = avaloniaComparison.PixelDiff,
    };
}
