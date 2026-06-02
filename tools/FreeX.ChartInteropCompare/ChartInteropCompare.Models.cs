using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FreeX.Core.Model;

internal sealed record ChartCase(
    string Name,
    ChartType Type,
    int ExcelChartType,
    ChartFixtureKind Kind,
    bool FirstColIsCategories = true,
    bool ShowLegend = true)
{
    public string Family => ChartTypeSupport.IsChartExFamily(Type)
        ? "chartEx"
        : "classic";
}

internal enum ChartFixtureKind
{
    CategorySeries,
    SingleSeries,
    Scatter,
    Bubble,
    Stock,
    Surface,
    Histogram,
    BoxAndWhisker
}

internal sealed record ExcelChartExport(int ChartCount, bool ChartExported);

internal static class VisualStatuses
{
    public const string NotEvaluated = "not-evaluated";
    public const string SkippedOpenability = "skipped-openability";
    public const string Pass = "pass";
    public const string KnownGap = "known-gap";
    public const string Fail = "fail";
}

internal sealed record PngMetrics(int Width, int Height, double NonWhiteRatio, IReadOnlyList<bool> AverageHash)
{
    public string SizeText => $"{Width}x{Height}";
}

internal sealed record VisualExpectation(int HashThreshold, int RoundTripHashThreshold, string? KnownGapReason)
{
    private static readonly IReadOnlyDictionary<string, string> KnownGaps =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> KnownRoundTripGaps =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static VisualExpectation For(ChartCompareResult result, CompareOptions options)
    {
        var threshold = string.Equals(result.Family, "chartEx", StringComparison.OrdinalIgnoreCase)
            ? options.ChartExVisualHashThreshold
            : options.ClassicVisualHashThreshold;
        var roundTripThreshold = KnownRoundTripGaps.Contains(result.Chart)
            ? Math.Max(options.RoundTripVisualHashThreshold, 12)
            : options.RoundTripVisualHashThreshold;
        KnownGaps.TryGetValue(result.Chart, out var reason);
        return new VisualExpectation(threshold, roundTripThreshold, reason);
    }

    public string AllowedThresholdText(CompareOptions options) =>
        KnownGapReason is null
            ? HashThreshold.ToString(CultureInfo.InvariantCulture)
            : $"{HashThreshold} (known-gap allowance {options.KnownGapVisualHashThreshold})";
}

internal sealed class ChartCompareResult(string chart, string type, string family)
{
    private readonly List<string> _notes = [];

    public string Chart { get; } = chart;
    public string Type { get; } = type;
    public string Family { get; } = family;
    public bool FreeXRendererPng { get; set; }
    public string? FreeXRendererPngPath { get; set; }
    public bool FreeXXlsxSaved { get; set; }
    public string? FreeXXlsxPath { get; set; }
    public bool FreeXXlsxOpenedInExcel { get; set; }
    public int FreeXExcelChartCount { get; set; }
    public bool FreeXExcelPng { get; set; }
    public string? FreeXExcelPngPath { get; set; }
    public bool ExcelNativeCreated { get; set; }
    public string? ExcelNativeXlsxPath { get; set; }
    public bool ExcelNativePng { get; set; }
    public string? ExcelNativePngPath { get; set; }
    public bool ExcelLoadedByFreeX { get; set; }
    public bool ExcelRoundTripSavedByFreeX { get; set; }
    public string? ExcelRoundTripXlsxPath { get; set; }
    public bool ExcelRoundTripOpenedInExcel { get; set; }
    public int ExcelRoundTripChartCount { get; set; }
    public bool ExcelRoundTripPng { get; set; }
    public string? ExcelRoundTripPngPath { get; set; }
    public string? Error { get; set; }
    public string VisualStatus { get; set; } = VisualStatuses.NotEvaluated;
    public bool KnownVisualGap { get; set; }
    public string? KnownVisualGapReason { get; set; }
    public int VisualHashThreshold { get; set; }
    public int? KnownVisualGapThreshold { get; set; }
    public int RoundTripVisualHashThreshold { get; set; }
    public double? FreeXRendererNonWhiteRatio { get; set; }
    public double? ExcelNativeNonWhiteRatio { get; set; }
    public double? FreeXXlsxExcelNonWhiteRatio { get; set; }
    public double? ExcelRoundTripNonWhiteRatio { get; set; }
    public int? HashDistanceNativeVsFreeXXlsx { get; set; }
    public int? HashDistanceNativeVsRoundTrip { get; set; }
    public int? HashDistanceNativeVsFreeXRenderer { get; set; }
    public string? FreeXRendererImageSize { get; set; }
    public string? ExcelNativeImageSize { get; set; }
    public string? FreeXXlsxExcelImageSize { get; set; }
    public string? ExcelRoundTripImageSize { get; set; }
    public bool ExcelNativeRoundTripXlsxByteIdentical { get; set; }
    public string? VisualFailure { get; set; }
    public string Notes => string.Join("; ", _notes);
    public string SummaryNote => string.Join(
            " ",
            new[] { Notes, Error, VisualFailure }.Where(value => !string.IsNullOrWhiteSpace(value)))
        .Trim();
    public bool OpenabilityPassed =>
        string.IsNullOrWhiteSpace(Error) &&
        FreeXXlsxOpenedInExcel &&
        FreeXExcelPng &&
        ExcelNativeCreated &&
        ExcelNativePng &&
        ExcelLoadedByFreeX &&
        ExcelRoundTripOpenedInExcel &&
        ExcelRoundTripPng;
    public bool VisualGatePassed =>
        VisualStatus is VisualStatuses.Pass or VisualStatuses.KnownGap;
    public string FailureCategory =>
        !OpenabilityPassed ? "openability" :
        !FreeXRendererPng ? "freex-renderer" :
        !VisualGatePassed ? "visual-mismatch" :
        "";
    public bool Passed =>
        OpenabilityPassed &&
        FreeXRendererPng &&
        VisualGatePassed;

