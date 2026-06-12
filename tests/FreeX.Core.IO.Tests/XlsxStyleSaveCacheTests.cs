using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the ClosedXML per-save style cache (P3 fix: StyleId → XLStyleValue deduplication).
/// The cache replaces ~15 individual property setter calls per styled cell with a single
/// <c>SetStyle(XLStyleValue, propagate: false)</c> call for every repeat of a previously-seen
/// StyleId.
/// </summary>
public sealed class XlsxStyleSaveCacheTests
{
    // Correctness: saving a workbook with 5 distinct styles spread across many cells and
    // reloading must produce the same formatting on every cell — the fast-path SetStyle
    // call must be semantically identical to the full ApplyStyle path.
    [Fact]
    public void Save_WithManyStyledCells_AllStylesRoundTripCorrectly()
    {
        const int rows = 200;
        const int cols = 10; // 5 distinct styles × 2 cols each

        var workbook = new Workbook("StyleCacheRoundTrip");
        var sheet = workbook.AddSheet("Data");

        // Define 5 distinct non-default styles that exercise different sub-domains.
        var boldRed = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(0xC0, 0, 0),
            FontSize = 14,
        });

        var italicGreenFill = workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(0xD9, 0xEA, 0xD3),
            FillPatternStyle = CellFillPatternStyle.Solid,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var borderedWrap = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0x70, 0xAD, 0x47)),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0x70, 0xAD, 0x47)),
            WrapText = true,
        });

        var rightAlignFormat = workbook.RegisterStyle(new CellStyle
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            NumberFormat = "#,##0.00",
        });

        var strikethroughSmall = workbook.RegisterStyle(new CellStyle
        {
            Strikethrough = true,
            FontSize = 8,
            FontName = "Arial",
        });

        var styleIdForCol = new[]
        {
            boldRed, boldRed,
            italicGreenFill, italicGreenFill,
            borderedWrap, borderedWrap,
            rightAlignFormat, rightAlignFormat,
            strikethroughSmall, strikethroughSmall,
        };

        // Populate all cells: first row uses ApplyStyle slow path (first encounter),
        // subsequent rows use SetStyle fast path (cache hit).
        for (var row = 1u; row <= rows; row++)
        {
            for (var col = 1u; col <= cols; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.SetCell(address, new NumberValue(row * cols + col));
                sheet.GetCell(row, col)!.StyleId = styleIdForCol[col - 1];
            }
        }

        // Save and reload.
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);

        // Spot-check first row (slow-path applied) and last row (fast-path applied).
        // Every row should have the same style.
        foreach (var row in new uint[] { 1, 2, 50, 100, 199, 200 })
        {
            CheckCellStyle(loaded, loadedSheet, row, 1, boldRed: true);
            CheckCellStyle(loaded, loadedSheet, row, 3, italicGreenFill: true);
            CheckCellStyle(loaded, loadedSheet, row, 5, borderedWrap: true);
            CheckCellStyle(loaded, loadedSheet, row, 7, rightAlignFormat: true);
            CheckCellStyle(loaded, loadedSheet, row, 9, strikethroughSmall: true);
        }
    }

    private static void CheckCellStyle(
        Workbook workbook,
        Sheet sheet,
        uint row,
        uint col,
        bool boldRed = false,
        bool italicGreenFill = false,
        bool borderedWrap = false,
        bool rightAlignFormat = false,
        bool strikethroughSmall = false)
    {
        var cell = sheet.GetCell(row, col);
        cell.Should().NotBeNull($"row {row} col {col} should exist");
        var style = workbook.GetStyle(cell!.StyleId);

        if (boldRed)
        {
            style.Bold.Should().BeTrue($"row {row} col {col} should be bold");
            style.FontColor.Should().Be(new CellColor(0xC0, 0, 0), $"row {row} col {col} font color");
            style.FontSize.Should().BeApproximately(14, 0.01, $"row {row} col {col} font size");
        }

        if (italicGreenFill)
        {
            style.Italic.Should().BeTrue($"row {row} col {col} should be italic");
            style.FillColor.Should().Be(new CellColor(0xD9, 0xEA, 0xD3), $"row {row} col {col} fill color");
            style.HorizontalAlignment.Should().Be(HorizontalAlignment.Center, $"row {row} col {col} alignment");
        }

        if (borderedWrap)
        {
            style.BorderTop.Style.Should().Be(BorderStyle.Thin, $"row {row} col {col} border top");
            style.BorderBottom.Style.Should().Be(BorderStyle.Thin, $"row {row} col {col} border bottom");
            style.WrapText.Should().BeTrue($"row {row} col {col} wrap text");
        }

        if (rightAlignFormat)
        {
            style.HorizontalAlignment.Should().Be(HorizontalAlignment.Right, $"row {row} col {col} h-align");
            style.VerticalAlignment.Should().Be(VerticalAlignment.Top, $"row {row} col {col} v-align");
            style.NumberFormat.Should().Be("#,##0.00", $"row {row} col {col} number format");
        }

        if (strikethroughSmall)
        {
            style.Strikethrough.Should().BeTrue($"row {row} col {col} strikethrough");
            style.FontSize.Should().BeApproximately(8, 0.01, $"row {row} col {col} font size");
            style.FontName.Should().Be("Arial", $"row {row} col {col} font name");
        }
    }

    // Correctness: distinct styles must not bleed into each other.  Two adjacent columns with
    // different styles must each retain their own formatting after round-trip.
    [Fact]
    public void Save_DistinctAdjacentStyles_DoNotBleedIntoEachOther()
    {
        var workbook = new Workbook("StyleBleedCheck");
        var sheet = workbook.AddSheet("S");

        var blueStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(0, 0, 0xFF),
            FillPatternStyle = CellFillPatternStyle.Solid,
            Bold = true,
        });

        var greenStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(0, 0xFF, 0),
            FillPatternStyle = CellFillPatternStyle.Solid,
            Italic = true,
        });

        // Alternate between the two styles so the fast path is exercised.
        for (var row = 1u; row <= 50; row++)
        {
            var blueAddress = new CellAddress(sheet.Id, row, 1);
            sheet.SetCell(blueAddress, new NumberValue(row));
            sheet.GetCell(row, 1)!.StyleId = blueStyle;

            var greenAddress = new CellAddress(sheet.Id, row, 2);
            sheet.SetCell(greenAddress, new NumberValue(row + 100));
            sheet.GetCell(row, 2)!.StyleId = greenStyle;
        }

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);

        for (var row = 1u; row <= 50; row++)
        {
            var blueCell = loadedSheet.GetCell(row, 1);
            var blueLoaded = loaded.GetStyle(blueCell!.StyleId);
            blueLoaded.Bold.Should().BeTrue($"row {row} col 1 must be bold (blue)");
            blueLoaded.Italic.Should().BeFalse($"row {row} col 1 must not be italic (bleed from green)");
            blueLoaded.FillColor.Should().Be(new CellColor(0, 0, 0xFF), $"row {row} col 1 fill");

            var greenCell = loadedSheet.GetCell(row, 2);
            var greenLoaded = loaded.GetStyle(greenCell!.StyleId);
            greenLoaded.Italic.Should().BeTrue($"row {row} col 2 must be italic (green)");
            greenLoaded.Bold.Should().BeFalse($"row {row} col 2 must not be bold (bleed from blue)");
            greenLoaded.FillColor.Should().Be(new CellColor(0, 0xFF, 0), $"row {row} col 2 fill");
        }
    }

    /// <summary>
    /// BenchmarkFact: saves a model workbook with 5 distinct styles across a large number of cells
    /// and measures elapsed time + allocations.  This documents the save cost and provides a
    /// baseline for the per-save style-value cache (P3 fix).  The test is skipped in normal runs
    /// (requires FREEX_RUN_BENCHMARK_TESTS=1).
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_SaveManyStyledCells_ReportsTiming()
    {
        const int rows = 1000;
        const int cols = 30;        // 5 styles × 6 cols each
        const int styleCount = 5;
        const int iterations = 3;

        var workbook = new Workbook("StyleCacheBench");
        var sheet = workbook.AddSheet("Data");

        var styles = new StyleId[styleCount];
        styles[0] = workbook.RegisterStyle(new CellStyle { Bold = true, FontSize = 14, FontColor = new CellColor(0xC0, 0, 0) });
        styles[1] = workbook.RegisterStyle(new CellStyle { Italic = true, FillColor = new CellColor(0xD9, 0xEA, 0xD3), FillPatternStyle = CellFillPatternStyle.Solid });
        styles[2] = workbook.RegisterStyle(new CellStyle { BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)), WrapText = true });
        styles[3] = workbook.RegisterStyle(new CellStyle { HorizontalAlignment = HorizontalAlignment.Right, NumberFormat = "#,##0.00" });
        styles[4] = workbook.RegisterStyle(new CellStyle { Strikethrough = true, FontName = "Arial", FontSize = 8 });

        for (var row = 1u; row <= rows; row++)
        {
            for (var col = 1u; col <= cols; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                sheet.SetCell(address, new NumberValue(row * cols + col));
                sheet.GetCell(row, col)!.StyleId = styles[(col - 1) % styleCount];
            }
        }

        var adapter = new XlsxFileAdapter();

        // Warm up (JIT + ClosedXML internal caches).
        using (var warmup = new MemoryStream())
            adapter.Save(workbook, warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream();
            var step = Stopwatch.StartNew();
            adapter.Save(workbook, stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(v => v).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_STYLE_CACHE " +
            $"rows={rows} cols={cols} distinct_styles={styleCount} " +
            $"total_styled_cells={rows * cols:N0} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }
}
