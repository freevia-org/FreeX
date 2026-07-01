using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.ToolsShared.Wpf;
using static FreeX.ToolsShared.Wpf.WpfImageDiff;

/// <summary>
/// FreeX Chart File Compare — renders all charts from a real workbook and compares against
/// pre-exported Excel PNGs.  Produces REPORT.txt and worst_NN.png composites.
/// </summary>
internal static class Program
{
    private const string WorkbookPath = @"E:\Users\quinn\Downloads\10-Advanced-Excel-Charts.xlsx";
    private const string ExcelPngDir = @"C:\Users\quinn\AppData\Local\Temp\advcharts-excel";
    private const string OutputDir = @"C:\Users\quinn\AppData\Local\Temp\advcharts-freex";
    private const int WorstCount = 12;

    [STAThread]
    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Directory.CreateDirectory(OutputDir);

        Console.WriteLine("=== FreeX Chart File Compare ===");
        Console.WriteLine($"Workbook : {WorkbookPath}");
        Console.WriteLine($"Excel PNGs: {ExcelPngDir}");
        Console.WriteLine($"Output   : {OutputDir}");

        // ------------------------------------------------------------------
        // 1. Load workbook
        // ------------------------------------------------------------------
        Console.WriteLine("\n[1/4] Loading workbook...");
        Workbook workbook;
        try
        {
            using var stream = File.OpenRead(WorkbookPath);
            var result = new XlsxFileAdapter().LoadWithWarnings(stream, inspectFeatures: false);
            workbook = result.Workbook;
            if (result.Warnings.Count > 0)
                Console.WriteLine($"  Load warnings ({result.Warnings.Count}): {string.Join("; ", result.Warnings.Take(5))}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: Could not load workbook: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"  Sheets: {workbook.Sheets.Count}");

        // ------------------------------------------------------------------
        // 2. Collect all charts in workbook order
        // ------------------------------------------------------------------
        Console.WriteLine("\n[2/4] Collecting charts...");
        var chartEntries = new List<ChartEntry>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var chart in sheet.Charts)
            {
                chartEntries.Add(new ChartEntry(sheet, chart));
            }
        }
        Console.WriteLine($"  Total charts: {chartEntries.Count}");

