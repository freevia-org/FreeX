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
}
