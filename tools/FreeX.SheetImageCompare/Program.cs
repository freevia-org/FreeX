using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Host;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static FreeX.ToolsShared.Wpf.WpfImageDiff;

/// <summary>
/// FreeX Sheet Image Compare — renders each worksheet of an .xlsx to a PNG using
/// FreeX's real print/render pipeline for visual fidelity comparison vs Excel.
/// Discovery tool only; no product-code changes.
/// </summary>
internal static class Program
{
    private const string DefaultWorkbookPath = @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";
    private const double RenderDpi = 144.0; // 1.5× of 96 dpi for higher resolution PNGs

    [STAThread]
    public static int Main(string[] args)
    {
        // Pin culture so number/date formatting is predictable
        var culture = CultureInfo.GetCultureInfo("en-US");
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var xlsxPath = args.Length > 0 ? args[0] : DefaultWorkbookPath;

        // Derive output dir from workbook file name
        var baseName = Path.GetFileNameWithoutExtension(xlsxPath).ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "");
        var freexOutputDir = Path.Combine(Path.GetTempPath(), $"{baseName}-freex");
        var excelInputDir = Path.Combine(Path.GetTempPath(), $"{baseName}-excel");
        Directory.CreateDirectory(freexOutputDir);

        Console.WriteLine("=== FreeX Sheet Image Compare ===");
        Console.WriteLine($"Workbook    : {xlsxPath}");
        Console.WriteLine($"FreeX output: {freexOutputDir}");
        Console.WriteLine($"Excel input : {excelInputDir} (optional — for diff mode)");
        Console.WriteLine();

        // ------------------------------------------------------------------
        // 1. Load workbook
        // ------------------------------------------------------------------
        Console.WriteLine("[1/3] Loading workbook...");
        Workbook workbook;
        try
        {
            using var stream = File.OpenRead(xlsxPath);
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
        // 2. Build viewport service (parameterless ctor, confirmed by test usage)
        // ------------------------------------------------------------------
        var viewportService = new ViewportService();

        // ------------------------------------------------------------------
        // 3. Render each visible sheet to PNG
        // ------------------------------------------------------------------
        Console.WriteLine("\n[2/3] Rendering sheets...");

        var sheetResults = new List<SheetResult>();
        int sheetIndex = 1;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
            {
                Console.WriteLine($"  [{sheetIndex:D2}] {sheet.Name} — SKIPPED (hidden)");
                sheetIndex++;
                continue;
            }

            var safeName = SanitizeFileName(sheet.Name);
            var outFileName = $"freex_{sheetIndex:D2}_{safeName}.png";
            var outPath = Path.Combine(freexOutputDir, outFileName);

            Console.Write($"  [{sheetIndex:D2}/{workbook.Sheets.Count}] {sheet.Name} ... ");

            var result = new SheetResult
            {
                NN = sheetIndex,
                SheetName = sheet.Name,
                FreeXPngPath = outPath,
                FreeXPngFileName = outFileName,
            };

            try
            {
                var doc = PrintRenderer.RenderWorksheet(
                    workbook,
                    sheet.Id,
                    viewportService,
                    ignorePrintArea: true);

                result.TotalPageCount = doc.Pages.Count;

                if (doc.Pages.Count == 0)
                {
                    Console.WriteLine($"0 pages — SKIPPED (empty sheet)");
                    result.Skipped = true;
                    result.SkipReason = "Zero pages (empty sheet)";
                    sheetResults.Add(result);
                    sheetIndex++;
                    continue;
                }

                // Rasterize the first page
                var pageContent = doc.Pages[0];
                pageContent.GetPageRoot(forceReload: false);
                var fixedPage = pageContent.Child
                    ?? throw new InvalidOperationException("FixedPage was null after GetPageRoot");

                var pageSize = new Size(
                    fixedPage.Width > 0 && !double.IsNaN(fixedPage.Width)
                        ? fixedPage.Width
                        : doc.DocumentPaginator.PageSize.Width,
                    fixedPage.Height > 0 && !double.IsNaN(fixedPage.Height)
                        ? fixedPage.Height
                        : doc.DocumentPaginator.PageSize.Height);

                fixedPage.Measure(pageSize);
                fixedPage.Arrange(new Rect(pageSize));
                fixedPage.UpdateLayout();

                // Scale factor: 1.5× (RenderDpi / 96)
                double scale = RenderDpi / 96.0;
                int pixelW = Math.Max(1, (int)Math.Ceiling(pageSize.Width * scale));
                int pixelH = Math.Max(1, (int)Math.Ceiling(pageSize.Height * scale));

                var rtb = new RenderTargetBitmap(pixelW, pixelH, RenderDpi, RenderDpi, PixelFormats.Pbgra32);
                rtb.Render(fixedPage);
                rtb.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fileStream = File.Create(outPath);
                encoder.Save(fileStream);

                result.Rendered = true;
                Console.WriteLine($"{doc.Pages.Count} page(s) -> {outFileName}");
            }
            catch (Exception ex)
            {
                result.Error = $"{ex.GetType().Name}: {ex.Message}";
                Console.WriteLine($"ERROR: {result.Error}");
            }

            sheetResults.Add(result);
            sheetIndex++;
        }

