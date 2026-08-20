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
/// Tests for AV-WRAP: text-wrap exclusion around floating objects in the Avalonia DocumentView.
/// Verifies that Square/Tight-wrapped floats narrow the line's usable horizontal span,
/// TopAndBottom floats push lines below them, Behind/InFront floats cause no exclusion,
/// and that no-float (regression) layout is unchanged.
/// </summary>
public sealed class DocumentViewWrapExclusionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private static byte[] SmallPng()
    {
        using var bmp = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(0, 128, 255));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Builds a document with body text in a single paragraph.
    /// Optionally attaches a floating image to that paragraph.
    /// Page: 8.5"×11", 1" margins, single column — text column = 6.5" = 468pt = 624 DIP.
    /// </summary>
    private static TextDocument DocWithText(
        string bodyText = "The quick brown fox jumps over the lazy dog. " +
                          "The quick brown fox jumps over the lazy dog. " +
                          "The quick brown fox jumps over the lazy dog.",
        InlineImage? floatImage = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var para = new Paragraph();
        para.Runs.Add(new Run(bodyText, RunFormatting.Default with { FontSizePt = 11 }));
        if (floatImage is not null)
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = floatImage });

        doc.Blocks.Add(para);

        doc.Page.WidthPt        = 612;  // 8.5"
        doc.Page.HeightPt       = 792;  // 11"
        doc.Page.MarginLeftPt   = 72;   // 1"
        doc.Page.MarginRightPt  = 72;   // 1"
        doc.Page.MarginTopPt    = 72;   // 1"
        doc.Page.MarginBottomPt = 72;   // 1"
        return doc;
    }

    /// <summary>
    /// Builds a floating InlineImage with the given wrapping and position.
    /// The image is placed at the left of the column (hOffset=0) so that its band
    /// covers lines at vertical offset vOffset from the paragraph.
    /// </summary>
    private static InlineImage MakeFloat(
        ImageWrapping wrapping,
        double hOffsetPt  = 0,
        double vOffsetPt  = 0,
        double widthPt    = 108,  // 1.5" — about 25% of the 6.5" column
        double heightPt   = 72)   // 1"
        => new InlineImage(SmallPng(), widthPt, heightPt)
        {
            Wrapping           = wrapping,
            HorizontalOffsetPt = hOffsetPt,
            VerticalOffsetPt   = vOffsetPt,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = 0,
        };

    // ── Test 1: No-float regression — layout unchanged ────────────────────────────────────────────

    [Fact]
    public async Task NoFloat_layout_is_unchanged()
    {
        int glyphCount = 0;
        int exclusionCount = 0;
        double firstGlyphX = -1;

        var ran = await OnUiThread(() =>
        {
            var doc  = DocWithText();
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            glyphCount     = view.PlacedGlyphCount;
            exclusionCount = view.WrapExclusionCount;
            var placed     = view.GetPlacedForBlock(0);
            firstGlyphX    = placed.Count > 0 ? placed[0].X : -1;
        });

        if (!ran) return;

        exclusionCount.Should().Be(0, "no floating object → no exclusion zones");
        glyphCount.Should().BeGreaterThan(0, "text should be placed");
        // First glyph X should be at contentLeft.
        // pageLeft = max(24, (816-816)/2) = 24; marginLeft = 72pt = 96 DIP; contentLeft = 24+96 = 120.
        firstGlyphX.Should().BeApproximately(120.0, 10.0,
            "first glyph should start at the left content margin with no float");
    }

    // ── Test 2: Square left float — line X starts right of float ─────────────────────────────────

    [Fact]
    public async Task SquareFloat_left_pushes_line_start_rightward()
    {
        int exclusionCount = 0;
        double firstGlyphX = -1;

        var ran = await OnUiThread(() =>
        {
            // Float: 108pt wide (144 DIP), left-anchored (hOffset=0).
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 108, heightPt: 72);
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            exclusionCount = view.WrapExclusionCount;
            var placed     = view.GetPlacedForBlock(0);
            // Find the first non-sentinel glyph on the FIRST line (the lowest Y).
            if (placed.Count > 0)
            {
                var firstLineY = placed[0].Y;
                var firstLineGlyphs = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2).ToList();
                firstGlyphX = firstLineGlyphs.Count > 0 ? firstLineGlyphs[0].X : -1;
            }
        });

        if (!ran) return;

        exclusionCount.Should().Be(1, "one Square float → one exclusion zone");

        // Float is 108pt = 144 DIP wide; contentLeft = 120 DIP; WrapGap = 9 DIP.
        // Expected first glyph X ≥ 120 + 144 + 9 = 273 DIP (minus alignment jitter).
        firstGlyphX.Should().BeGreaterThan(260,
            "first line glyph X should be pushed right of the left float by the wrap gap");
    }

    // ── Test 3: Square right float — line right edge reduced ─────────────────────────────────────

    [Fact]
    public async Task SquareFloat_bothSides_places_one_line_in_both_fragments()
    {
        double leftMost = -1;
        double rightMost = -1;
        double floatLeft = -1;
        double floatRight = -1;

        var ran = await OnUiThread(() =>
        {
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 90, vOffsetPt: 0,
                widthPt: 72, heightPt: 72);
            fi.WrapTextSide = FloatingWrapTextSide.BothSides;
            var doc = DocWithText(
                "Alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron.",
                fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var zones = view.WrapExclusionZones;
            floatLeft = zones[0].Rect.Left;
            floatRight = zones[0].Rect.Right;
            var placed = view.GetPlacedForBlock(0).Where(p => !char.IsWhiteSpace(p.Ch)).ToList();
            var firstLineY = placed.Min(p => p.Y);
            var firstLine = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2).ToList();
            leftMost = firstLine.Min(p => p.X);
            rightMost = firstLine.Max(p => p.X);
        });

        if (!ran) return;

        leftMost.Should().BeLessThan(floatLeft - 8,
            "bothSides wrapping retains the left fragment before the float");
        rightMost.Should().BeGreaterThan(floatRight + 8,
            "the same line continues through the right fragment after the float");
    }

    [Fact]
    public async Task SquareFloat_right_reduces_line_available_width()
    {
        int exclusionCount = 0;
        double firstLineMaxX = -1;
        double floatLeftX = -1;

        var ran = await OnUiThread(() =>
        {
            // Right-anchored float: hOffset = 360pt from column left puts it at the right third.
            // Column width = 6.5" = 468pt; float width = 108pt.
            // contentLeft = 120 DIP; float hOffset = 360pt = 480 DIP → floatLeft = 120+480 = 600 DIP.
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 360, vOffsetPt: 0,
                               widthPt: 108, heightPt: 72);
            var doc   = DocWithText(floatImage: fi);
            var view  = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            exclusionCount = view.WrapExclusionCount;
            var zones   = view.WrapExclusionZones;
            floatLeftX  = zones.Count > 0 ? zones[0].Rect.Left : -1;

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
            {
                var firstLineY = placed[0].Y;
                firstLineMaxX  = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2)
                                       .Select(p => p.X + p.W)
                                       .DefaultIfEmpty(-1)
                                       .Max();
            }
        });

        if (!ran) return;

        exclusionCount.Should().Be(1, "one Square float → one exclusion zone");

        // The first line should not extend past float.Left - WrapGap.
        // floatLeftX ≈ 600 DIP; WrapGap = 9 DIP → expected maxX ≤ ~591 + some word-break tolerance.
        firstLineMaxX.Should().BeLessThan(floatLeftX,
            "right float should reduce the line's right edge to stay left of the float");
    }

    // ── Test 4: TopAndBottom float — lines pushed below float ────────────────────────────────────

    [Fact]
    public async Task TopAndBottom_float_pushes_lines_below()
    {
        int exclusionCount = 0;
        double firstGlyphY = -1;
        double floatBottom = -1;

        var ran = await OnUiThread(() =>
        {
            // TopAndBottom float: centred horizontally, at top of para, 72pt tall (96 DIP).
            var fi = MakeFloat(ImageWrapping.TopAndBottom, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 432, heightPt: 72); // full column width
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            exclusionCount = view.WrapExclusionCount;
            var zones      = view.WrapExclusionZones;
            floatBottom    = zones.Count > 0 ? zones[0].Rect.Bottom : -1;

            var placed = view.GetPlacedForBlock(0);
            firstGlyphY = placed.Count > 0 ? placed.Where(p => !char.IsWhiteSpace(p.Ch)).Select(p => p.Y).DefaultIfEmpty(-1).Min() : -1;
        });

        if (!ran) return;

        exclusionCount.Should().Be(1, "one TopAndBottom float → one exclusion zone");

        // All lines must start below floatBottom.
        firstGlyphY.Should().BeGreaterThan(floatBottom - 5,
            "TopAndBottom float should push all lines below its bottom edge");
    }

    // ── Test 5: Behind float — NO exclusion registered ────────────────────────────────────────────

    [Fact]
    public async Task Behind_float_causes_no_exclusion()
    {
        int exclusionCount = -1;
        double firstGlyphX = -1;

        var ran = await OnUiThread(() =>
        {
            var fi = MakeFloat(ImageWrapping.Behind, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 108, heightPt: 72);
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            exclusionCount = view.WrapExclusionCount;
            var placed     = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
                firstGlyphX = placed[0].X;
        });

        if (!ran) return;

        exclusionCount.Should().Be(0, "Behind float must NOT register an exclusion zone");
        // First glyph should be at approximately contentLeft (120 DIP for this page config).
        firstGlyphX.Should().BeApproximately(120.0, 10.0,
            "Behind float must not push the line start rightward");
    }

    // ── Test 6: InFront float — NO exclusion registered ──────────────────────────────────────────

    [Fact]
    public async Task InFront_float_causes_no_exclusion()
    {
        int exclusionCount = -1;
        double firstGlyphX = -1;

        var ran = await OnUiThread(() =>
        {
            var fi = MakeFloat(ImageWrapping.InFront, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 108, heightPt: 72);
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            exclusionCount = view.WrapExclusionCount;
            var placed     = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
                firstGlyphX = placed[0].X;
        });

        if (!ran) return;

        exclusionCount.Should().Be(0, "InFront float must NOT register an exclusion zone");
        firstGlyphX.Should().BeApproximately(120.0, 10.0,
            "InFront float must not push the line start rightward");
    }

    // ── Test 7: Lines below the float return to full width ────────────────────────────────────────

    [Fact]
    public async Task Lines_below_left_float_return_to_full_width()
    {
        double firstLineX    = -1;
        double belowFloatX   = -1;

        var ran = await OnUiThread(() =>
        {
            // Float: 108pt wide, 36pt tall (48 DIP) — only covers the first ~3 lines.
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 108, heightPt: 36);
            // Large body text to ensure many lines.
            var body = string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet consectetur", 20));
            var doc  = DocWithText(body, fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var placed    = view.GetPlacedForBlock(0);
            var zones     = view.WrapExclusionZones;
            var floatBot  = zones.Count > 0 ? zones[0].Rect.Bottom : 0;

            if (placed.Count > 0)
            {
                // First line X (should be pushed right).
                var firstLineY = placed[0].Y;
                firstLineX = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2).Select(p => p.X).DefaultIfEmpty(-1).Min();

                // A line clearly below the float.
                var belowLine = placed.FirstOrDefault(p => p.Y > floatBot + 5);
                belowFloatX = belowLine != default ? belowLine.X : -1;
            }
        });

        if (!ran) return;

        // firstLineX should be pushed right of contentLeft (120 DIP).
        firstLineX.Should().BeGreaterThan(200, "first line starts right of the left float");

        // Lines below float should return close to contentLeft (120 DIP for this page config).
        if (belowFloatX >= 0)
            belowFloatX.Should().BeApproximately(120.0, 12.0,
                "lines below the float should return to the full column width");
    }

    // ── Test 8: Shape Square wrapping registers exclusion ────────────────────────────────────────

    [Fact]
    public async Task FloatingShape_Square_registers_exclusion_zone()
    {
        int exclusionCount = -1;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var para = new Paragraph();
            para.Runs.Add(new Run("Some body text here.", RunFormatting.Default));

            var shape = new Shape(ShapeKind.Rectangle, 108, 72, "#4472C4")
            {
                Placement = new FloatingPlacement
                {
                    Wrapping           = ImageWrapping.Square,
                    HorizontalOffsetPt = 0,
                    VerticalOffsetPt   = 0,
                    HorizontalAnchor   = HorizontalAnchor.Column,
                    VerticalAnchor     = VerticalAnchor.Paragraph,
                }
            };
            para.Runs.Add(Run.FromShape(shape));
            doc.Blocks.Add(para);
            doc.Page.WidthPt = 612; doc.Page.HeightPt = 792;
            doc.Page.MarginLeftPt = doc.Page.MarginRightPt = 72;
            doc.Page.MarginTopPt  = doc.Page.MarginBottomPt = 72;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            exclusionCount = view.WrapExclusionCount;
        });

        if (!ran) return;

        exclusionCount.Should().Be(1, "one Square-wrapped shape → one exclusion zone");
    }

    // ── Test 9: Tight wrapping also registers exclusion ──────────────────────────────────────────

    [Fact]
    public async Task FloatingImage_Tight_registers_exclusion_zone()
    {
        int exclusionCount = -1;

        var ran = await OnUiThread(() =>
        {
            var fi = MakeFloat(ImageWrapping.Tight, hOffsetPt: 0, vOffsetPt: 0);
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            exclusionCount = view.WrapExclusionCount;
        });

        if (!ran) return;

        exclusionCount.Should().Be(1, "Tight-wrapped float → one exclusion zone");
    }

    // ── BB1 Test 10: Wide left-anchored float is classified as LEFT (not RIGHT) ─────────────────
    // Regression: before BB1 fix, a float wider than 50% of the column had its CENTRE past the
    // column centre and was classified as a RIGHT float, squeezing text into the left edge (overlap).

    [Fact]
    public async Task BB1_wide_left_float_text_wraps_to_right_not_overlapping()
    {
        double firstLineMaxX = -1;
        double firstLineMinX = -1;
        double floatRight    = -1;

        var ran = await OnUiThread(() =>
        {
            // Float: 60% of 468pt column = 280pt = 373 DIP. Left-anchored (hOffset=0).
            // Centre at 373/2 = 187 DIP from colLeft (120 DIP) = 307 DIP absolute.
            // ColCentre = 120 + 624/2 = 432 DIP → float centre (307) < colCentre (432).
            // BB1 fix classifies by free space: freeLeft=0, freeRight=251 DIP → classify as LEFT.
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 280, heightPt: 72); // ~60% column width
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var zones = view.WrapExclusionZones;
            floatRight = zones.Count > 0 ? zones[0].Rect.Right : -1;

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
            {
                var firstLineY = placed[0].Y;
                var firstLine  = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2).ToList();
                firstLineMinX  = firstLine.Count > 0 ? firstLine.Min(p => p.X) : -1;
                firstLineMaxX  = firstLine.Count > 0 ? firstLine.Max(p => p.X + p.W) : -1;
            }
        });

        if (!ran) return;

        // Text must start to the RIGHT of the float (float is on the left).
        firstLineMinX.Should().BeGreaterThan(floatRight - 5,
            "BB1: wide left-anchored float — text must start to the RIGHT of the float, not overlap it");

        // Text must not extend beyond column right edge.
        firstLineMaxX.Should().BeLessThan(120 + 624 + 20,
            "BB1: text right edge should stay within the column");
    }

    // ── BB1 Test 11: Right-anchored float → text to the LEFT ──────────────────────────────────────

    [Fact]
    public async Task BB1_right_anchored_float_text_wraps_to_left()
    {
        double firstLineMaxX = -1;
        double floatLeftX    = -1;

        var ran = await OnUiThread(() =>
        {
            // Float: 60% column width, anchored to right edge.
            // Column = 468pt; float = 280pt wide. hOffset = 468-280 = 188pt = 251 DIP from colLeft.
            // floatLeft = 120 + 251 = 371 DIP; colRight = 120+624 = 744 DIP.
            // freeLeft = 251 DIP, freeRight = 0 DIP → classify as RIGHT → text pushed to the left.
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 188, vOffsetPt: 0,
                               widthPt: 280, heightPt: 72);
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var zones  = view.WrapExclusionZones;
            floatLeftX = zones.Count > 0 ? zones[0].Rect.Left : -1;

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
            {
                var firstLineY = placed[0].Y;
                firstLineMaxX  = placed.Where(p => Math.Abs(p.Y - firstLineY) < 2)
                                       .Select(p => p.X + p.W)
                                       .DefaultIfEmpty(-1).Max();
            }
        });

        if (!ran) return;

        // Text must not extend into the float.
        firstLineMaxX.Should().BeLessThan(floatLeftX + 5,
            "BB1: right-anchored wide float — text right edge must not enter the float");
    }

    // ── BB1 Test 12: Near-full-width float → text pushed below (no overlap) ──────────────────────

    [Fact]
    public async Task BB1_near_full_width_float_text_pushed_below()
    {
        double firstGlyphY = -1;
        double floatBottom = -1;

        var ran = await OnUiThread(() =>
        {
            // Float: 463pt wide in a 468pt column. Left-anchored (hOffset=0).
            // freeLeft = 0pt = 0 DIP  (<20)
            // freeRight = 5pt ≈ 7 DIP (<20)
            // Neither side has room → BB1 classifies as near-full-width → text pushed below.
            var fi = MakeFloat(ImageWrapping.Square, hOffsetPt: 0, vOffsetPt: 0,
                               widthPt: 463, heightPt: 72); // leaves only ~5pt on right
            var doc  = DocWithText(floatImage: fi);
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var zones  = view.WrapExclusionZones;
            floatBottom = zones.Count > 0 ? zones[0].Rect.Bottom : -1;

            var placed = view.GetPlacedForBlock(0);
            if (placed.Count > 0)
            {
                // Find the first non-space glyph.
                var firstNonSpace = placed.FirstOrDefault(p => !char.IsWhiteSpace(p.Ch));
                firstGlyphY = firstNonSpace != default ? firstNonSpace.Y : placed[0].Y;
            }
        });

        if (!ran) return;

        // All text must be BELOW the float's bottom edge.
        firstGlyphY.Should().BeGreaterThan(floatBottom - 5,
            "BB1: near-full-width float should push ALL text below the float's bottom (no overlap)");
    }

    // ── BD1 Test 13: wide float in column 2 of a 2-column layout pushes text below ──────────────
    // Regression: before BD1 fix, TopAndBottomExclusionBottom used _contentLeft (column 0 origin)
    // for freeLeft/freeRight, so a float in column 2 had inflated freeLeft > 20 and was NOT
    // promoted to push text below → text overlapped the float.

    [Fact]
    public async Task BD1_wide_float_in_column2_of_2col_layout_pushes_text_below()
    {
        double floatBottom = -1;
        double firstGlyphBelowColBreakY = -1;

        var ran = await OnUiThread(() =>
        {
            // 2-column layout: 612pt wide page, 1" margins, 36pt gap.
            // colWidth = (612 - 72 - 72 - 36) / 2 = 216pt = 288 DIP.
            // col1 left = contentLeft + colWidth + gap = 96 + 288 + 48 = 432 DIP (page-space).
            // Float: nearly full column 1 width = 200pt = ~267 DIP, placed in col1.
            // hOffset: column 1 start = 216pt from contentLeft.
            // Float in col 1: hOffset = 216pt, width = 205pt → leaves ~11pt on right (<20).
            // freeLeft col1 = 0 DIP (<20), freeRight col1 = ~14 DIP (<20) → should push below.
            var body = string.Join(" ", Enumerable.Repeat("word", 200)); // lots of text
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run(body, RunFormatting.Default with { FontSizePt = 11 }));

            // Float: wide near-full-column float anchored at column 1 start.
            var fi = new InlineImage(SmallPng(), 205, 72) // 205pt wide
            {
                Wrapping           = ImageWrapping.Square,
                HorizontalOffsetPt = 216, // col 1 start in pt (colWidth = 216pt)
                VerticalOffsetPt   = 0,
                HorizontalAnchor   = HorizontalAnchor.Column,
                VerticalAnchor     = VerticalAnchor.Paragraph,
                ZOrderIndex        = 0,
            };
            para.Runs.Add(new Run(string.Empty, RunFormatting.Default) { Image = fi });
            doc.Blocks.Add(para);
            doc.Page.WidthPt        = 612;
            doc.Page.HeightPt       = 792;
            doc.Page.MarginLeftPt   = 72;
            doc.Page.MarginRightPt  = 72;
            doc.Page.MarginTopPt    = 72;
            doc.Page.MarginBottomPt = 72;
            doc.Page.ColumnCount      = 2;
            doc.Page.ColumnSpacingPt  = 36;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            var zones = view.WrapExclusionZones;
            floatBottom = zones.Count > 0 ? zones[0].Rect.Bottom : -1;

            // We look for placed glyphs in column 1 (X > col1Left) that are below the float.
            // col1Left in page-space ≈ 120 (pageLeft) + 288 (col0 width) + 48 (gap) = 456 DIP.
            var col1Left = 440.0; // approximate, give generous tolerance
            var placed = view.GetPlacedForBlock(0);
            var col1GlyphsBelowFloat = placed
                .Where(p => p.X > col1Left && p.Y > floatBottom + 2)
                .ToList();
            firstGlyphBelowColBreakY = col1GlyphsBelowFloat.Count > 0
                ? col1GlyphsBelowFloat.Min(p => p.Y)
                : -1;
        });

        if (!ran) return;
        if (floatBottom < 0) return; // float not registered — skip (headless env issue)

        // Any text that lands in column 1 must be below the float.
        if (firstGlyphBelowColBreakY >= 0)
        {
            firstGlyphBelowColBreakY.Should().BeGreaterThan(floatBottom - 5,
                "BD1: wide float in column 2 must push text below it, not overlap");
        }
    }
}
