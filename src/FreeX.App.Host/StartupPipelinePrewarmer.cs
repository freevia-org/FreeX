using System.IO;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Warms the XLSX open/save pipeline on a background thread shortly after startup.
///
/// <para>The very first workbook open in a freshly launched process pays a large one-time cost —
/// JIT compilation of the load/save code paths, type/static initialization inside ClosedXML, lazy
/// assembly loading, and <see cref="System.Text.RegularExpressions.RegexOptions.Compiled"/> regex
/// compilation in the schema normalizers.  On a cold process this adds roughly 6-7 seconds to the
/// first open; subsequent opens are fast because everything is already JITted and initialized.</para>
///
/// <para>Running a representative save -> load -> patch-save cycle on a throwaway in-memory workbook
/// pays that cost up front, off the UI thread, so the user's first real open is already warm.  The
/// cost is per-code-path, not per-cell, so a tiny workbook warms the same methods a large file hits.
/// Everything here is isolated (its own <see cref="XlsxFileAdapter"/>, streams, and recalc engine),
/// so it can never touch the application's live workbook or services, and any failure is swallowed —
/// prewarming is a latency optimization, never a correctness dependency.</para>
/// </summary>
internal static partial class StartupPipelinePrewarmer
{
    private static int _started;

    /// <summary>
    /// Kicks off the background prewarm exactly once per process.  Safe to call from app startup
    /// after the main window is shown; returns immediately.
    /// </summary>
    public static void StartBackgroundPrewarm()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the initial window render settle before competing for CPU; the prewarm is a
                // latency optimization, not part of the startup-critical path.
                await Task.Delay(TimeSpan.FromMilliseconds(750)).ConfigureAwait(false);
                Prewarm();
            }
            catch
            {
                // Best-effort: a prewarm failure must never affect the running application.
            }
        });
    }

    private static void Prewarm()
    {
        var workbook = CreateRepresentativeWorkbook();

        var adapter = new XlsxFileAdapter();
        using var package = new MemoryStream();
        adapter.Save(workbook, package); // warm the write path (ClosedXML build + schema normalizers)

        package.Position = 0;
        // warm the read path (ClosedXML parse + worksheet materialization + feature inspection)
        var loaded = adapter.LoadWithWarnings(package, inspectFeatures: true).Workbook;
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out _);

        // Warm formula evaluation with a throwaway engine so application recalc state is untouched.
        try
        {
            new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(loaded);
        }
        catch
        {
            // Formula warmup is optional; the open/save warmup above is the important part.
        }

        // Warm the cell-patch save fast path.
        var sheet = loaded.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("warm"));
        using var patched = new MemoryStream();
        adapter.Save(loaded, patched);
    }

    private static Workbook CreateRepresentativeWorkbook()
    {
        var workbook = new Workbook("Prewarm");
        var styleId = RegisterPrewarmStyle(workbook);

        for (var sheetIndex = 1; sheetIndex <= 2; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet{sheetIndex}");
            for (var row = 1u; row <= 32u; row++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"S{sheetIndex}-R{row}"));
                for (var col = 2u; col <= 8u; col++)
                {
                    var value = sheetIndex * 100_000 + row * 100 + col;
                    var cell = col == 8 && row % 8 == 0
                        ? Cell.FromFormula($"B{row}+C{row}")
                        : Cell.FromValue(new NumberValue(value));
                    cell.Value = new NumberValue(value);
                    if (col % 3 == 0)
                        cell.StyleId = styleId;
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
                }
            }
        }

        return workbook;
    }

    private static StyleId RegisterPrewarmStyle(Workbook workbook)
    {
        var style = CellStyle.Default.Clone();
        style.FillColor = CellColor.FromArgb(221, 235, 247);
        style.FillPatternStyle = CellFillPatternStyle.Solid;
        style.NumberFormat = "#,##0.00";
        style.BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(91, 155, 213));
        return workbook.RegisterStyle(style);
    }
}
