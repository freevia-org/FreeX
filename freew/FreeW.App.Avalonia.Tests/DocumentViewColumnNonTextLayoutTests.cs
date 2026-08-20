using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for AV-COL-NONTXT: multi-column body layout for non-text content in DocumentView.
/// Verifies that tables, inline images, inline charts, inline WordArt, and inline SmartArt
/// that flow into column 2 of a 2-column section receive the correct column-band X position
/// rather than being pinned to column 0's X (causing overlap).
///
/// Bug fixed: AG1 (tables), AG2 (inline images), AG3 (inline charts/WordArt/SmartArt + text),
/// AG4 (declared-width tables fit within the column).  WPF FreeW + Word are the ground truth.
///
/// Test strategy: use a very short page so that a 50pt filler inline image overflows column 0's
/// textAreaHeight (45pt = 60 DIP), causing the target content to be in column 1 via
/// ReserveContentY's slot-push logic.  This works regardless of headless font metrics.
/// </summary>
public sealed class DocumentViewColumnNonTextLayoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 2-column page: standard US letter 612×792 pt, 1" margins.
    /// DocumentView minimum-height floor: _pageHeightPx = max(400, 792*96/72) = 1056 DIP.
    /// textAreaHeight = max(40, 1056 - 96 - 96) = 864 DIP.
    /// contentWidth   = (612 - 72 - 72) pt × 96/72 = 624 DIP.
    /// colGap         = 36 pt × 96/72 = 48 DIP.
    /// colWidth       = (624 - 48) / 2 = 288 DIP.
    /// contentLeft    = 72 pt × 96/72 + pageLeft (≈ centre of the 816-DIP measure width).
    ///   With Measure(816, …): pageLeft = max(MinHorzGutter, (816-816)/2) = MinHorzGutter ≈ 0,
    ///   so contentLeft = 96 DIP.  band0 = [96, 384]; band1 = [432, 720].
    /// </summary>
    private static PageSettings TwoColumnPage() => new()
    {
        WidthPt = 612, HeightPt = 792,
        MarginLeftPt = 72, MarginRightPt = 72,
        MarginTopPt = 72, MarginBottomPt = 72,
        ColumnCount = 2,
        ColumnSpacingPt = 36,
    };

    /// <summary>Same page in single-column mode (regression guard).</summary>
    private static PageSettings SingleColumnPage() => new()
    {
        WidthPt = 612, HeightPt = 792,
        MarginLeftPt = 72, MarginRightPt = 72,
        MarginTopPt = 72, MarginBottomPt = 72,
        ColumnCount = 1,
    };

    /// <summary>Minimal 4×4 PNG stand-in for a real image.</summary>
    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 100, 50));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Builds a 2-block document: block 0 = filler inline image (700 pt tall), block 1 = target.
    /// <para>
    /// Strategy for reliable column-2 placement regardless of headless font metrics:
    /// DocumentView clamps _pageHeightPx to max(400, pageHeight_DIP), so for a 792pt page
    /// _pageHeightPx = 1056 DIP and textAreaHeight = max(40, 1056-96-96) = 864 DIP.
    /// A 700pt (≈ 933 DIP) filler image starts at contentY=0 (posInPage=0, no push) and ends at
    /// _layoutContentY = 933 + 6(gap) = 939 DIP.  slot = (int)(939/864) = 1 → column 1. ✓
    /// The target content lands at contentY≈939 DIP in slot 1 provided its height does not
    /// push it past slot boundary (939 % 864 = 75 DIP; headroom = 789 DIP).
    /// For single-column layout colIndex = slot % 1 = 0 always — ColumnLeftFor returns _contentLeft.
    /// </para>
    /// </summary>
    private static TextDocument DocWithTargetInColumn2(PageSettings page, Block contentBlock)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt         = page.WidthPt;
        doc.Page.HeightPt        = page.HeightPt;
        doc.Page.MarginLeftPt    = page.MarginLeftPt;
        doc.Page.MarginRightPt   = page.MarginRightPt;
        doc.Page.MarginTopPt     = page.MarginTopPt;
        doc.Page.MarginBottomPt  = page.MarginBottomPt;
        doc.Page.ColumnCount     = page.ColumnCount;
        doc.Page.ColumnSpacingPt = page.ColumnSpacingPt > 0 ? page.ColumnSpacingPt : 36;

        // Filler image: 700 pt = 933.3 DIP > textAreaHeight (864 DIP for standard 11" page).
        // contentY = 0 (slot 0 = column 0). After: _layoutContentY = 939.3 DIP → slot 1 = column 1.
        var fillerPara = new Paragraph();
        var fillerImg = new InlineImage(SmallPng(), 100, 700) { Wrapping = ImageWrapping.Inline };
        fillerPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = fillerImg });
        doc.Blocks.Add(fillerPara);  // block 0: filler in column 0
        doc.Blocks.Add(contentBlock); // block 1: target in column 1
        return doc;
    }

    /// <summary>
    /// Builds a single-block document using <see cref="SingleColumnPage"/> settings.
    /// The target content is the only block, so it's always in column 0 (single-column).
    /// </summary>
    private static TextDocument DocSingleCol(Block contentBlock)
    {
        var page = SingleColumnPage();
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Page.WidthPt         = page.WidthPt;
        doc.Page.HeightPt        = page.HeightPt;
        doc.Page.MarginLeftPt    = page.MarginLeftPt;
        doc.Page.MarginRightPt   = page.MarginRightPt;
        doc.Page.MarginTopPt     = page.MarginTopPt;
        doc.Page.MarginBottomPt  = page.MarginBottomPt;
        doc.Page.ColumnCount     = 1;
        doc.Page.ColumnSpacingPt = 0;
        doc.Blocks.Add(contentBlock);
        return doc;
    }

    /// <summary>Builds an inline (non-floating) image paragraph with the given dimensions in pt.</summary>
    private static Paragraph InlineImageParagraph(double widthPt, double heightPt)
    {
        var para = new Paragraph();
        var image = new InlineImage(SmallPng(), widthPt, heightPt) { Wrapping = ImageWrapping.Inline };
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = image });
        return para;
    }

    /// <summary>Builds an inline chart paragraph (small: 30pt height to stay in one slot).</summary>
    private static Paragraph InlineChartParagraph(double heightPt = 30)
    {
        var para = new Paragraph();
        var chart = Chart.Create(ChartKind.Column,
            new[] { "A", "B", "C" }, new[] { 10.0, 20.0, 15.0 }, "S1", "Col2 Chart");
        chart.WidthPt  = 100;
        chart.HeightPt = heightPt;
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
        return para;
    }

    /// <summary>Builds a simple 2-column, 2-row Table (no declared column widths).</summary>
    private static Table SimpleTable(int tableCols = 2, int tableRows = 2) =>
        Table.Create(tableCols, tableRows);

    /// <summary>Builds a Table with declared column widths summing to totalDeclaredPt.</summary>
    private static Table DeclaredWidthTable(int tableCols = 3, double totalDeclaredPt = 340)
    {
        var t = Table.Create(tableCols, 2);
        var perCol = totalDeclaredPt / tableCols;
        for (var c = 0; c < tableCols; c++)
            t.ColumnWidthsPt.Add(perCol);
        return t;
    }

    // ── AG1: table in column 2 gets column-2 X band ───────────────────────────────────────────────

    [Fact]
    public async Task AG1_Table_in_column2_rects_land_in_column2_band()
    {
        (double Left, double Width) band0 = default;
        (double Left, double Width) band1 = default;
        bool allRectsInBand1 = false;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            var table = SimpleTable(2, 2);
            var doc = DocWithTargetInColumn2(TwoColumnPage(), table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            band1 = view.LayoutColumnBand(1);

            // block 1 = the table (block 0 = filler).
            var tableBlockIdx = 1;
            var tableCellRects = view.TableCellRects
                .Where(r => r.Block == tableBlockIdx)
                .Select(r => r.Rect)
                .ToList();
            rectCount = tableCellRects.Count;

            const double tol = 3.0;
            allRectsInBand1 = tableCellRects.Count > 0 && tableCellRects.All(r =>
                r.X >= band1.Left - tol && r.X < band1.Left + band1.Width + tol);
        });

        if (!ran) return;

        rectCount.Should().BeGreaterThan(0, "table must produce at least one cell rect");
        allRectsInBand1.Should().BeTrue(
            $"table rects that flow into column 2 must start in column-2 band " +
            $"[{band1.Left:F1}, {band1.Left + band1.Width:F1}], not column-1 band " +
            $"[{band0.Left:F1}, {band0.Left + band0.Width:F1}]");
    }

    [Fact]
    public async Task AG1_Table_in_column2_rects_NOT_in_column1_band()
    {
        (double Left, double Width) band0 = default;
        bool anyRectInBand0 = true;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            var table = SimpleTable(2, 2);
            var doc = DocWithTargetInColumn2(TwoColumnPage(), table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            var (band1Left, band1Width) = view.LayoutColumnBand(1);

            var tableBlockIdx = 1;
            var tableCellRects = view.TableCellRects
                .Where(r => r.Block == tableBlockIdx)
                .Select(r => r.Rect)
                .ToList();
            rectCount = tableCellRects.Count;

            const double tol = 3.0;
            // None of the rects should be exclusively in column-0's band (that would be the bug).
            anyRectInBand0 = tableCellRects.Any(r =>
                r.X >= band0.Left - tol && r.X < band0.Left + band0.Width + tol
                && !(r.X >= band1Left - tol && r.X < band1Left + band1Width + tol));
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0);
        anyRectInBand0.Should().BeFalse(
            "table rects in column 2 must NOT land in column 1's X band (that is the overlap bug)");
    }

    // ── AG1: single-column regression guard ───────────────────────────────────────────────────────

    [Fact]
    public async Task AG1_SingleColumn_table_rects_stay_in_content_left_band()
    {
        (double Left, double Width) band0 = default;
        bool allInBand0 = false;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            var table = SimpleTable(2, 2);
            var doc = DocSingleCol(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);

            var tableBlockIdx = 0;
            var tableCellRects = view.TableCellRects
                .Where(r => r.Block == tableBlockIdx)
                .Select(r => r.Rect)
                .ToList();
            rectCount = tableCellRects.Count;

            const double tol = 3.0;
            allInBand0 = tableCellRects.Count > 0 && tableCellRects.All(r =>
                r.X >= band0.Left - tol && r.X < band0.Left + band0.Width + tol);
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0, "table must produce cell rects");
        allInBand0.Should().BeTrue(
            "single-column: table rects must stay in the one content column (no regression)");
    }

    // ── AG2: inline image in column 2 gets column-2 X band ────────────────────────────────────────

    [Fact]
    public async Task AG2_InlineImage_in_column2_lands_in_column2_X_band()
    {
        (double Left, double Width) band0 = default;
        (double Left, double Width) band1 = default;
        bool targetInBand1 = false;
        int imageCount = 0;

        var ran = await OnUiThread(() =>
        {
            // Target: 30pt-tall image (40 DIP < 54 DIP headroom so it stays in the same slot).
            var imgPara = InlineImageParagraph(widthPt: 80, heightPt: 30);
            var doc = DocWithTargetInColumn2(TwoColumnPage(), imgPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            band1 = view.LayoutColumnBand(1);

            var imageRects = view.InlineImageRects;
            imageCount = imageRects.Count;

            const double tol = 3.0;
            // There are 2 images: [0] = filler (in column 0), [1] = target (should be in column 1).
            targetInBand1 = imageCount >= 2 &&
                imageRects[1].X >= band1.Left - tol &&
                imageRects[1].X < band1.Left + band1.Width + tol;
        });

        if (!ran) return;
        imageCount.Should().BeGreaterThanOrEqualTo(2, "filler + target images must both be in InlineImageRects");
        targetInBand1.Should().BeTrue(
            $"target inline image must land in band1 X [{band1.Left:F1}, {band1.Left + band1.Width:F1}], " +
            $"not band0 [{band0.Left:F1}, {band0.Left + band0.Width:F1}]");
    }

    [Fact]
    public async Task AG2_SingleColumn_InlineImage_stays_at_contentLeft()
    {
        (double Left, double Width) band0 = default;
        bool imageInBand0 = false;

        var ran = await OnUiThread(() =>
        {
            var imgPara = InlineImageParagraph(widthPt: 80, heightPt: 30);
            var doc = DocSingleCol(imgPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            var imageRects = view.InlineImageRects;

            const double tol = 3.0;
            imageInBand0 = imageRects.Count > 0 && imageRects.All(r =>
                r.X >= band0.Left - tol && r.X < band0.Left + band0.Width + tol);
        });

        if (!ran) return;
        imageInBand0.Should().BeTrue(
            "single-column: inline image must remain in the one content column (no regression)");
    }

    // ── AG3: inline chart in column 2 gets column-2 X band ────────────────────────────────────────

    [Fact]
    public async Task AG3_InlineChart_in_column2_lands_in_column2_X_band()
    {
        (double Left, double Width) band0 = default;
        (double Left, double Width) band1 = default;
        bool chartInBand1 = false;
        int chartCount = 0;

        var ran = await OnUiThread(() =>
        {
            var chartPara = InlineChartParagraph(heightPt: 30);
            var doc = DocWithTargetInColumn2(TwoColumnPage(), chartPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            band1 = view.LayoutColumnBand(1);

            var chartRects = view.InlineChartRects;
            chartCount = chartRects.Count;

            const double tol = 3.0;
            chartInBand1 = chartCount > 0 && chartRects.All(c =>
                c.Rect.X >= band1.Left - tol && c.Rect.X < band1.Left + band1.Width + tol);
        });

        if (!ran) return;
        chartCount.Should().BeGreaterThan(0, "at least one inline chart rect must be produced");
        chartInBand1.Should().BeTrue(
            $"inline chart in column 2 must land in band1 X [{band1.Left:F1}, {band1.Left + band1.Width:F1}], " +
            $"not band0 [{band0.Left:F1}, {band0.Left + band0.Width:F1}]");
    }

    [Fact]
    public async Task AG3_SingleColumn_InlineChart_stays_at_contentLeft()
    {
        (double Left, double Width) band0 = default;
        bool chartInBand0 = false;
        int chartCount = 0;

        var ran = await OnUiThread(() =>
        {
            var chartPara = InlineChartParagraph(heightPt: 30);
            var doc = DocSingleCol(chartPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            var chartRects = view.InlineChartRects;
            chartCount = chartRects.Count;

            const double tol = 3.0;
            chartInBand0 = chartRects.Count > 0 && chartRects.All(c =>
                c.Rect.X >= band0.Left - tol && c.Rect.X < band0.Left + band0.Width + tol);
        });

        if (!ran) return;
        chartCount.Should().BeGreaterThan(0);
        chartInBand0.Should().BeTrue(
            "single-column: inline chart must remain in the one content column (no regression)");
    }

    // ── AG3: inline WordArt in column 2 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AG3_InlineWordArt_in_column2_lands_in_column2_X_band()
    {
        (double Left, double Width) band0 = default;
        (double Left, double Width) band1 = default;
        bool waInBand1 = false;
        int waCount = 0;

        var ran = await OnUiThread(() =>
        {
            var waPara = new Paragraph();
            // Keep font small so the WordArt height estimate stays < 54 DIP (< 40 pt).
            var wa = new WordArt("HW", WordArtStyle.FillBlue, 14); // h estimate ≈ 14*1.6=22 pt
            waPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });

            var doc = DocWithTargetInColumn2(TwoColumnPage(), waPara);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            band1 = view.LayoutColumnBand(1);

            var waRects = view.InlineWordArtRects;
            waCount = waRects.Count;

            const double tol = 3.0;
            waInBand1 = waRects.Count > 0 && waRects.All(w =>
                w.Rect.X >= band1.Left - tol && w.Rect.X < band1.Left + band1.Width + tol);
        });

        if (!ran) return;
        waCount.Should().BeGreaterThan(0, "at least one inline WordArt rect must be produced");
        waInBand1.Should().BeTrue(
            $"inline WordArt in column 2 must land in band1 X [{band1.Left:F1}, {band1.Left + band1.Width:F1}], " +
            $"not band0 [{band0.Left:F1}, {band0.Left + band0.Width:F1}]");
    }

    // ── AG3: inline SmartArt in column 2 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AG3_InlineSmartArt_in_column2_lands_in_column2_X_band()
    {
        (double Left, double Width) band0 = default;
        (double Left, double Width) band1 = default;
        bool saInBand1 = false;
        int saCount = 0;

        var ran = await OnUiThread(() =>
        {
            var saPara = new Paragraph();
            var sa = SmartArt.Create(SmartArtKind.Process, new[] { "A", "B" });
            sa.WidthPt  = 100;
            sa.HeightPt = 30; // < 40 pt so it stays within the 54-DIP headroom
            saPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });

            var doc = DocWithTargetInColumn2(TwoColumnPage(), saPara);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            band0 = view.LayoutColumnBand(0);
            band1 = view.LayoutColumnBand(1);

            var saRects = view.InlineSmartArtRects;
            saCount = saRects.Count;

            const double tol = 3.0;
            saInBand1 = saRects.Count > 0 && saRects.All(s =>
                s.Rect.X >= band1.Left - tol && s.Rect.X < band1.Left + band1.Width + tol);
        });

        if (!ran) return;
        saCount.Should().BeGreaterThan(0, "at least one inline SmartArt rect must be produced");
        saInBand1.Should().BeTrue(
            $"inline SmartArt in column 2 must land in band1 X [{band1.Left:F1}, {band1.Left + band1.Width:F1}], " +
            $"not band0 [{band0.Left:F1}, {band0.Left + band0.Width:F1}]");
    }

    // ── AG4: declared-width table fits within _colWidth in multi-column ───────────────────────────

    [Fact]
    public async Task AG4_DeclaredWidth_table_fits_within_column_width_in_2col_layout()
    {
        double colWidth = -1;
        double maxCellRight = 0;
        (double Left, double Width) band1 = default;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            // Declared widths sum to 340 pt = contentWidth, but colWidth is only ~213 DIP ≈ 160 pt.
            // ComputeColumnWidths must scale them down to fit the column.
            var table = DeclaredWidthTable(3, totalDeclaredPt: 340);
            var doc = DocWithTargetInColumn2(TwoColumnPage(), table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            colWidth = view.LayoutColumnWidth;
            band1 = view.LayoutColumnBand(1);

            var tableBlockIdx = 1;
            var tableCellRects = view.TableCellRects
                .Where(r => r.Block == tableBlockIdx)
                .Select(r => r.Rect)
                .ToList();
            rectCount = tableCellRects.Count;

            maxCellRight = tableCellRects.Count > 0
                ? tableCellRects.Max(r => r.Right)
                : 0;
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0, "declared-width table must produce cell rects");
        var colRight = band1.Left + band1.Width;
        maxCellRight.Should().BeLessThanOrEqualTo(colRight + 3.0,
            $"declared-width table must fit within _colWidth ({colWidth:F1} DIP); " +
            $"column 2 right edge = {colRight:F1}, table right edge = {maxCellRight:F1}");
    }

    [Fact]
    public async Task AG4_DeclaredWidth_table_fits_within_content_width_in_single_column()
    {
        double colWidth = -1;
        double maxCellRight = 0;
        (double Left, double Width) band0 = default;
        int rectCount = 0;

        var ran = await OnUiThread(() =>
        {
            // Single-column: declared widths sum to content width → should also fit (no regression).
            var table = DeclaredWidthTable(3, totalDeclaredPt: 340);
            var doc = DocSingleCol(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            colWidth = view.LayoutColumnWidth;
            band0 = view.LayoutColumnBand(0);

            var tableCellRects = view.TableCellRects
                .Select(r => r.Rect)
                .ToList();
            rectCount = tableCellRects.Count;

            maxCellRight = tableCellRects.Count > 0
                ? tableCellRects.Max(r => r.Right)
                : 0;
        });

        if (!ran) return;
        rectCount.Should().BeGreaterThan(0, "declared-width table must produce cell rects");
        var contentRight = band0.Left + band0.Width;
        maxCellRight.Should().BeLessThanOrEqualTo(contentRight + 3.0,
            $"single-column: table must fit content width {colWidth:F1} DIP (no regression)");
    }
}
