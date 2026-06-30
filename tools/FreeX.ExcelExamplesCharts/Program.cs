using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.ToolsShared.Wpf;

/// <summary>
/// Chart fidelity census + comparison for a real workbook (default ExcelExamples1.xlsx).
/// Phase A (always): load in FreeX, enumerate every chart, render each to PNG, record
/// load/render outcomes (type, renderable?, rendered?, visibly-blank?).
/// Phase B (if Excel COM available): open the SAME file in Excel, export every chart PNG as
/// ground truth, diff against FreeX renders (matched per sheet by chart index).
/// Phase C (round-trip): FreeX save -> reopen in Excel, count charts retained per sheet.
/// </summary>
internal static class Program
{
    private const string ExcelProcessName = "EXCEL";
    private const int XlOpenXmlWorkbook = 51;

    [STAThread]
    public static int Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var workbookPath = args.Length > 0 ? args[0] : @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";
        var outputDir = args.Length > 1
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "excelexamples-charts");
        var skipExcel = args.Contains("--no-excel");
        Directory.CreateDirectory(outputDir);
        var freexPngDir = Path.Combine(outputDir, "freex");
        var excelPngDir = Path.Combine(outputDir, "excel");
        Directory.CreateDirectory(freexPngDir);
        Directory.CreateDirectory(excelPngDir);

        Console.WriteLine("=== FreeX ExcelExamples Chart Fidelity ===");
        Console.WriteLine($"Workbook: {workbookPath}");
        Console.WriteLine($"Output  : {outputDir}");

        // ---- Phase A: load + render census ----
        Workbook workbook;
        var warnings = new List<string>();
        try
        {
            using var stream = File.OpenRead(workbookPath);
            var result = new XlsxFileAdapter().LoadWithWarnings(stream, inspectFeatures: false);
            workbook = result.Workbook;
            warnings.AddRange(result.Warnings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL load: {ex.Message}");
            return 1;
        }

        var entries = new List<ChartRow>();
        var perSheetCount = new Dictionary<string, int>();
        foreach (var sheet in workbook.Sheets)
        {
            var idx = 0;
            foreach (var chart in sheet.Charts)
            {
                idx++;
                entries.Add(new ChartRow
                {
                    Sheet = sheet.Name,
                    SheetIndexInSheet = idx,
                    Name = chart.Name ?? "(unnamed)",
                    Type = chart.Type.ToString(),
                    Family = ChartTypeSupport.IsChartExFamily(chart.Type) ? "chartEx" : "classic",
                    Renderable = ChartTypeSupport.IsRenderable(chart.Type),
                    ChartRef = chart,
                    SheetRef = sheet,
                });
            }
            if (idx > 0) perSheetCount[sheet.Name] = idx;
        }

        Console.WriteLine($"\n[A] FreeX loaded {entries.Count} charts across {perSheetCount.Count} sheets. Warnings: {warnings.Count}");
        foreach (var w in warnings.Take(10)) Console.WriteLine($"    warn: {w}");

        // Render each FreeX chart to PNG
        foreach (var row in entries)
        {
            try
            {
                var cells = BuildChartDataCells(workbook, row.SheetRef!, row.ChartRef!);
                var vp = new ViewportModel([], [], [], null, [], null, cells);
                var img = ChartRenderer.Render(row.ChartRef!, vp, workbook.Theme, renderScale: 1.5);
                if (img is BitmapSource bmp)
                {
                    var path = Path.Combine(freexPngDir, $"{Sanitize(row.Sheet)}_{row.SheetIndexInSheet:D2}.png");
                    SaveImage(bmp, path);
                    row.FreeXPng = path;
                    row.Rendered = true;
                    row.FreeXBlank = IsVisiblyBlank(path);
                }
                else
                {
                    row.RenderError = "Renderer returned null";
                }
            }
            catch (Exception ex)
            {
                row.RenderError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        var renderedCount = entries.Count(e => e.Rendered);
        var blankCount = entries.Count(e => e.Rendered && e.FreeXBlank);
        Console.WriteLine($"    Rendered {renderedCount}/{entries.Count}; visibly-blank {blankCount}.");

        // ---- Phase B: Excel ground truth + diff ----
        if (!skipExcel)
        {
            try
            {
                ExportExcelGroundTruth(workbookPath, entries, excelPngDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[B] Excel ground-truth export failed: {ex.Message}");
            }
        }

        // Compute diffs
        foreach (var row in entries)
        {
            if (row.Rendered && row.ExcelPng is not null && File.Exists(row.ExcelPng))
            {
                try { row.DiffPercent = ComputeMeanPixelDiff(row.ExcelPng, row.FreeXPng!); }
                catch (Exception ex) { row.DiffNote = $"diff failed: {ex.Message}"; }
            }
        }

        // ---- Phase C: round-trip ----
        var roundTrip = new Dictionary<string, int>();
        if (!skipExcel)
        {
            try
            {
                var savedPath = Path.Combine(outputDir, "freex-roundtrip.xlsx");
                using (var input = File.OpenRead(workbookPath))
                using (var output = File.Create(savedPath))
                {
                    var wb2 = new XlsxFileAdapter().Load(input);
                    new XlsxFileAdapter().Save(wb2, output);
                }
                Console.WriteLine($"\n[C] FreeX round-trip saved -> {savedPath}");
                roundTrip = CountChartsPerSheetInExcel(savedPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[C] Round-trip failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        WriteReport(outputDir, workbookPath, entries, perSheetCount, roundTrip, warnings);
        Console.WriteLine($"\nReport: {Path.Combine(outputDir, "REPORT.md")}");
        WriteWorstComposites(outputDir, entries);
        Console.WriteLine("DONE.");
        return 0;
    }

    // ---------------- Excel ground truth ----------------
    private static void ExportExcelGroundTruth(string workbookPath, List<ChartRow> entries, string excelPngDir)
    {
        var baseline = GetExcelPids();
        object? excel = null;
        var owned = new HashSet<int>();
        try
        {
            excel = CreateExcel();
            owned = GetExcelPids().Except(baseline).ToHashSet();
            dynamic app = excel;
            dynamic wb = app.Workbooks.Open(workbookPath, 0, true); // ReadOnly positional
            Console.WriteLine("\n[B] Opened workbook in Excel; exporting chart PNGs...");

            int sheetCount = (int)wb.Worksheets.Count;
            int exported = 0, exportFailures = 0;
            for (int si = 1; si <= sheetCount; si++)
            {
                dynamic ws = wb.Worksheets.Item(si);
                string sheetName = (string)ws.Name;
                dynamic chartObjs = ws.ChartObjects();
                int n = (int)chartObjs.Count;
                for (int ci = 1; ci <= n; ci++)
                {
                    dynamic chart = chartObjs.Item(ci).Chart;
                    var path = Path.Combine(excelPngDir, $"{Sanitize(sheetName)}_{ci:D2}.png");
                    if (TryExportChart(chart, path))
                    {
                        exported++;
                        var match = entries.FirstOrDefault(e => e.Sheet == sheetName && e.SheetIndexInSheet == ci);
                        if (match is not null) match.ExcelPng = path;
                    }
                    else exportFailures++;
                }
            }
            Console.WriteLine($"    Excel exported {exported} chart PNGs ({exportFailures} export failures).");
            wb.Close(false);
        }
        finally
        {
            if (excel is not null)
            {
                try { ((dynamic)excel).Quit(); } catch { }
                try { if (Marshal.IsComObject(excel)) Marshal.FinalReleaseComObject(excel); } catch { }
            }
            KillExcel(owned);
        }
    }

    private static Dictionary<string, int> CountChartsPerSheetInExcel(string workbookPath)
    {
        var counts = new Dictionary<string, int>();
        var baseline = GetExcelPids();
        object? excel = null;
        var owned = new HashSet<int>();
        try
        {
            excel = CreateExcel();
            owned = GetExcelPids().Except(baseline).ToHashSet();
            dynamic app = excel;
            dynamic wb = app.Workbooks.Open(workbookPath, 0, true);
            int sheetCount = (int)wb.Worksheets.Count;
            for (int si = 1; si <= sheetCount; si++)
            {
                dynamic ws = wb.Worksheets.Item(si);
                string sheetName = (string)ws.Name;
                int n = (int)ws.ChartObjects().Count;
                if (n > 0) counts[sheetName] = n;
            }
            wb.Close(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    reopen-count failed: {ex.Message}");
        }
        finally
        {
            if (excel is not null)
            {
                try { ((dynamic)excel).Quit(); } catch { }
                try { if (Marshal.IsComObject(excel)) Marshal.FinalReleaseComObject(excel); } catch { }
            }
            KillExcel(owned);
        }
        return counts;
    }

    private static object CreateExcel()
    {
        var t = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel.Application not registered.");
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var excel = Activator.CreateInstance(t)!;
                dynamic app = excel;
                app.Visible = false;
                app.DisplayAlerts = false;
                try { app.EnableEvents = false; } catch { }
                try { app.AutomationSecurity = 3; } catch { }
                return excel;
            }
            catch (Exception ex) when (attempt < 3) { last = ex; Thread.Sleep(2000); }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException($"Excel activation failed: {last?.Message}", last);
    }

    private static bool TryExportChart(dynamic chart, string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var ok = Convert.ToBoolean(chart.Export(path, "PNG"), CultureInfo.InvariantCulture);
            return ok && File.Exists(path);
        }
        catch { return false; }
    }

    private static HashSet<int> GetExcelPids() =>
        Process.GetProcessesByName(ExcelProcessName).Select(p => p.Id).ToHashSet();

    private static void KillExcel(HashSet<int> owned)
    {
        if (owned.Count == 0) return;
        foreach (var p in Process.GetProcessesByName(ExcelProcessName))
        {
            if (!owned.Contains(p.Id)) continue;
            try { p.Kill(true); p.WaitForExit(5000); } catch { }
        }
    }

    // ---------------- chart data ----------------
    private static IReadOnlyList<ChartDataCell> BuildChartDataCells(Workbook workbook, Sheet hostSheet, ChartModel chart)
    {
        if (chart.DataRange.Start.Row == 0 && chart.DataRange.End.Row == 0)
            return [];
        var cells = new List<ChartDataCell>();
        var seen = new HashSet<(SheetId, uint, uint)>();
        var source = workbook.GetSheet(chart.DataRange.Start.Sheet) ?? hostSheet;
        for (uint r = chart.DataRange.Start.Row; r <= chart.DataRange.End.Row; r++)
            for (uint c = chart.DataRange.Start.Col; c <= chart.DataRange.End.Col; c++)
            {
                if (!seen.Add((source.Id, r, c))) continue;
                var cell = source.GetCell(r, c);
                if (cell is null) { cells.Add(new ChartDataCell(source.Id, r, c, "", BlankValue.Instance)); continue; }
                cells.Add(new ChartDataCell(source.Id, r, c, ToText(cell.Value), cell.Value));
            }
        return cells;
    }

    private static string ToText(ScalarValue? v) => v switch
    {
        null or BlankValue => "",
        TextValue tv => tv.Value,
        NumberValue nv => nv.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue bv => bv.Value ? "TRUE" : "FALSE",
        DateTimeValue dv => dv.Value.ToString(CultureInfo.InvariantCulture),
        ErrorValue ev => ev.Code,
        _ => ""
    };

    // ---------------- image utils ----------------
    private static void SaveImage(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var s = File.Create(path);
        enc.Save(s);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var s = File.OpenRead(path);
        var dec = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var f = dec.Frames[0];
        return f.Format == PixelFormats.Bgra32 ? f : new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
    }

    private static bool IsVisiblyBlank(string path)
    {
        const int W = 64, H = 64;
        var bmp = ResizeTo(LoadBitmap(path), W, H);
        var px = new byte[W * H * 4];
        bmp.CopyPixels(px, W * 4, 0);
        int nonWhite = 0;
        for (int i = 0; i < W * H; i++)
        {
            int o = i * 4;
            double a = px[o + 3] / 255.0;
            double b = px[o] * a + 255 * (1 - a);
            double g = px[o + 1] * a + 255 * (1 - a);
            double r = px[o + 2] * a + 255 * (1 - a);
            if (b < 245 || g < 245 || r < 245) nonWhite++;
        }
        return nonWhite < (W * H) * 0.01;
    }

    private static double ComputeMeanPixelDiff(string excelPath, string freexPath)
    {
        const int W = 600, H = 400;
        return WpfImageDiff.ComputeMeanPixelDiff(excelPath, freexPath, W, H);
    }

    private static BitmapSource ResizeTo(BitmapSource src, int w, int h)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, w, h));
            double scale = Math.Min((double)w / src.PixelWidth, (double)h / src.PixelHeight);
            double dw = src.PixelWidth * scale, dh = src.PixelHeight * scale;
            ctx.DrawImage(src, new System.Windows.Rect((w - dw) / 2, (h - dh) / 2, dw, dh));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
    }

    // ---------------- report ----------------
    private static void WriteReport(
        string outputDir, string workbookPath, List<ChartRow> entries,
        Dictionary<string, int> perSheetCount, Dictionary<string, int> roundTrip, List<string> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Chart fidelity — {Path.GetFileName(workbookPath)}");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine($"- FreeX charts loaded: **{entries.Count}** ({entries.Count(e => e.Family == "classic")} classic, {entries.Count(e => e.Family == "chartEx")} chartEx)");
        sb.AppendLine($"- Renderable type: {entries.Count(e => e.Renderable)}/{entries.Count}");
        sb.AppendLine($"- Rendered to PNG: {entries.Count(e => e.Rendered)}/{entries.Count}");
        sb.AppendLine($"- Visibly-blank render: {entries.Count(e => e.Rendered && e.FreeXBlank)}");
        var withDiff = entries.Where(e => e.DiffPercent >= 0).ToList();
        if (withDiff.Count > 0)
            sb.AppendLine($"- Diffed vs Excel: {withDiff.Count}; mean diff {withDiff.Average(e => e.DiffPercent):F1}%, max {withDiff.Max(e => e.DiffPercent):F1}%");
        sb.AppendLine($"- Load warnings: {warnings.Count}");
        sb.AppendLine();

        sb.AppendLine("## Round-trip (charts retained per sheet after FreeX save, counted by Excel)");
        foreach (var kv in perSheetCount.OrderBy(k => k.Key))
        {
            var rt = roundTrip.GetValueOrDefault(kv.Key, -1);
            var status = rt < 0 ? "(not measured)" : (rt == kv.Value ? "OK" : $"LOST {kv.Value - rt}");
            sb.AppendLine($"- {kv.Key}: orig {kv.Value} -> roundtrip {(rt < 0 ? "?" : rt.ToString())}  {status}");
        }
        sb.AppendLine();

        sb.AppendLine("## Per-chart (worst diff first)");
        sb.AppendLine("| Sheet | # | Name | Type | Family | Renderable | Rendered | Blank | Diff% | Note |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in entries.OrderByDescending(e => e.DiffPercent).ThenBy(e => e.Sheet))
        {
            var diff = r.DiffPercent >= 0 ? r.DiffPercent.ToString("F1") : "-";
            var note = string.Join(" ", new[] { r.RenderError, r.DiffNote, r.ExcelPng is null && r.Rendered ? "no-excel-png" : null }.Where(x => !string.IsNullOrEmpty(x)));
            sb.AppendLine($"| {r.Sheet} | {r.SheetIndexInSheet} | {r.Name} | {r.Type} | {r.Family} | {(r.Renderable ? "y" : "N")} | {(r.Rendered ? "y" : "N")} | {(r.FreeXBlank ? "BLANK" : "")} | {diff} | {note} |");
        }
        File.WriteAllText(Path.Combine(outputDir, "REPORT.md"), sb.ToString(), Encoding.UTF8);
    }

    private static void WriteWorstComposites(string outputDir, List<ChartRow> entries)
    {
        var worst = entries.Where(e => e.DiffPercent >= 0 && e.ExcelPng is not null && e.FreeXPng is not null)
            .OrderByDescending(e => e.DiffPercent).Take(12).ToList();
        var dir = Path.Combine(outputDir, "worst");
        Directory.CreateDirectory(dir);
        foreach (var r in worst)
        {
            try
            {
                const int TW = 600, TH = 400, Pad = 10, Lab = 24;
                int totalW = TW * 2 + Pad * 3, totalH = TH + Pad * 2 + Lab;
                var ex = ResizeTo(LoadBitmap(r.ExcelPng!), TW, TH);
                var fx = ResizeTo(LoadBitmap(r.FreeXPng!), TW, TH);
                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), null, new System.Windows.Rect(0, 0, totalW, totalH));
                    var ft = new FormattedText($"{r.Sheet}/{r.Name} [{r.Type}] diff={r.DiffPercent:F1}%  (left=Excel, right=FreeX)",
                        CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 13, Brushes.Black, 1.0);
                    ctx.DrawText(ft, new System.Windows.Point(Pad, 4));
                    ctx.DrawImage(ex, new System.Windows.Rect(Pad, Lab, TW, TH));
                    ctx.DrawImage(fx, new System.Windows.Rect(Pad * 2 + TW, Lab, TW, TH));
                }
                var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                SaveImage(rtb, Path.Combine(dir, $"worst_{r.DiffPercent:000.0}_{Sanitize(r.Sheet)}_{r.SheetIndexInSheet:D2}.png"));
            }
            catch { }
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        return sb.ToString();
    }
}

internal sealed class ChartRow
{
    public string Sheet { get; set; } = "";
    public int SheetIndexInSheet { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Family { get; set; } = "";
    public bool Renderable { get; set; }
    public bool Rendered { get; set; }
    public bool FreeXBlank { get; set; }
    public string? FreeXPng { get; set; }
    public string? ExcelPng { get; set; }
    public double DiffPercent { get; set; } = -1.0;
    public string? RenderError { get; set; }
    public string? DiffNote { get; set; }
    public ChartModel? ChartRef { get; set; }
    public Sheet? SheetRef { get; set; }
}
