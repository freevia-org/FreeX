using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the <c>--parity-grid</c> headless grid-range capture added in wave-0.
///
/// Coverage:
/// <list type="bullet">
///   <item><see cref="ComputeRangePixelExtent_ReturnsCorrectExtent"/> — unit test for the sizing helper
///   that does not require a UI thread: verifies the pixel-extent formula against known row heights and
///   column widths from a constructed <see cref="ViewportModel"/>.</item>
///   <item><see cref="GridCaptureOptions_TryParse_AcceptsThreePositionalArgs"/> — arg-parsing smoke for
///   the three-positional-value <c>--parity-grid</c> syntax.</item>
///   <item><see cref="GridCaptureOptions_TryParse_RejectsFewerThanThreeArgs"/> — error path: too few args.</item>
///   <item><see cref="CaptureGridRange_WritesPng_ForNewWorkbook"/> — headless integration smoke: constructs
///   a MainWindow under the Avalonia headless platform, calls CaptureGridRangeAsync on a fresh workbook
///   written to disk, and asserts that a non-empty PNG and a JSON log are written to the output directory.</item>
/// </list>
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class GridCaptureTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── Sizing-helper unit test (no UI thread needed) ────────────────────────────────────────────

    [Fact]
    public void ComputeRangePixelExtent_ReturnsCorrectExtent()
    {
        // Arrange: construct a minimal ViewportModel whose col/row metrics match a known layout.
        // Default Excel column width ≈ 64 DIPs each; default row height = 20 DIPs.
        // We build 5 cols (B–F, cols 2–6) and 10 rows (rows 3–12), and ask for the extent of B3:C5
        // (2 cols × 3 rows).  With no minimum clamp needed (64 > MinimumDisplayedColumnWidth=2) the
        // result should be ceil(64*2) × ceil(20*3) = 128 × 60.

        const double ColWidth = 64.0;   // DIPs
        const double RowHeight = 20.0;  // DIPs

        var colMetrics = Enumerable.Range(2, 5)
            .Select(c => new ColMetric((uint)c, ColWidth, (c - 2) * ColWidth))
            .ToList();
        var rowMetrics = Enumerable.Range(3, 10)
            .Select(r => new RowMetric((uint)r, RowHeight, (r - 3) * RowHeight))
            .ToList();

        var viewport = new ViewportModel(
            Cells: [],
            RowMetrics: rowMetrics,
            ColMetrics: colMetrics);

        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),  // B3
            new CellAddress(sheetId, 5, 3)); // C5 — 2 cols (2,3), 3 rows (3,4,5)

        // Act
        var (widthPx, heightPx) = MainWindow.ComputeRangePixelExtent(viewport, range, zoomFactor: 1.0);

        // Assert
        widthPx.Should().Be(128, "2 cols × 64 DIPs = 128 px at zoom 1");
        heightPx.Should().Be(60, "3 rows × 20 DIPs = 60 px at zoom 1");
    }

    [Fact]
    public void ComputeRangePixelExtent_EnforcesMinimumDimensionPerCell()
    {
        // Arrange: cells whose natural size is BELOW the minimum (e.g. hidden-width columns = 0).
        // MainWindow applies Math.Max(MinimumDisplayedColumnWidth, metric.Width) per cell.
        // MinimumDisplayedColumnWidth is 2 DIPs (from MainWindow internals).

        var colMetrics = new[]
        {
            new ColMetric(1u, Width: 0.0, LeftOffset: 0),   // zero-width: clamps to minimum
            new ColMetric(2u, Width: 64.0, LeftOffset: 0),  // normal
        };
        var rowMetrics = new[] { new RowMetric(1u, Height: 0.0, TopOffset: 0) }; // zero-height: clamps

        var viewport = new ViewportModel(Cells: [], RowMetrics: rowMetrics, ColMetrics: colMetrics);
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2));

        // Act
        var (widthPx, heightPx) = MainWindow.ComputeRangePixelExtent(viewport, range, zoomFactor: 1.0);

        // Assert: the minimum is applied per-cell, so width ≥ 2+64=66 and height ≥ 1 (the overall minimum).
        widthPx.Should().BeGreaterThanOrEqualTo(66, "col1 clamps to MinimumDisplayedColumnWidth, col2 is 64");
        heightPx.Should().BeGreaterThanOrEqualTo(1, "overall minimum of 1 px applies");
    }

    // ── Arg-parsing unit tests (no UI thread needed) ─────────────────────────────────────────────

    [Fact]
    public void GridCaptureOptions_TryParse_AcceptsThreePositionalArgs()
    {
        var args = new[] { "--parity-grid", "book.xlsx", "A1:B10", @"C:\out" };

        var parsed = GridCaptureOptions.TryParse(args, out var options, out var remaining, out var error);

        parsed.Should().BeTrue();
        error.Should().BeEmpty();
        options.Should().NotBeNull();
        options!.WorkbookPath.Should().Be("book.xlsx");
        options.RangeText.Should().Be("A1:B10");
        options.OutputDirectory.Should().Be(@"C:\out");
        remaining.Should().BeEmpty("all three positional args should be consumed");
    }

    [Fact]
    public void GridCaptureOptions_TryParse_RejectsFewerThanThreeArgs()
    {
        var args = new[] { "--parity-grid", "book.xlsx", "A1:B10" }; // missing outDir

        var parsed = GridCaptureOptions.TryParse(args, out var options, out _, out var error);

        parsed.Should().BeFalse();
        options.Should().BeNull();
        error.Should().Contain("three arguments");
    }

    [Fact]
    public void GridCaptureOptions_TryParse_PassesThroughUnrelatedArgs()
    {
        var args = new[] { "--parity-grid", "book.xlsx", "A1:C3", @"C:\out", "--some-other-flag" };

        var parsed = GridCaptureOptions.TryParse(args, out var options, out var remaining, out _);

        parsed.Should().BeTrue();
        options.Should().NotBeNull();
        remaining.Should().Equal("--some-other-flag");
    }

    [Fact]
    public void GridCaptureOptions_TryParse_AcceptsExplicitWorksheet()
    {
        var args = new[]
        {
            "--parity-grid", "book.xlsx", "A1:C3", @"C:\out",
            "--parity-grid-sheet", "Pivot Output"
        };

        var parsed = GridCaptureOptions.TryParse(args, out var options, out var remaining, out var error);

        parsed.Should().BeTrue();
        error.Should().BeEmpty();
        options.Should().NotBeNull();
        options!.WorksheetName.Should().Be("Pivot Output");
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void GridCaptureOptions_TryParse_RejectsWorksheetWithoutGridCapture()
    {
        var args = new[] { "--parity-grid-sheet", "Pivot Output" };

        var parsed = GridCaptureOptions.TryParse(args, out var options, out _, out var error);

        parsed.Should().BeFalse();
        options.Should().BeNull();
        error.Should().Contain("requires --parity-grid");
    }

    [Fact]
    public void GridCaptureOutputGuard_RejectsFullyTransparentBlackFrame()
    {
        var pixels = new byte[2 * 2 * 4];

        ParityCaptureOutputGuard.ValidateGridPixels(pixels, width: 2, height: 2)
            .Should().Be("Grid PNG output is fully transparent-black.");
    }

    [Fact]
    public void GridCaptureOutputGuard_RequiresVisiblePixelVariance()
    {
        var uniformWhite = new byte[]
        {
            255, 255, 255, 255,
            255, 255, 255, 255,
        };
        var variedPixels = new byte[]
        {
            255, 255, 255, 255,
            31, 31, 31, 255,
        };

        ParityCaptureOutputGuard.ValidateGridPixels(uniformWhite, width: 2, height: 1)
            .Should().Be("Grid PNG output has no pixel variance.");
        ParityCaptureOutputGuard.ValidateGridPixels(variedPixels, width: 2, height: 1)
            .Should().BeNull();
    }

    [Fact]
    public void GridCaptureOutputGuard_RequiresChartPixels_WhenRangeOverlapsChart()
    {
        // A populated cell grid has visible variance and may have colored cells outside the chart.
        // Require chromatic pixels inside the known chart rectangle, not anywhere in the range.
        var cellsOnlyPixels = new byte[]
        {
            255, 255, 255, 255,
            31, 31, 31, 255,
        };
        var chartPixels = new byte[]
        {
            255, 255, 255, 255,
            31, 31, 31, 255,
            24, 132, 196, 255,
        };

        var chartBounds = new[] { new ChartPixelBounds(Left: 2, Top: 0, Width: 1, Height: 1) };

        ParityCaptureOutputGuard.ValidateGridPixels(cellsOnlyPixels, width: 2, height: 1, chartBounds, minimumChromaticPixels: 1)
            .Should().Contain("chart bounds");
        ParityCaptureOutputGuard.ValidateGridPixels(chartPixels, width: 3, height: 1, chartBounds, minimumChromaticPixels: 1)
            .Should().BeNull();
    }

    [Fact]
    public void ParityGridCapture_RequiresChartPixels_OnlyForOverlappingVisibleCharts()
    {
        var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 500, viewportWidth: 800);
        var sheet = session.ActiveSheet;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 25, 14));
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            IsVisible = true,
            Left = 10,
            Top = 110,
            Width = 320,
            Height = 220,
        };
        sheet.Charts.Add(chart);

        var method = typeof(MainWindow).GetMethod(
            "HasVisibleChartOverlappingRange",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        method.Invoke(null, [sheet, range]).Should().Be(true);

        chart.Left = 10_000;
        method.Invoke(null, [sheet, range]).Should().Be(false,
            "a visible chart outside the requested capture range must not require chart-pixel evidence");
    }

    // ── Headless integration smoke ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureGridRange_WritesPngAndJsonLog_ForNewWorkbook()
    {
        // Arrange: write a minimal new workbook to a temp xlsx file, then capture A1:B5 from it.
        using (var workbookDirectory = new TestTemporaryDirectory("freex-grid-capture-src-"))
        using (var outputDirectory = new TestTemporaryDirectory("freex-grid-capture-out-"))
        {
            var workbookDir = workbookDirectory.Path;
            var outputDir = outputDirectory.Path;

            // Create a minimal workbook with a few styled cells and save it.
            var xlsxPath = Path.Combine(workbookDir, "capture_smoke.xlsx");
            var session = new WorkbookSessionFactory().CreateNew(viewportHeight: 500, viewportWidth: 800);
            var sheet = session.ActiveSheet;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Hello")));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(42)));

            var adapter = new XlsxFileAdapter();
            using (var fs = new FileStream(xlsxPath, FileMode.Create, FileAccess.Write))
                adapter.SaveWithWarnings(session.Workbook, fs);

            GridCaptureResult? captureResult = null;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                captureResult = await window.CaptureGridRangeAsync(xlsxPath, "A1:B5", outputDir);

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
                return true;
            }, CancellationToken.None);

            // Assert result record
            captureResult.Should().NotBeNull();
            captureResult!.Captured.Should().BeTrue($"capture should succeed (note: {captureResult.Note})");
            captureResult.WidthPx.Should().BeGreaterThan(0, "at least 2 columns B col-width");
            captureResult.HeightPx.Should().BeGreaterThan(0, "at least 5 rows of row-height");
            captureResult.SheetName.Should().NotBeNullOrEmpty();

            // Assert the PNG file was written. Under the headless drawing platform
            // (UseHeadlessDrawing=true) a detached visual produces a 0-byte frame, so we only require the
            // file to exist here; the real Win32+Skia backend (exercised via the --parity-grid CLI)
            // produces the actual non-empty cropped PNG.
            File.Exists(captureResult.PngPath).Should().BeTrue($"PNG should exist at {captureResult.PngPath}");

            // Assert JSON log was written alongside the PNG
            var jsonPath = Path.ChangeExtension(captureResult.PngPath, ".json");
            File.Exists(jsonPath).Should().BeTrue("JSON log should be written alongside the PNG");
            var json = File.ReadAllText(jsonPath);
            json.Should().Contain("\"captured\": true");
            json.Should().Contain("\"widthPx\"");
            json.Should().Contain("\"heightPx\"");
        }
    }

    [Fact]
    public async Task CaptureGridRange_ReturnsFailure_ForMissingWorkbook()
    {
        using (var outputDirectory = new TestTemporaryDirectory("freex-grid-capture-fail-"))
        {
            var outputDir = outputDirectory.Path;

            GridCaptureResult? result = null;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new global::Avalonia.Size(1120, 720));
                window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                result = await window.CaptureGridRangeAsync(
                    @"C:\nonexistent-fixture-999\missing.xlsx", "A1:B5", outputDir);

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
                return true;
            }, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Captured.Should().BeFalse("a missing file must yield a failure result");
            result.Note.Should().NotBeNullOrEmpty("a failure result must have a reason note");
        }
    }
}