    public void AddNote(string note)
    {
        if (!string.IsNullOrWhiteSpace(note))
            _notes.Add(note);
    }
}

internal sealed record ComparisonDirectories(
    string Root,
    string FreeXXlsx,
    string ExcelXlsx,
    string ExcelRoundTripXlsx,
    string FreeXRendererPng,
    string FreeXExcelPng,
    string ExcelNativePng,
    string ExcelRoundTripPng)
{
    public static ComparisonDirectories Create(string root)
    {
        var directories = new ComparisonDirectories(
            root,
            Path.Combine(root, "freex-xlsx"),
            Path.Combine(root, "excel-xlsx"),
            Path.Combine(root, "excel-roundtrip-xlsx"),
            Path.Combine(root, "png-freex-renderer"),
            Path.Combine(root, "png-excel-rendered-freex-xlsx"),
            Path.Combine(root, "png-excel-rendered-native"),
            Path.Combine(root, "png-excel-rendered-roundtrip"));

        foreach (var directory in directories.All())
            Directory.CreateDirectory(directory);

        return directories;
    }

    private IEnumerable<string> All()
    {
        yield return Root;
        yield return FreeXXlsx;
        yield return ExcelXlsx;
        yield return ExcelRoundTripXlsx;
        yield return FreeXRendererPng;
        yield return FreeXExcelPng;
        yield return ExcelNativePng;
        yield return ExcelRoundTripPng;
    }
}

internal sealed record CompareOptions(
    string? OutputDirectory,
    IReadOnlySet<string> ChartFilters,
    IReadOnlySet<string> FamilyFilters,
    int ClassicVisualHashThreshold,
    int ChartExVisualHashThreshold,
    int KnownGapVisualHashThreshold,
    int RoundTripVisualHashThreshold,
    bool ListCharts,
    bool ShowHelp)
{
    private const int MaxHashThreshold = 256;

    public static CompareOptions Parse(string[] args)
    {
        string? outputDirectory = null;
        var chartFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var familyFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classicVisualHashThreshold = 96;
        var chartExVisualHashThreshold = 72;
        var knownGapVisualHashThreshold = 128;
        var roundTripVisualHashThreshold = 4;
        var listCharts = false;
        var showHelp = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h" or "/?":
                    showHelp = true;
                    break;
                case "--list-charts":
                    listCharts = true;
                    break;
                case "--out":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException("--out requires a directory.");
                    outputDirectory = Path.GetFullPath(args[++index]);
                    break;
                case "--chart" or "--charts":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires a chart name or comma-separated list.");
                    AddFilterValues(chartFilters, args[++index]);
                    break;
                case "--family":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException("--family requires classic or chartEx.");
                    AddFilterValues(familyFilters, args[++index]);
                    break;
                case "--classic-visual-threshold":
                    classicVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--chartex-visual-threshold":
                    chartExVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--known-gap-threshold":
                    knownGapVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--roundtrip-threshold":
                    roundTripVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        ValidateFamilies(familyFilters);
        return new CompareOptions(
            outputDirectory,
            chartFilters,
            familyFilters,
            classicVisualHashThreshold,
            chartExVisualHashThreshold,
            knownGapVisualHashThreshold,
            roundTripVisualHashThreshold,
            listCharts,
            showHelp);
    }

    private static void AddFilterValues(HashSet<string> filters, string value)
    {
        foreach (var part in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            filters.Add(part);
    }

    private static int ReadHashThreshold(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires an integer from 0 to 256.");
        if (!int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) ||
            threshold < 0 ||
            threshold > MaxHashThreshold)
        {
            throw new ArgumentException($"{optionName} requires an integer from 0 to {MaxHashThreshold}.");
        }

        return threshold;
    }

    private static void ValidateFamilies(IEnumerable<string> familyFilters)
    {
        foreach (var family in familyFilters)
        {
            if (!string.Equals(family, "classic", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(family, "chartEx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown chart family '{family}'. Expected classic or chartEx.");
            }
        }
    }
}

internal static class CsvBuilderExtensions
{
    public static void AppendCsvRow(this StringBuilder builder, params object?[] values)
    {
        builder.AppendLine(string.Join(",", values.Select(Format)));
    }

    private static string Format(object? value)
    {
        var text = value switch
        {
            null => "",
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
