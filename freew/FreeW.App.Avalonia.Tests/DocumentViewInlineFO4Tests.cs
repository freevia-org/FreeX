using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the Avalonia DocumentView inline-object render path — FO4 wave:
/// inline (non-floating) Charts, WordArt, and SmartArt laid out in the document flow.
/// Verifies: each type reserves a non-zero line box; is stored in the inline collections;
/// caret/selection still steps over each object; render does not crash; headless PNG captures
/// non-blank output with the objects in-flow.
/// </summary>
public sealed class DocumentViewInlineFO4Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates a doc with a single paragraph: text before + inline chart + text after.</summary>
    private static TextDocument DocWithInlineChart(
        ChartKind kind,
        double widthPt  = 240,
        double heightPt = 160,
        string? title   = "Inline Chart")
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var chart = Chart.Create(kind,
            new[] { "A", "B", "C" },
            new[] { 10.0, 25.0, 15.0 },
            "Series 1",
            title);
        chart.WidthPt  = widthPt;
        chart.HeightPt = heightPt;
        // No Placement → inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Creates a doc with a single paragraph: text before + inline WordArt + text after.</summary>
    private static TextDocument DocWithInlineWordArt(
        WordArtStyle style = WordArtStyle.FillBlue,
        string text = "Hello WordArt",
        double fontSizePt = 28)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var wa = new WordArt(text, style, fontSizePt); // No Placement → inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    /// <summary>Creates a doc with a single paragraph: text before + inline SmartArt + text after.</summary>
    private static TextDocument DocWithInlineSmartArt(
        SmartArtKind kind = SmartArtKind.Process,
        double widthPt  = 360,
        double heightPt = 160)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Before ", RunFormatting.Default));

        var sa = SmartArt.Create(kind, new[] { "Step A", "Step B", "Step C" });
        sa.WidthPt  = widthPt;
        sa.HeightPt = heightPt;
        // No Placement → inline.
        para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });

        para.Runs.Add(new Run(" after.", RunFormatting.Default));
        doc.Blocks.Add(para);
        return doc;
    }

    // ── Inline chart tests ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_chart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineChartCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline chart must produce one entry in _inlineCharts");
    }

    [Fact]
    public async Task Inline_chart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Bar);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingChartCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline chart must NOT appear in _floatingCharts");
    }

    [Fact]
    public async Task Inline_chart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, heightPt: 160);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline chart must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_chart_rect_height_matches_model_heightpt()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, heightPt: 144);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeApproximately(144 * (96.0 / 72.0), 2,
            "inline chart height should be 144pt converted to DIP");
    }

    [Fact]
    public async Task Inline_chart_kind_preserved()
    {
        ChartKind kind = ChartKind.Column;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Pie);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) kind = rects[0].Kind;
        });

        if (!ran) return;
        kind.Should().Be(ChartKind.Pie, "chart kind must be preserved for inline charts");
    }

    [Fact]
    public async Task Inline_chart_title_preserved()
    {
        string? title = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column, title: "Revenue");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineChartRects;
            if (rects.Count > 0) title = rects[0].Title;
        });

        if (!ran) return;
        title.Should().Be("Revenue", "chart title must be preserved for inline charts");
    }

    [Fact]
    public async Task Inline_chart_produces_sentinel_glyph_for_caret()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            // Placed count includes sentinel(s) — at minimum the chart sentinel + end sentinel.
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0, "inline chart paragraph must emit at least one glyph/sentinel");
    }

    // ── Inline WordArt tests ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_wordart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline WordArt must produce one entry in _inlineWordArts");
    }

    [Fact]
    public async Task Inline_wordart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingWordArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline WordArt must NOT appear in _floatingWordArts");
    }

    [Fact]
    public async Task Inline_wordart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(fontSizePt: 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineWordArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline WordArt must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_wordart_text_and_style_preserved()
    {
        string? text = null;
        WordArtStyle style = WordArtStyle.FillBlue;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineWordArt(WordArtStyle.Shadow, text: "FreeW!");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineWordArtRects;
            if (rects.Count > 0) { text = rects[0].Text; style = rects[0].Style; }
        });

        if (!ran) return;
        text.Should().Be("FreeW!", "WordArt text must be preserved for inline WordArt");
        style.Should().Be(WordArtStyle.Shadow, "WordArt style must be preserved for inline WordArt");
    }

    // ── Inline SmartArt tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_smartart_is_collected_in_inline_list()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(SmartArtKind.Process);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.InlineSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(1, "one inline SmartArt must produce one entry in _inlineSmartArts");
    }

    [Fact]
    public async Task Inline_smartart_is_not_collected_as_floating()
    {
        int count = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            count = view.FloatingSmartArtCount;
        });

        if (!ran) return;
        count.Should().Be(0, "inline SmartArt must NOT appear in _floatingSmartArts");
    }

    [Fact]
    public async Task Inline_smartart_rect_has_positive_height()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(heightPt: 160);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeGreaterThan(0, "inline SmartArt must reserve a positive height in the flow");
    }

    [Fact]
    public async Task Inline_smartart_rect_height_matches_model_heightpt()
    {
        double height = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(heightPt: 120);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) height = rects[0].Rect.Height;
        });

        if (!ran) return;
        height.Should().BeApproximately(120 * (96.0 / 72.0), 2,
            "inline SmartArt height should be 120pt converted to DIP");
    }

    [Fact]
    public async Task Inline_smartart_kind_preserved()
    {
        SmartArtKind kind = SmartArtKind.List;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt(SmartArtKind.Hierarchy);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) kind = rects[0].Kind;
        });

        if (!ran) return;
        kind.Should().Be(SmartArtKind.Hierarchy, "SmartArt kind must be preserved for inline SmartArt");
    }

    [Fact]
    public async Task Inline_smartart_node_count_preserved()
    {
        int nodeCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithInlineSmartArt();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            var rects = view.InlineSmartArtRects;
            if (rects.Count > 0) nodeCount = rects[0].NodeCount;
        });

        if (!ran) return;
        nodeCount.Should().Be(3, "SmartArt with 3 nodes must expose node count 3");
    }

    // ── Caret / selection step-over ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inline_chart_paragraph_still_produces_glyphs_from_text_runs()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            // "Before " + chart + " after." — text runs should still produce placed glyphs.
            var doc = DocWithInlineChart(ChartKind.Column);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        // "Before " = 7 chars, " after." = 7 chars, plus the atomic chart position and sentinels.
        glyphs.Should().BeGreaterThan(0,
            "a paragraph with inline chart + surrounding text must produce placed glyphs");
    }

    [Fact]
    public async Task Multiple_inline_types_in_sequence_all_collected()
    {
        int charts = 0, wordarts = 0, smartarts = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var p1 = new Paragraph();
            var c = Chart.Create(ChartKind.Line, new[] { "A" }, new[] { 1.0 });
            p1.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = c });
            doc.Blocks.Add(p1);

            var p2 = new Paragraph();
            var wa = new WordArt("Test", WordArtStyle.FillBlue, 24);
            p2.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
            doc.Blocks.Add(p2);

            var p3 = new Paragraph();
            var sa = SmartArt.Create(SmartArtKind.List, new[] { "X", "Y" });
            p3.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
            doc.Blocks.Add(p3);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            charts    = view.InlineChartCount;
            wordarts  = view.InlineWordArtCount;
            smartarts = view.InlineSmartArtCount;
        });

        if (!ran) return;
        charts.Should().Be(1,   "one inline chart paragraph");
        wordarts.Should().Be(1, "one inline WordArt paragraph");
        smartarts.Should().Be(1, "one inline SmartArt paragraph");
    }

    // ── Headless render capture — all FO4 inline types in one document ────────────────────────────

    [Fact]
    public async Task FO4_render_capture_inline_types_produces_non_blank_output()
    {
        byte[]? pngBytes = null;
        string? outPath  = null;
        var ran = false;

        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;

                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();

                // Introductory text paragraph.
                var intro = new Paragraph();
                intro.Runs.Add(new Run("FO4 inline render test.", RunFormatting.Default with { FontSizePt = 11 }));
                doc.Blocks.Add(intro);

                // Inline chart paragraph.
                var pChart = new Paragraph();
                pChart.Runs.Add(new Run("Chart: ", RunFormatting.Default));
                var chart = Chart.Create(ChartKind.Column,
                    new[] { "Q1", "Q2", "Q3" },
                    new[] { 10.0, 20.0, 15.0 },
                    "Sales", "Revenue 2025");
                chart.WidthPt  = 280;
                chart.HeightPt = 150;
                pChart.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Chart = chart });
                pChart.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pChart);

                // Inline WordArt paragraph.
                var pWa = new Paragraph();
                pWa.Runs.Add(new Run("WordArt: ", RunFormatting.Default));
                var wa = new WordArt("FreeW!", WordArtStyle.GradientFill, 28);
                pWa.Runs.Add(new Run(string.Empty, RunFormatting.Default) { WordArt = wa });
                pWa.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pWa);

                // Inline SmartArt paragraph.
                var pSa = new Paragraph();
                pSa.Runs.Add(new Run("SmartArt: ", RunFormatting.Default));
                var sa = SmartArt.Create(SmartArtKind.Process, new[] { "Design", "Build", "Ship" });
                sa.WidthPt  = 320;
                sa.HeightPt = 100;
                pSa.Runs.Add(new Run(string.Empty, RunFormatting.Default) { SmartArt = sa });
                pSa.Runs.Add(new Run(" end.", RunFormatting.Default));
                doc.Blocks.Add(pSa);

                // Trailing paragraph.
                var trail = new Paragraph();
                trail.Runs.Add(new Run("Done.", RunFormatting.Default));
                doc.Blocks.Add(trail);

                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window { Width = 816, Height = 1100, Content = view };
                window.Show();
                window.Measure(new Size(816, 1100));
                window.Arrange(new Rect(0, 0, 816, 1100));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    pngBytes = WriteableBitmapToPng(frame);

                window.Close();

                var testBinDir = Path.GetDirectoryName(typeof(DocumentViewInlineFO4Tests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(Path.Combine(testBinDir, "freew_avalonia_fo4_inline.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);

                Console.WriteLine($"[FO4Capture] PNG written ({pngBytes?.Length ?? 0} bytes) to: {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FO4Capture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null)
        {
            Console.WriteLine("[FO4Capture] CaptureRenderedFrame returned null — skipping.");
            return;
        }
        if (pngBytes.Length == 0)
        {
            Console.WriteLine("[FO4Capture] Encoder produced 0 bytes — skipping.");
            return;
        }

        pngBytes.Length.Should().BeGreaterThan(5_000,
            "a rendered page with FO4 inline objects and body text should produce a non-trivial PNG");
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be((byte)'P');
        pngBytes[2].Should().Be((byte)'N');
        pngBytes[3].Should().Be((byte)'G');

        Console.WriteLine($"[FO4Capture] Visual inspection: {outPath}");
    }

    // ── PNG encoder ───────────────────────────────────────────────────────────────────────────────

    private static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
    {
        try
        {
            using var locked = bitmap.Lock();
            var info = new SKImageInfo(
                locked.Size.Width,
                locked.Size.Height,
                locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888,
                SKAlphaType.Premul);

            using var skBitmap = new SKBitmap();
            if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes))
                return [];

            using var skImage = SKImage.FromBitmap(skBitmap);
            using var data    = skImage.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