        // ------------------------------------------------------------------
        // 4. Optional diff mode or simple index
        // ------------------------------------------------------------------
        Console.WriteLine("\n[3/3] Building report...");

        bool hasDiffDir = Directory.Exists(excelInputDir) &&
            Directory.EnumerateFiles(excelInputDir, "excel_*.png").Any();

        string reportPath;
        if (hasDiffDir)
            reportPath = RunDiffMode(workbook, sheetResults, freexOutputDir, excelInputDir);
        else
            reportPath = WriteSimpleIndex(sheetResults, freexOutputDir, xlsxPath);

        // Print summary
        int rendered = sheetResults.Count(r => r.Rendered);
        int skipped = sheetResults.Count(r => r.Skipped);
        int errors = sheetResults.Count(r => r.Error != null && !r.Skipped);

        Console.WriteLine();
        Console.WriteLine($"Rendered : {rendered}");
        Console.WriteLine($"Skipped  : {skipped} (hidden/empty)");
        Console.WriteLine($"Errors   : {errors}");
        Console.WriteLine($"Report   : {reportPath}");
        Console.WriteLine("\nDONE.");
        return errors > 0 ? 2 : 0;
    }

    // -----------------------------------------------------------------------
    // Simple index (no Excel PNGs to compare against)
    // -----------------------------------------------------------------------
    private static string WriteSimpleIndex(
        IReadOnlyList<SheetResult> results,
        string outputDir,
        string xlsxPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FreeX Sheet Image Render Index");
        sb.AppendLine($"Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Workbook  : {xlsxPath}");
        sb.AppendLine();
        sb.AppendLine($"{"NN",-4}  {"Pages",-6}  {"Status",-10}  {"Sheet",-35}  PNG");
        sb.AppendLine(new string('-', 100));

        foreach (var r in results)
        {
            string status = r.Rendered ? "OK" : r.Skipped ? "SKIPPED" : "ERROR";
            string pngName = r.Rendered ? r.FreeXPngFileName : r.Error ?? r.SkipReason ?? "";
            sb.AppendLine($"{r.NN.ToString("D2"),-4}  {(r.Rendered ? r.TotalPageCount.ToString() : "-"),-6}  {status,-10}  {Trunc(r.SheetName, 35),-35}  {pngName}");
        }

        var reportPath = Path.Combine(outputDir, "REPORT.txt");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return reportPath;
    }

    // -----------------------------------------------------------------------
    // Diff mode: compare FreeX PNGs against Excel PNGs
    // -----------------------------------------------------------------------
    private static string RunDiffMode(
        Workbook workbook,
        IReadOnlyList<SheetResult> results,
        string freexOutputDir,
        string excelInputDir)
    {
        Console.WriteLine($"  Diff mode: Excel PNGs found in {excelInputDir}");

        // Discover Excel PNGs: format excel_NN_<sheetToken>.png
        var excelPngs = new SortedDictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(excelInputDir, "excel_*.png"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var parts = stem.Split('_');
            if (parts.Length >= 3 && int.TryParse(parts[1], out var nn))
                excelPngs[nn] = file;
        }
        Console.WriteLine($"  Excel PNGs discovered: {excelPngs.Count}");

        var rows = new List<DiffRow>();

        foreach (var r in results)
        {
            if (!r.Rendered)
            {
                rows.Add(new DiffRow
                {
                    NN = r.NN,
                    SheetName = r.SheetName,
                    Status = r.Skipped ? "SKIPPED" : "ERROR",
                    Error = r.Skipped ? r.SkipReason : r.Error,
                    DiffPercent = -1,
                });
                continue;
            }

            var row = new DiffRow
            {
                NN = r.NN,
                SheetName = r.SheetName,
                FreeXPng = r.FreeXPngPath,
                TotalPages = r.TotalPageCount,
                Status = "OK",
            };

            if (excelPngs.TryGetValue(r.NN, out var excelPng) && File.Exists(excelPng))
            {
                row.ExcelPng = excelPng;
                try
                {
                    row.DiffPercent = ComputeMeanPixelDiff(excelPng, r.FreeXPngPath!, 800, 600);
                }
                catch (Exception ex)
                {
                    row.Error = $"Diff failed: {ex.Message}";
                    row.DiffPercent = 100.0;
                }
            }
            else
            {
                row.DiffPercent = -1;
                row.Error = "No matching Excel PNG";
            }

            rows.Add(row);
        }

        // Write composites for worst-diff sheets
        const int WorstCount = 10;
        var validDiffs = rows
            .Where(r => r.DiffPercent >= 0 && r.ExcelPng != null)
            .OrderByDescending(r => r.DiffPercent)
            .Take(WorstCount)
            .ToList();

        foreach (var row in validDiffs)
        {
            var compositePath = Path.Combine(freexOutputDir, $"worst_{row.NN:D2}.png");
            try
            {
                WriteSideBySide(row.ExcelPng!, row.FreeXPng, compositePath, row);
                Console.WriteLine($"  worst_{row.NN:D2}.png  diff={row.DiffPercent:F1}%  {row.SheetName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Composite failed NN={row.NN}: {ex.Message}");
            }
        }

        // Write REPORT.txt
        var sb = new StringBuilder();
        sb.AppendLine("FreeX vs Excel Sheet Fidelity Report");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine("=== RANKED BY DIFF% (worst first) ===");
        sb.AppendLine($"{"NN",-4}  {"Diff%",7}  {"Pages",-6}  {"Status",-8}  Sheet");
        sb.AppendLine(new string('-', 90));

        foreach (var r in rows.OrderByDescending(r => r.DiffPercent))
        {
            var diffStr = r.DiffPercent >= 0 ? $"{r.DiffPercent:F1}%" : "  N/A";
            sb.AppendLine($"{r.NN.ToString("D2"),-4}  {diffStr,7}  {(r.TotalPages > 0 ? r.TotalPages.ToString() : "-"),-6}  {r.Status,-8}  {r.SheetName}");
            if (r.Error != null)
                sb.AppendLine($"       NOTE: {r.Error}");
        }

        var reportPath = Path.Combine(freexOutputDir, "REPORT.txt");
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        return reportPath;
    }

    private static void WriteSideBySide(string excelPath, string? freexPath, string outPath, DiffRow row)
    {
        const int ThumbW = 700, ThumbH = 500;
        const int Padding = 10;
        const int LabelH = 30;
        int totalW = ThumbW * 2 + Padding * 3;
        int totalH = ThumbH + Padding * 2 + LabelH * 2;

        var excelBmp = File.Exists(excelPath) ? ResizeTo(LoadBitmap(excelPath), ThumbW, ThumbH) : CreateWhite(ThumbW, ThumbH);
        var freexBmp = freexPath != null && File.Exists(freexPath) ? ResizeTo(LoadBitmap(freexPath), ThumbW, ThumbH) : CreateWhite(ThumbW, ThumbH);

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(240, 240, 240)), null, new Rect(0, 0, totalW, totalH));

            var headerText = $"NN={row.NN:D2}  {row.SheetName}  diff={row.DiffPercent:F1}%";
            ctx.DrawText(MakeText(headerText, 13, Brushes.Black, FontWeights.SemiBold), new Point(Padding, 4));

            int yImg = LabelH;
            int xLeft = Padding;
            int xRight = Padding * 2 + ThumbW;

            ctx.DrawText(MakeText("Excel (ground truth)", 11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xLeft, yImg + ThumbH + 4));
            ctx.DrawText(MakeText($"FreeX renderer  diff={row.DiffPercent:F1}%", 11, Brushes.DarkSlateGray, FontWeights.Normal), new Point(xRight, yImg + ThumbH + 4));

            ctx.DrawImage(excelBmp, new Rect(xLeft, yImg, ThumbW, ThumbH));
            ctx.DrawImage(freexBmp, new Rect(xRight, yImg, ThumbW, ThumbH));
        }

        var rtb = new RenderTargetBitmap(totalW, totalH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(outPath);
        encoder.Save(stream);
    }

    private static FormattedText MakeText(string text, double size, Brush brush, FontWeight weight) =>
        new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static string SanitizeFileName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
                sb.Append(ch);
            else if (ch == ' ' || ch == '_')
                sb.Append('_');
            // drop other chars
        }
        return sb.Length > 0 ? sb.ToString() : "sheet";
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
internal sealed class SheetResult
{
    public int NN { get; set; }
    public string SheetName { get; set; } = "";
    public string? FreeXPngPath { get; set; }
    public string FreeXPngFileName { get; set; } = "";
    public bool Rendered { get; set; }
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public string? Error { get; set; }
    public int TotalPageCount { get; set; }
}

internal sealed class DiffRow
{
    public int NN { get; set; }
    public string SheetName { get; set; } = "";
    public string? FreeXPng { get; set; }
    public string? ExcelPng { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public double DiffPercent { get; set; } = -1;
    public int TotalPages { get; set; }
}