        // ------------------------------------------------------------------
        // 3. Discover Excel PNGs (NN = 01..50) and build missing-chart list
        // ------------------------------------------------------------------
        Console.WriteLine("\n[3/4] Discovering Excel PNGs...");
        // Build ordered list: (nn, path, sheetNameEncoded)
        var excelPngList = new List<(int NN, string Path, string SheetToken)>();
        foreach (var file in Directory.EnumerateFiles(ExcelPngDir, "excel_*.png").OrderBy(f => f))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            // Format: excel_NN_<sheet_token>_<chart_token>
            var parts = fileName.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[1], out var nn))
            {
                // Everything after "excel_NN_" up to the last "_Chart_NNN" is the sheet token
                // sheet token is parts[2..^2] joined with _
                var sheetToken = string.Join("_", parts.Skip(2).Take(parts.Length - 4));
                excelPngList.Add((nn, file, sheetToken));
            }
        }
        excelPngList.Sort((a, b) => a.NN.CompareTo(b.NN));
        Console.WriteLine($"  Excel PNGs found: {excelPngList.Count}");

        // Build a NN->path dictionary for direct lookup
        var excelPngs = excelPngList.ToDictionary(x => x.NN, x => x.Path);

        // Detect which Excel NNs correspond to charts FreeX cannot see
        // (sheets that exist in Excel but are absent/empty in FreeX)
        // Strategy: walk FreeX charts and Excel PNGs in parallel, matching sheet name tokens
        // FreeX sheet name -> normalized token (lowercase, spaces to underscores, strip punctuation)
        // Normalize to lowercase alphanumeric only (strip spaces, underscores, punctuation)
        static string NormalizeSheetName(string name)
        {
            var sb2 = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch))
                    sb2.Append(char.ToLowerInvariant(ch));
                // drop ALL non-alphanumeric chars (spaces, underscores, periods, ampersands)
            }
            return sb2.ToString();
        }

        // Map each Excel NN to whether FreeX has a chart at that slot
        // by walking them in parallel
        var freexToExcelNN = new Dictionary<int, int>(); // freexIndex(1-based) -> excelNN
        var missingInFreeX = new List<(int ExcelNN, string ExcelPath, string SheetToken)>();

        {
            int fi = 0; // FreeX chart index (0-based)
            int ei = 0; // Excel PNG list index
            while (fi < chartEntries.Count && ei < excelPngList.Count)
            {
                var freexSheetToken = NormalizeSheetName(chartEntries[fi].Sheet.Name);
                var excelSheetToken = NormalizeSheetName(excelPngList[ei].SheetToken);

                if (freexSheetToken == excelSheetToken ||
                    freexSheetToken.Contains(excelSheetToken) ||
                    excelSheetToken.Contains(freexSheetToken))
                {
                    freexToExcelNN[fi + 1] = excelPngList[ei].NN;
                    fi++;
                    ei++;
                }
                else
                {
                    // Excel has a chart FreeX doesn't — skip this Excel slot
                    missingInFreeX.Add(excelPngList[ei]);
                    ei++;
                }
            }
            // Any remaining Excel PNGs not matched
            while (ei < excelPngList.Count)
                missingInFreeX.Add(excelPngList[ei++]);
        }

        if (missingInFreeX.Count > 0)
        {
            Console.WriteLine($"  Charts in Excel with no FreeX equivalent: {missingInFreeX.Count}");
            foreach (var m in missingInFreeX)
                Console.WriteLine($"    Excel NN={m.ExcelNN:D2} {System.IO.Path.GetFileName(m.ExcelPath)}");
        }

        // ------------------------------------------------------------------
        // 4. Render FreeX PNGs and compare
        // ------------------------------------------------------------------
        Console.WriteLine("\n[4/4] Rendering FreeX charts and comparing...");
        var rows = new List<ReportRow>();

        // Add phantom rows for missing charts (Excel has them, FreeX doesn't)
        foreach (var m in missingInFreeX)
        {
            var fi2 = new FileInfo(m.ExcelPath);
            rows.Add(new ReportRow
            {
                NN = 0,
                ExcelNN = m.ExcelNN,
                SheetName = m.SheetToken,
                ChartName = "(MISSING IN FREEX)",
                ChartType = "?",
                ExcelPngPath = fi2.Length > 0 ? m.ExcelPath : null,
                Rendered = false,
                DiffPercent = fi2.Length > 0 ? 100.0 : -1.0,
                Error = fi2.Length == 0
                    ? "Excel PNG also zero bytes — chart likely invisible"
                    : "Chart exists in Excel but FreeX loaded 0 charts from this sheet",
            });
        }

        for (var index = 0; index < chartEntries.Count; index++)
        {
            // Use the matched Excel NN if available, else fall back to sequential
            var excelNN = freexToExcelNN.TryGetValue(index + 1, out var mapped) ? mapped : index + 1;
            var nn = index + 1; // FreeX NN
            var entry = chartEntries[index];
            Console.WriteLine($"  [{nn:D2}/{chartEntries.Count}] Sheet={entry.Sheet.Name} Chart={entry.Chart.Name ?? "(unnamed)"} Type={entry.Chart.Type} -> ExcelNN={excelNN:D2}");

            var row = new ReportRow
            {
                NN = nn,
                SheetName = entry.Sheet.Name,
                ChartName = entry.Chart.Name ?? "(unnamed)",
                ChartType = entry.Chart.Type.ToString(),
                ExcelNN = excelNN,
            };

            // Excel PNG
            if (excelPngs.TryGetValue(excelNN, out var excelPng))
            {
                var fi = new FileInfo(excelPng);
                if (fi.Length > 0)
                    row.ExcelPngPath = excelPng;
                else
                    row.ExcelPngNote = $"Excel PNG NN={excelNN:D2} is zero bytes (likely blank/hidden chart in Excel)";
            }
            else
                Console.WriteLine($"    WARNING: No Excel PNG for ExcelNN={excelNN:D2}");

            // Build chart data cells from workbook
            var chartDataCells = BuildChartDataCells(workbook, entry.Sheet, entry.Chart);

            // Render FreeX
            try
            {
                var viewport = new ViewportModel(
                    [],
                    [],
                    [],
                    null,
                    [],
                    null,
                    chartDataCells);

                var image = ChartRenderer.Render(entry.Chart, viewport, workbook.Theme, renderScale: 1.5);
                if (image is not null)
                {
                    var freexPath = Path.Combine(OutputDir, $"freex_{nn:D2}.png");
                    SaveImage(image, freexPath);
                    row.FreeXPngPath = freexPath;
                    row.Rendered = true;
                    Console.WriteLine($"    Rendered -> {freexPath}");
                }
                else
                {
                    row.Error = "Renderer returned null (unsupported chart type)";
                    Console.WriteLine($"    NOT RENDERED: {row.Error}");
                }
            }
            catch (Exception ex)
            {
                row.Error = $"{ex.GetType().Name}: {ex.Message}";
                Console.WriteLine($"    ERROR: {row.Error}");
            }

            // Compare
            if (row.Rendered && row.ExcelPngPath is not null && File.Exists(row.ExcelPngPath))
            {
                try
                {
                    row.DiffPercent = ComputeMeanPixelDiff(row.ExcelPngPath, row.FreeXPngPath!, 600, 400);
                }
                catch (Exception ex)
                {
                    row.Error = (row.Error is null ? "" : row.Error + "; ") + $"Diff failed: {ex.Message}";
                    row.DiffPercent = 100.0;
                }
            }
            else if (!row.Rendered)
            {
                row.DiffPercent = 100.0;
            }
            else
            {
                // Excel PNG missing or zero-size — can't compute a valid diff
                // Use -1 as sentinel meaning "no valid comparison available"
                row.DiffPercent = -1.0;
                row.ExcelPngNote = row.ExcelPngNote ?? "Excel PNG missing or zero bytes — no diff computed";
            }

            rows.Add(row);
        }

        // ------------------------------------------------------------------
        // 5. Write REPORT.txt
        // ------------------------------------------------------------------
        var reportPath = Path.Combine(OutputDir, "REPORT.txt");
        // Sort: invalid diff (-1) at bottom, valid diffs worst-first
        var ranked = rows.OrderByDescending(r => r.DiffPercent).ToList();
        WriteReport(reportPath, ranked, chartEntries.Count + missingInFreeX.Count);
        Console.WriteLine($"\nReport: {reportPath}");

        // ------------------------------------------------------------------
        // 6. Write worst_NN composites
        // ------------------------------------------------------------------
        Console.WriteLine($"\nWriting worst {WorstCount} composites...");
        var worstRenderable = ranked
            .Where(r => r.DiffPercent >= 0 && r.ExcelPngPath is not null && File.Exists(r.ExcelPngPath) && new FileInfo(r.ExcelPngPath).Length > 0)
            .Take(WorstCount)
            .ToList();

        foreach (var row in worstRenderable)
        {
            var compositePath = Path.Combine(OutputDir, $"worst_{row.NN:D2}.png");
            try
            {
                var freexLabel = row.Rendered
                    ? $"FreeX renderer (diff={row.DiffPercent:F1}%)"
                    : $"FreeX: NOT RENDERED — {row.Error}";
                WpfSideBySidePng.Write(
                    row.ExcelPngPath!,
                    row.FreeXPngPath,
                    compositePath,
                    new WpfSideBySidePngOptions(
                        600,
                        400,
                        10,
                        28,
                        $"NN={row.NN:D2}  {row.SheetName} / {row.ChartName}  [{row.ChartType}]  diff={row.DiffPercent:F1}%",
                        "Excel (ground truth)",
                        freexLabel));
                Console.WriteLine($"  worst_{row.NN:D2}.png  diff={row.DiffPercent:F1}%  {row.SheetName}/{row.ChartName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Composite failed for NN={row.NN}: {ex.Message}");
            }
        }

        Console.WriteLine("\nDONE.");
        return 0;
    }

    // -----------------------------------------------------------------------
    // Chart data cell builder (mirrors ViewportService.BuildChartDataCells)
    // -----------------------------------------------------------------------
    private static IReadOnlyList<ChartDataCell> BuildChartDataCells(Workbook workbook, Sheet hostSheet, ChartModel chart)
    {
        if (chart.DataRange.Start.Row == 0 && chart.DataRange.End.Row == 0)
            return [];

        var cells = new List<ChartDataCell>();
        var seen = new HashSet<(SheetId, uint, uint)>();

        // Determine source sheet — chart.DataRange references a sheet
        var sourceSheet = workbook.GetSheet(chart.DataRange.Start.Sheet) ?? hostSheet;

        for (uint row = chart.DataRange.Start.Row; row <= chart.DataRange.End.Row; row++)
        {
            for (uint col = chart.DataRange.Start.Col; col <= chart.DataRange.End.Col; col++)
            {
                if (!seen.Add((sourceSheet.Id, row, col)))
                    continue;

                var cell = sourceSheet.GetCell(row, col);
                if (cell is null)
                {
                    cells.Add(new ChartDataCell(sourceSheet.Id, row, col, "", BlankValue.Instance));
                    continue;
                }

                var displayText = CellToDisplayText(cell.Value);
                cells.Add(new ChartDataCell(sourceSheet.Id, row, col, displayText, cell.Value));
            }
        }

        return cells;
    }

    private static string CellToDisplayText(ScalarValue? value) => value switch
    {
        null => "",
        BlankValue => "",
        TextValue tv => tv.Value,
        NumberValue nv => nv.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue bv => bv.Value ? "TRUE" : "FALSE",
        DateTimeValue dv => dv.Value.ToString(CultureInfo.InvariantCulture),
        ErrorValue ev => ev.Code,
        _ => ""
    };

    // -----------------------------------------------------------------------
    // Image utilities
    // -----------------------------------------------------------------------
    private static void SaveImage(ImageSource image, string path)
    {
        if (image is not BitmapSource bitmap)
            throw new InvalidOperationException($"Unexpected image type: {image.GetType().FullName}");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // -----------------------------------------------------------------------
    // Report writer
    // -----------------------------------------------------------------------
    private static void WriteReport(string path, IReadOnlyList<ReportRow> ranked, int totalCharts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FreeX vs Excel Chart Fidelity Report");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Workbook: {WorkbookPath}");
        sb.AppendLine($"Total FreeX charts: {totalCharts}");
        sb.AppendLine($"Total Excel PNGs: 50");
        int rendered = ranked.Count(r => r.Rendered);
        int notRendered = ranked.Count(r => !r.Rendered);
        int noDiff = ranked.Count(r => r.DiffPercent < 0);
        sb.AppendLine($"FreeX rendered: {rendered}/{totalCharts}");
        sb.AppendLine($"NOT RENDERED (missing in FreeX): {notRendered}");
        sb.AppendLine($"No valid Excel PNG to diff (zero-byte Excel PNGs): {noDiff}");
        sb.AppendLine();

        // Missing-in-FreeX summary
        var missingRows = ranked.Where(r => !r.Rendered).OrderBy(r => r.NN).ToList();
        if (missingRows.Count > 0)
        {
            sb.AppendLine("=== MISSING IN FREEX (chart in Excel but FreeX loaded 0 from sheet) ===");
            foreach (var r in missingRows)
                sb.AppendLine($"  ExcelNN={r.ExcelNN:D2}  Sheet={r.SheetName}  Error={r.Error}");
            sb.AppendLine();
        }

        // No-diff summary (Excel PNG is blank/zero)
        var noDiffRows = ranked.Where(r => r.Rendered && r.DiffPercent < 0).OrderBy(r => r.NN).ToList();
        if (noDiffRows.Count > 0)
        {
            sb.AppendLine("=== NO DIFF AVAILABLE (Excel PNG zero bytes — invisible/hidden chart in Excel) ===");
            foreach (var r in noDiffRows)
                sb.AppendLine($"  FreeX NN={r.NN:D2}  ExcelNN={r.ExcelNN:D2}  Sheet={r.SheetName}  Note={r.ExcelPngNote}");
            sb.AppendLine();
        }

        // Ranked table (only rows with valid diff)
        var validRows = ranked.Where(r => r.DiffPercent >= 0).OrderByDescending(r => r.DiffPercent).ToList();
        sb.AppendLine("=== RANKED BY DIFF% (worst first, only charts with valid Excel PNG) ===");
        sb.AppendLine($"{"NN",-4} {"ExNN",-5} {"DiffPct",7}  {"Rendered",-8}  {"Sheet",-35}  {"Type",-22}  ExcelPng");
        sb.AppendLine(new string('-', 148));

        foreach (var r in validRows)
        {
            var excelPngName = r.ExcelPngPath is not null ? Path.GetFileName(r.ExcelPngPath) : "(missing)";
            var renderedStr = r.Rendered ? "yes" : "NO";
            sb.AppendLine($"{r.NN.ToString("D2"),-4} {r.ExcelNN.ToString("D2"),-5} {r.DiffPercent,7:F1}%  {renderedStr,-8}  {Trunc(r.SheetName, 35),-35}  {Trunc(r.ChartType, 22),-22}  {excelPngName}");
            if (r.Error is not null)
                sb.AppendLine($"     NOTE: {r.Error}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Trunc(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}

// -----------------------------------------------------------------------
// Data structures
// -----------------------------------------------------------------------
internal sealed class ChartEntry(Sheet sheet, ChartModel chart)
{
    public Sheet Sheet { get; } = sheet;
    public ChartModel Chart { get; } = chart;
}

internal sealed class ReportRow
{
    public int NN { get; set; }          // FreeX chart index (1-based)
    public int ExcelNN { get; set; }     // matching Excel PNG NN
    public string SheetName { get; set; } = "";
    public string ChartName { get; set; } = "";
    public string ChartType { get; set; } = "";
    public string? ExcelPngPath { get; set; }
    public string? ExcelPngNote { get; set; }
    public string? FreeXPngPath { get; set; }
    public bool Rendered { get; set; }
    /// <summary>Mean pixel diff % (0-100), or -1 if no valid Excel PNG to compare against.</summary>
    public double DiffPercent { get; set; } = 100.0;
    public string? Error { get; set; }
}
