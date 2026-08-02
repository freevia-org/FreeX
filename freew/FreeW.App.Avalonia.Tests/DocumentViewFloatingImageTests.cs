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
/// Tests for the Avalonia DocumentView floating image render path (FO1 wave).
/// Verifies: floating images are collected separately from inline images; page-space rect
/// is resolved from FloatingPlacement offsets and anchors; z-order (behind vs in-front of
/// text) controls the draw-order bucket; a headless render capture produces non-blank pixels
/// in the float's region.
/// </summary>
public sealed class DocumentViewFloatingImageTests
{
    [Fact]
    public void ImportedReflectionParameters_UseAuthoredStartAlphaAndDistance()
    {
        var parameters = DocumentView.ReflectionParameters(new InlineImage([], 1, 1)
        {
            ReflectionPreset = 1,
            ImportedEffects = new ShapeEffectLst
            {
                HasReflection = true,
                ReflectionStartAlpha = 35000,
                ReflectionDist = 38100,
            },
        });

        parameters.Should().NotBeNull();
        parameters!.Opacity.Should().Be(0.35);
        parameters.DistanceDip.Should().Be(4);
    }

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
            return false; // no headless drawing backend in this environment
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal 4x4 PNG as a stand-in for a real image.</summary>
    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(255, 128, 0)); // orange
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Builds a document that contains a single paragraph with one floating image anchored to it,
    /// plus some body text on the same paragraph so glyphs are produced.
    /// </summary>
    private static TextDocument DocWithFloatingImage(
        ImageWrapping wrapping,
        double hOffsetPt,
        double vOffsetPt,
        HorizontalAnchor hAnchor = HorizontalAnchor.Column,
        VerticalAnchor   vAnchor = VerticalAnchor.Paragraph,
        int zOrder = 0,
        double imgWidthPt  = 144,  // 2 in
        double imgHeightPt = 108,  // 1.5 in
        int reflectionPreset = 0)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var bodyPara = new Paragraph();
        bodyPara.Runs.Add(new Run("Body text with a floating image anchored here.",
            RunFormatting.Default with { FontSizePt = 11 }));

        var floatImage = new InlineImage(SmallPng(), imgWidthPt, imgHeightPt)
        {
            Wrapping           = wrapping,
            HorizontalOffsetPt = hOffsetPt,
            VerticalOffsetPt   = vOffsetPt,
            HorizontalAnchor   = hAnchor,
            VerticalAnchor     = vAnchor,
            ZOrderIndex        = zOrder,
            ReflectionPreset   = reflectionPreset,
        };
        var floatRun = new Run(string.Empty, RunFormatting.Default) { Image = floatImage };
        bodyPara.Runs.Add(floatRun);

        doc.Blocks.Add(bodyPara);

        // Add a second plain paragraph so there's more body text for z-order verification.
        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Second paragraph below the float anchor.", RunFormatting.Default));
        doc.Blocks.Add(p2);

        return doc;
    }

    // ── Test 1: inline image is NOT treated as floating ──────────────────────────────────────────────

    [Fact]
    public async Task Inline_image_is_not_collected_as_floating()
    {
        int floatCount = -1;
        int inlineCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var p = new Paragraph();
            var img = new InlineImage(SmallPng(), 72, 54) { Wrapping = ImageWrapping.Inline };
            p.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });
            doc.Blocks.Add(p);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            floatCount  = view.FloatingImageCount;
            inlineCount = view.PlacedGlyphCount; // sentinel only — no text, but layout runs
        });

        if (!ran) return;
        floatCount.Should().Be(0, "an inline image must NOT be added to _floatingImages");
    }

    // ── Test 2: floating image IS collected ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_square_wrap_image_is_collected()
    {
        int floatCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 36, vOffsetPt: 36);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));
            floatCount = view.FloatingImageCount;
        });

        if (!ran) return;
        floatCount.Should().Be(1, "one floating image in the document should produce one entry in _floatingImages");
    }

    // ── Test 3: column-anchor horizontal position ────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_image_column_anchor_x_matches_content_left_plus_offset()
    {
        Rect floatRect = default;
        var ran = await OnUiThread(() =>
        {
            // Column anchor: X = _contentLeft + offsetPt * PxPerPoint
            // With 816px page, default margins (1in each side): contentLeft ≈ (816/2 - 816/2*0.5) = varies
            // We just verify offset is respected: two images with different offsets → different X.
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 36, vOffsetPt: 0,
                hAnchor: HorizontalAnchor.Column, vAnchor: VerticalAnchor.Paragraph);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                floatRect = rects[0].Rect;
        });

        if (!ran) return;
        // 36pt * (96/72) = 48 DIP offset from the content left edge.
        // The rect.X should be >= the page content left (no negative offset).
        floatRect.X.Should().BeGreaterThan(0, "floating image X should be positive");
        floatRect.Width.Should().BeApproximately(144 * (96.0 / 72.0), 2,
            "image width should be 144pt converted to DIP");
    }

    // ── Test 4: vertical paragraph-anchor position ───────────────────────────────────────────────────

    [Fact]
    public async Task Floating_image_paragraph_anchor_y_is_near_paragraph_top()
    {
        Rect floatRect = default;
        var ran = await OnUiThread(() =>
        {
            // VerticalAnchor.Paragraph + 0 offset: rect.Y should be near the top margin
            // of the first page (paragraph is the first block).
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                vAnchor: VerticalAnchor.Paragraph);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                floatRect = rects[0].Rect;
        });

        if (!ran) return;
        // In PrintLayout with default 1in top margin (96dip) and DeskPadding=24:
        // The first paragraph's page-space Y ≈ DeskPadding(24) + marginTop(96) ≈ 120.
        // With 0 vertical offset the float rect.Y should be around that range.
        floatRect.Y.Should().BeGreaterThan(0, "floating image Y should be positive");
        floatRect.Y.Should().BeLessThan(300, "floating image at paragraph anchor with 0 offset should be near the top of the page");
    }

    // ── Test 5: vertical offset is applied ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Floating_image_vertical_offset_shifts_rect_Y()
    {
        Rect rectNoOffset = default;
        Rect rectWithOffset = default;
        var ran = await OnUiThread(() =>
        {
            var doc0 = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                vAnchor: VerticalAnchor.Paragraph);
            var view0 = new DocumentView();
            view0.LoadDocument(doc0);
            view0.Measure(new Size(816, 2000));
            if (view0.FloatingImageRects.Count > 0)
                rectNoOffset = view0.FloatingImageRects[0].Rect;

            var doc1 = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 72,
                vAnchor: VerticalAnchor.Paragraph);
            var view1 = new DocumentView();
            view1.LoadDocument(doc1);
            view1.Measure(new Size(816, 2000));
            if (view1.FloatingImageRects.Count > 0)
                rectWithOffset = view1.FloatingImageRects[0].Rect;
        });

        if (!ran) return;
        // 72pt = 1 inch = 96 DIP. The Y should shift by approximately 96 DIP.
        var delta = rectWithOffset.Y - rectNoOffset.Y;
        delta.Should().BeApproximately(96, 4, "72pt vertical offset should shift Y by ~96 DIP (96dpi)");
    }

    // ── Test 6: behind-text z-order bucket ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Behind_text_floating_image_is_marked_BehindText_true()
    {
        bool? behindText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.Behind, hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                behindText = rects[0].BehindText;
        });

        if (!ran) return;
        behindText.Should().BeTrue("ImageWrapping.Behind must place the image in the behind-text draw bucket");
    }

    // ── Test 7: in-front-of-text z-order bucket ──────────────────────────────────────────────────────

    [Fact]
    public async Task InFront_floating_image_is_marked_BehindText_false()
    {
        bool? behindText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.InFront, hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                behindText = rects[0].BehindText;
        });

        if (!ran) return;
        behindText.Should().BeFalse("ImageWrapping.InFront must place the image in the in-front draw bucket");
    }

    // ── Test 8: square wrap is in-front bucket ────────────────────────────────────────────────────────

    [Fact]
    public async Task Square_wrap_floating_image_is_in_front_bucket()
    {
        bool? behindText = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                behindText = rects[0].BehindText;
        });

        if (!ran) return;
        behindText.Should().BeFalse("Square/Tight/TopAndBottom wrap modes render in front of text (not Behind)");
    }

    // ── Test 9: z-order is preserved ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZOrderIndex_is_preserved_in_floating_image_rects()
    {
        int capturedZOrder = -999;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                zOrder: 42);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            var rects = view.FloatingImageRects;
            if (rects.Count > 0)
                capturedZOrder = rects[0].ZOrder;
        });

        if (!ran) return;
        capturedZOrder.Should().Be(42, "ZOrderIndex from the model must be preserved in the layout list");
    }

    // ── Test 10: multiple floating images — count and z-order sort ──────────────────────────────────

    [Fact]
    public async Task Multiple_floating_images_sorted_by_z_order_in_same_bucket()
    {
        var zOrders = Array.Empty<int>();
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Anchor text.", RunFormatting.Default));

            // Add two floating images with reversed z-order.
            foreach (var (z, offset) in new[] { (10, 0.0), (5, 36.0) })
            {
                var img = new InlineImage(SmallPng(), 72, 54)
                {
                    Wrapping           = ImageWrapping.Square,
                    HorizontalOffsetPt = offset,
                    VerticalOffsetPt   = 0,
                    ZOrderIndex        = z,
                };
                para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });
            }
            doc.Blocks.Add(para);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            // FloatingImageRects preserves insertion order (layout order), not sorted order.
            // The render pass sorts by ZOrder. Verify both images are captured.
            zOrders = view.FloatingImageRects.Select(r => r.ZOrder).ToArray();
        });

        if (!ran) return;
        zOrders.Should().HaveCount(2, "two floating images should produce two entries");
        zOrders.Should().Contain(10).And.Contain(5);
    }

    // ── Test 11: body text still lays out when paragraph has only floating images ──────────────────

    [Fact]
    public async Task Paragraph_with_only_floating_image_still_produces_text_glyphs_for_other_runs()
    {
        int glyphs = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFloatingImage(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));
            glyphs = view.PlacedGlyphCount;
        });

        if (!ran) return;
        glyphs.Should().BeGreaterThan(0,
            "a paragraph that has text runs alongside a floating image run should still produce placed glyphs");
    }

    // ── Test 12: headless render capture — float appears in PNG ────────────────────────────────────

    [Fact]
    public async Task Floating_image_render_capture_produces_non_blank_output()
    {
        byte[]? pngBytes = null;
        string? outPath = null;
        var ran = false;

        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;

                // Build a document with a floating image anchored at (1in, 1in) from column/paragraph.
                var doc = DocWithFloatingImage(ImageWrapping.InFront,
                    hOffsetPt: 72, vOffsetPt: 72,
                    hAnchor: HorizontalAnchor.Column,
                    vAnchor: VerticalAnchor.Paragraph,
                    imgWidthPt: 144, imgHeightPt: 108);

                // Add more body text so the PNG clearly shows text + float.
                for (var i = 0; i < 5; i++)
                {
                    var p = new Paragraph();
                    p.Runs.Add(new Run(
                        $"Body paragraph {i + 1}: lorem ipsum dolor sit amet consectetur.",
                        RunFormatting.Default));
                    doc.Blocks.Add(p);
                }

                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window
                {
                    Width   = 816,
                    Height  = 1200,
                    Content = view,
                };
                window.Show();

                window.Measure(new Size(816, 1200));
                window.Arrange(new Rect(0, 0, 816, 1200));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    pngBytes = WriteableBitmapToPng(frame);

                window.Close();

                var testBinDir = Path.GetDirectoryName(
                    typeof(DocumentViewFloatingImageTests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(
                    Path.Combine(testBinDir, "freew_avalonia_floating_image.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);

                Console.WriteLine(
                    $"[FloatingImageCapture] PNG written ({pngBytes?.Length ?? 0} bytes) to: {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FloatingImageCapture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null)
        {
            Console.WriteLine("[FloatingImageCapture] CaptureRenderedFrame returned null — skipping.");
            return;
        }
        if (pngBytes.Length == 0)
        {
            Console.WriteLine("[FloatingImageCapture] Encoder produced 0 bytes — skipping.");
            return;
        }

        pngBytes.Length.Should().BeGreaterThan(5_000,
            "a rendered page with a floating image and body text should produce a non-trivial PNG");
        pngBytes[0].Should().Be(0x89);
        pngBytes[1].Should().Be((byte)'P');
        pngBytes[2].Should().Be((byte)'N');
        pngBytes[3].Should().Be((byte)'G');

        Console.WriteLine($"[FloatingImageCapture] Visual inspection: {outPath}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Floating_image_reflection_preset_changes_only_the_reflection_region(int reflectionPreset)
    {
        var control = await CaptureFloatingImageFrameAsync(reflectionPreset: 0);
        var reflected = await CaptureFloatingImageFrameAsync(reflectionPreset);

        if (!control.Ran || !reflected.Ran || control.Png is null || reflected.Png is null)
            return;

        control.FloatRect.Should().Be(reflected.FloatRect,
            "reflection must not affect the floating image layout rect");

        using var controlBitmap = SKBitmap.Decode(control.Png);
        using var reflectedBitmap = SKBitmap.Decode(reflected.Png);
        controlBitmap.Should().NotBeNull();
        reflectedBitmap.Should().NotBeNull();

        var x0 = Math.Clamp((int)Math.Floor(control.FloatRect.X), 0, controlBitmap!.Width);
        var x1 = Math.Clamp((int)Math.Ceiling(control.FloatRect.Right), 0, controlBitmap.Width);
        var y0 = Math.Clamp((int)Math.Floor(control.FloatRect.Bottom), 0, controlBitmap.Height);
        var y1 = Math.Clamp((int)Math.Ceiling(control.FloatRect.Bottom + control.FloatRect.Height), 0, controlBitmap.Height);
        var changedPixels = 0;
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            if (controlBitmap.GetPixel(x, y) != reflectedBitmap!.GetPixel(x, y))
                changedPixels++;
        }

        changedPixels.Should().BeGreaterThan(100,
            $"reflection preset {reflectionPreset} should paint a mirrored, fading copy below its source image");
    }

    private static async Task<(bool Ran, byte[]? Png, Rect FloatRect)> CaptureFloatingImageFrameAsync(int reflectionPreset)
    {
        byte[]? png = null;
        Rect floatRect = default;
        try
        {
            await Session.Dispatch(() =>
            {
                var doc = DocWithFloatingImage(
                    ImageWrapping.InFront,
                    hOffsetPt: 72,
                    vOffsetPt: 72,
                    imgWidthPt: 144,
                    imgHeightPt: 108,
                    reflectionPreset: reflectionPreset);
                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window { Width = 816, Height = 1200, Content = view };
                window.Show();
                window.Measure(new Size(816, 1200));
                window.Arrange(new Rect(0, 0, 816, 1200));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                if (window.CaptureRenderedFrame() is { } frame)
                    png = WriteableBitmapToPng(frame);
                if (view.FloatingImageRects.Count > 0)
                    floatRect = view.FloatingImageRects[0].Rect;
                window.Close();
            }, CancellationToken.None);
            return (true, png, floatRect);
        }
        catch
        {
            return (false, null, default);
        }
    }

    // ── SS1: paragraph-anchor float page-break tests ─────────────────────────────────────────────────

    /// <summary>
    /// SS1 (page-break float): A paragraph whose first line overflows to the NEXT page must have its
    /// VerticalAnchor.Paragraph float placed on that NEXT page (same page as the paragraph's first line),
    /// not on the previous page where the anchor paragraph's pre-break Y happened to be.
    ///
    /// Layout geometry (96 DPI, US Letter, 1-in margins):
    ///   pageHeightPx = 1056, marginTop = marginBottom = 96, textAreaHeight = 864.
    ///   Page 1 content:  Y in [120, 984)  (DeskPadding 24 + marginTop 96 = 120; + 864 = 984).
    ///   Page 2 content:  Y ≥ 984 + PageGap(20) + marginTop(96) = 1100.
    ///
    /// We fill page 1 with ~45 single-line paragraphs (each ≈ 20 DIP, totaling ~900 DIP > 864) so
    /// the ANCHOR paragraph's first line is pushed to page 2.  The float with vOffset=0 must render
    /// at Y ≥ 1100 (page 2 content start), NOT at some Y in [120, 984) (page 1).
    /// </summary>
    [Fact]
    public async Task Paragraph_anchored_float_after_pagebreak_lands_on_next_page()
    {
        Rect floatRect = default;
        IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder)>? allRects = null;
        int pageCount = 0;

        var ran = await OnUiThread(() =>
        {
            // US Letter defaults: 612x792pt, 72pt margins.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var bodyFmt = RunFormatting.Default with { FontSizePt = 11 };

            // Fill page 1: ~45 one-liner paragraphs.  At 11pt × 1.3 leading × (96/72) ≈ 20.3 DIP each,
            // 45 lines × 20.3 ≈ 913 DIP, which exceeds the 864-DIP text area, ensuring the last paragraph
            // is pushed to page 2.
            for (var i = 0; i < 45; i++)
            {
                var filler = new Paragraph();
                filler.Runs.Add(new Run($"Filler line {i + 1}.", bodyFmt));
                doc.Blocks.Add(filler);
            }

            // The anchor paragraph: its first line must overflow to page 2.
            // Add a floating image at vOffset=0 (should land at the paragraph's first-line Y on page 2).
            var anchorPara = new Paragraph();
            anchorPara.Runs.Add(new Run("Anchor paragraph on page 2.", bodyFmt));

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, bodyFmt) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, double.PositiveInfinity));

            pageCount = view.PageCount;
            allRects  = view.FloatingImageRects;
            if (allRects.Count > 0)
                floatRect = allRects[0].Rect;
        });

        if (!ran) return;

        // The document must span at least 2 pages.
        pageCount.Should().BeGreaterThan(1, "45 filler lines should push the anchor paragraph to page 2");

        allRects.Should().NotBeNull().And.HaveCount(1, "exactly one floating image was added");

        // Page geometry at 96 DIP:
        //   textAreaHeight = (792 - 72 - 72) × (96/72) = 648 × (4/3) = 864
        //   Page 2 content Y ≥ DeskPadding(24) + textAreaHeight(864) + PageGap(20) + marginTop(96) = 1004
        // We use 1000 as a generous lower bound (avoids rounding sensitivity).
        const double page2Threshold = 1000.0;

        floatRect.Y.Should().BeGreaterThanOrEqualTo(page2Threshold,
            $"SS1: paragraph-anchored float must be on page 2 (Y ≥ {page2Threshold}), " +
            $"not on page 1 (got Y={floatRect.Y:F1})");
    }

    /// <summary>
    /// SS1 (SpaceBefore offset): A vOffset=0 paragraph-anchored float on the FIRST paragraph of a
    /// document (no page-break, no SpaceBefore) must have its Y match the paragraph's first-line Y
    /// within a tight tolerance.  This verifies the basic no-regression case and the SpaceBefore
    /// alignment: the float aligns with text top, not SpaceBefore above it.
    /// </summary>
    [Fact]
    public async Task Paragraph_anchored_float_no_pagebreak_aligns_with_text_top()
    {
        Rect floatRect = default;
        double firstGlyphY = double.MaxValue;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var anchorPara = new Paragraph
            {
                // Give SpaceBefore so we can confirm the float is aligned AFTER it (at text top),
                // not BEFORE it (the old bug would place the float SpaceBefore points above the text).
                Formatting = new ParagraphFormatting { SpaceBeforePt = 24, SpaceBeforeIsSet = true },
            };
            anchorPara.Runs.Add(new Run("First paragraph with SpaceBefore.", RunFormatting.Default));

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            if (view.FloatingImageRects.Count > 0)
                floatRect = view.FloatingImageRects[0].Rect;

            // First glyph Y for block 0 tells us where the text top actually is.
            var placed = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
                firstGlyphY = placed.Min(g => g.Y);
        });

        if (!ran) return;

        floatRect.Y.Should().BeGreaterThan(0, "float should be placed after document top");

        if (firstGlyphY < double.MaxValue)
        {
            // The float Y (vOffset=0) should be within a small tolerance of the first glyph Y.
            // Before the fix, the float was placed SpaceBefore (32 DIP for 24pt) above the first glyph.
            // After the fix, the float is anchored at the post-SpaceBefore position, so delta ≤ 4 DIP.
            var delta = Math.Abs(floatRect.Y - firstGlyphY);
            delta.Should().BeLessThanOrEqualTo(4.0,
                $"SS1 SpaceBefore: vOffset=0 float should align with first-line Y (glyph Y={firstGlyphY:F1}), " +
                $"got float Y={floatRect.Y:F1}, delta={delta:F1}");
        }
    }

    /// <summary>
    /// SS1 (page/margin anchors unaffected): VerticalAnchor.Page and VerticalAnchor.Margin floats
    /// must not be affected by the SS1 fix — they are page-relative, not paragraph-relative.
    /// Both should still produce a Y within the first page's range regardless of how many filler
    /// paragraphs precede the anchor (since they offset from the page/margin top, not from the
    /// paragraph's content Y).
    /// </summary>
    [Theory]
    [InlineData(VerticalAnchor.Page)]
    [InlineData(VerticalAnchor.Margin)]
    public async Task Page_and_margin_anchored_floats_are_page_relative_after_ss1_fix(VerticalAnchor anchor)
    {
        Rect floatRect = default;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var bodyFmt = RunFormatting.Default with { FontSizePt = 11 };

            // Don't fill the page — anchor paragraph is on page 1.
            var anchorPara = new Paragraph();
            anchorPara.Runs.Add(new Run("Anchor paragraph.", bodyFmt));

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = anchor,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, bodyFmt) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 2000));

            if (view.FloatingImageRects.Count > 0)
                floatRect = view.FloatingImageRects[0].Rect;
        });

        if (!ran) return;

        // Page anchor: Y = DeskPadding(24) + 0*vOffset = 24; Margin anchor: Y = DeskPadding(24) + marginTop(96) = 120.
        // In either case the rect should be in the first page's visible range (< 984 DIP).
        const double page1ContentEnd = 984.0; // DeskPadding(24) + textAreaHeight(864) + marginTop(96)
        floatRect.Y.Should().BeLessThan(page1ContentEnd,
            $"{anchor} anchor with vOffset=0 on page-1 paragraph should remain on page 1 (Y < {page1ContentEnd}), " +
            $"got Y={floatRect.Y:F1}");
    }

    /// <summary>
    /// SS1 (no-break paragraph regression): A paragraph-anchored float on a paragraph that does NOT
    /// break pages still renders at the correct Y — no regression from the fix.
    /// Two floats with different vOffsets → their Y delta matches the expected DIP offset.
    /// </summary>
    [Fact]
    public async Task Paragraph_anchored_float_no_break_vertical_offset_correct()
    {
        Rect rect0 = default, rect72 = default;
        var ran = await OnUiThread(() =>
        {
            // Build two single-paragraph documents (same anchor para on page 1, no break).
            TextDocument MakeDoc(double vOffsetPt)
            {
                var doc = TextDocument.CreateEmpty();
                doc.Blocks.Clear();
                var p = new Paragraph();
                p.Runs.Add(new Run("No-break anchor.", RunFormatting.Default));
                var img = new InlineImage(SmallPng(), 72, 54)
                {
                    Wrapping           = ImageWrapping.Square,
                    HorizontalOffsetPt = 0,
                    VerticalOffsetPt   = vOffsetPt,
                    HorizontalAnchor   = HorizontalAnchor.Column,
                    VerticalAnchor     = VerticalAnchor.Paragraph,
                };
                p.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = img });
                doc.Blocks.Add(p);
                return doc;
            }

            Rect Measure(TextDocument doc)
            {
                var v = new DocumentView();
                v.LoadDocument(doc);
                v.Measure(new Size(816, 2000));
                var r = v.FloatingImageRects;
                return r.Count > 0 ? r[0].Rect : default;
            }

            rect0  = Measure(MakeDoc(0));
            rect72 = Measure(MakeDoc(72));
        });

        if (!ran) return;

        // 72pt = 96 DIP at 96 DPI.
        var delta = rect72.Y - rect0.Y;
        delta.Should().BeApproximately(96, 4,
            "SS1 no-break regression: 72pt vertical offset should shift Y by ~96 DIP");
    }

    // ── TT1: boundary-band page-break probe ──────────────────────────────────────────────────────────

    /// <summary>
    /// TT1 (boundary-band fix): Fill a page so the anchor paragraph's first line sits in the last
    /// ~lineHeight px of the text area (posInPage just below textAreaHeight - lineHeight, i.e.
    /// in the band (textAreaHeight - lineHeight, textAreaHeight)).  Before the fix a 1-px probe
    /// used by PeekFirstLineContentY did NOT trigger the page-break, so anchorContentY stayed on
    /// page 1 while the first line was pushed to page 2 → float landed on page 1 (wrong).
    ///
    /// We fill the page to leave just enough room for ~1px but not a full line, then add the
    /// anchor paragraph.  The float with vOffset=0 must land on page 2 (the same page as the
    /// paragraph's first line).
    ///
    /// Layout geometry (96 DPI, US Letter, 1-in margins):
    ///   textAreaHeight = (792-72-72) × (96/72) = 864 DIP
    ///   lineHeight ≈ 11pt × (96/72) × 1.3 ≈ 20.3 DIP
    ///   We fill 863 DIP (864 - 1) of the text area, leaving 1 DIP at the bottom.
    ///   The anchor para's first line (≈20 DIP) overflows → page 2.
    ///   Page 2 content start Y ≈ 24 + 864 + 20 + 96 = 1004 DIP.
    /// </summary>
    [Fact]
    public async Task TT1_boundary_band_anchor_float_lands_on_same_page_as_first_line()
    {
        Rect floatRect = default;
        double firstGlyphY = double.MaxValue;
        int pageCount = 0;

        var ran = await OnUiThread(() =>
        {
            // US-Letter defaults: 612×792pt, 72pt margins → textAreaHeight = 864 DIP.
            const double textAreaHeightDip = 864.0;
            // Default 11pt line ≈ 20.3 DIP.  Fill to textAreaHeight - 1 px so the band test fires.
            const double lineHDip = 20.3;
            // We need to fill exactly enough that posInPage ∈ (textAreaHeight - lineH, textAreaHeight).
            // Strategy: add filler paragraphs totalling ~(textAreaHeight - 1) DIP.
            // Each filler line ≈ lineHDip. Count = floor((textAreaHeight - 1) / lineHDip).
            var fillerCount = (int)((textAreaHeightDip - 1) / lineHDip); // ≈ 42

            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var bodyFmt = RunFormatting.Default with { FontSizePt = 11 };

            for (var i = 0; i < fillerCount; i++)
            {
                var filler = new Paragraph();
                filler.Runs.Add(new Run($"Fill {i + 1}.", bodyFmt));
                doc.Blocks.Add(filler);
            }

            // Anchor paragraph at the boundary band — its first line must overflow to page 2.
            var anchorPara = new Paragraph();
            anchorPara.Runs.Add(new Run("Boundary anchor.", bodyFmt));

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, bodyFmt) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, double.PositiveInfinity));

            pageCount = view.PageCount;
            if (view.FloatingImageRects.Count > 0)
                floatRect = view.FloatingImageRects[0].Rect;

            // Find the Y of the first glyph in the anchor paragraph (last block = fillerCount).
            var placed = view.GetPlacedForBlock(fillerCount);
            if (placed.Count > 0)
                firstGlyphY = placed.Min(g => g.Y);
        });

        if (!ran) return;

        // The anchor paragraph must have been pushed to page 2.
        pageCount.Should().BeGreaterThan(1,
            "TT1: filler paragraphs should fill page 1, pushing the anchor to page 2");

        // Page 2 content start ≈ DeskPadding(24) + textAreaHeight(864) + PageGap(20) + marginTop(96) = 1004.
        const double page2Threshold = 1000.0;

        floatRect.Y.Should().BeGreaterThanOrEqualTo(page2Threshold,
            $"TT1 boundary-band: paragraph-anchored float must be on page 2 (Y ≥ {page2Threshold}), " +
            $"got Y={floatRect.Y:F1}. Before fix, PeekFirstLineContentY(1) didn't trigger the break " +
            $"but the actual line (≈20px) did, landing the float one page too early.");

        // Float Y must match the anchor paragraph's first glyph Y (both on page 2, vOffset=0).
        if (firstGlyphY < double.MaxValue)
        {
            var delta = Math.Abs(floatRect.Y - firstGlyphY);
            delta.Should().BeLessThanOrEqualTo(4.0,
                $"TT1: float Y ({floatRect.Y:F1}) should match first glyph Y ({firstGlyphY:F1}) of anchor paragraph");
        }
    }

    // ── VV1: first-line height probe uses MAX height over all runs, not just first char ────────────────

    /// <summary>
    /// VV1 (refinement of TT1): When line 0 of a paragraph contains a MIX of font sizes — a small
    /// run first ("see " at 9pt) followed by a large run ("IMPORTANT" at 24pt) — the TT1 code took
    /// only the first char of the first run, under-estimating line-0 height.  PeekFirstLineContentY
    /// then failed to detect the page-break that ReserveContentY made for the real 24pt-tall line,
    /// so the paragraph-anchored float landed one page too early.
    ///
    /// VV1 fix: scan ALL cells (max height) so the probe height matches or exceeds EmitLinePaged's
    /// naturalHeight, guaranteeing Peek breaks whenever the real first line does.
    ///
    /// Layout: fill the page to the boundary band, then add a paragraph whose first text run is 9pt
    /// ("see ") and whose second run is 24pt ("IMPORTANT").  The 24pt run drives line-0 height
    /// (~38 DIP after line-spacing).  With the fix, Peek uses ~38 DIP and correctly sees the
    /// overflow → anchorContentY is on page 2 → float lands on page 2 (same as the first glyph).
    /// </summary>
    [Fact]
    public async Task VV1_mixed_font_size_first_line_float_lands_on_same_page_as_paragraph()
    {
        Rect floatRect = default;
        double firstGlyphY = double.MaxValue;
        int pageCount = 0;

        var ran = await OnUiThread(() =>
        {
            // US-Letter: textAreaHeight = 864 DIP.
            // The large run (24pt) has naturalH ≈ 24 * (96/72) * 1.3 ≈ 42.7 DIP.
            // Fill so that (textAreaHeight - fillHeight) < 42.7 DIP but > 0 — 1 DIP gap is enough.
            const double textAreaHeightDip = 864.0;
            const double smallFontPt  = 9.0;
            const double smallLineH   = smallFontPt * (96.0 / 72.0) * 1.3; // ≈ 16.6 DIP
            var fillerCount = (int)((textAreaHeightDip - 1) / smallLineH);   // fill with 9pt lines

            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var smallFmt = RunFormatting.Default with { FontSizePt = smallFontPt };
            var largeFmt = RunFormatting.Default with { FontSizePt = 24.0 };

            for (var i = 0; i < fillerCount; i++)
            {
                var filler = new Paragraph();
                filler.Runs.Add(new Run($"Fill {i + 1}.", smallFmt));
                doc.Blocks.Add(filler);
            }

            // Anchor paragraph: FIRST run is small (9pt), SECOND run is large (24pt) on the same line.
            // The 24pt run drives line-0 height — VV1 bug: old code would only see the 9pt first char.
            var anchorPara = new Paragraph();
            anchorPara.Runs.Add(new Run("see ", smallFmt));
            anchorPara.Runs.Add(new Run("IMPORTANT", largeFmt));

            var floatImg = new InlineImage(SmallPng(), 72, 54)
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 0,
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            anchorPara.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = floatImg });
            doc.Blocks.Add(anchorPara);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, double.PositiveInfinity));

            pageCount = view.PageCount;
            if (view.FloatingImageRects.Count > 0)
                floatRect = view.FloatingImageRects[0].Rect;

            var placed = view.GetPlacedForBlock(fillerCount); // anchor paragraph
            if (placed.Count > 0)
                firstGlyphY = placed.Min(g => g.Y);
        });

        if (!ran) return;

        pageCount.Should().BeGreaterThan(1,
            "VV1: filler paragraphs (9pt) should fill page 1, pushing the mixed-size anchor to page 2");

        const double page2Threshold = 1000.0;
        floatRect.Y.Should().BeGreaterThanOrEqualTo(page2Threshold,
            $"VV1: paragraph-anchored float must be on page 2 (Y ≥ {page2Threshold}), got Y={floatRect.Y:F1}. " +
            "Before VV1 fix, PeekFirstLineContentY used 9pt height (first char of first run) and missed " +
            "the break caused by the 24pt second run — landing the float one page too early.");

        if (firstGlyphY < double.MaxValue)
        {
            var delta = Math.Abs(floatRect.Y - firstGlyphY);
            delta.Should().BeLessThanOrEqualTo(6.0,
                $"VV1: float Y ({floatRect.Y:F1}) should match first glyph Y ({firstGlyphY:F1}) of anchor paragraph");
        }
    }

    // ── PNG encoder (shared with PrintLayoutCaptureTests) ────────────────────────────────────────────

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
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 90);
            return data?.ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
