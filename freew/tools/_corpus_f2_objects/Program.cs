// _corpus_f2_objects — generates 7 FreeW f2-objects verification corpus .docx files.
// These are purpose-built documents for the 2026-06-26 visual-fidelity verification pass
// that exercises composite-render features (floating objects, page chrome, multi-column).
//
// Usage: _corpus_f2_objects <outDir>
//
// Files generated:
//   f2-objects-01-float-wrap.docx — floating image, Square wrap; floating image, Tight wrap
//   f2-02-float-zorder.docx      — three overlapping floating shapes (z-order: blue<orange<green)
//   f2-03-object-effects.docx    — inline shape with drop shadow; shape with glow; image with shadow
//   f2-04-border-watermark.docx  — navy page border + diagonal DRAFT watermark
//   f2-05-columns-2.docx         — 2-column layout with column rule
//   f2-06-columns-3.docx         — 3-column layout with column rule
//   f2-07-combined.docx          — page border + watermark + 2-col + floating shape in one document

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.IO;
using FreeW.Core.Model;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: _corpus_f2_objects <outDir>");
    return 1;
}

string outDir = args[0];
Directory.CreateDirectory(outDir);

// ── Generate sample PNG bytes ─────────────────────────────────────────────────────────────────────

byte[] solidRedPng    = MakeSolidColorPng(Color.FromRgb(0xC0, 0x00, 0x00), 120, 90);
byte[] solidBluePng   = MakeSolidColorPng(Color.FromRgb(0x00, 0x44, 0x72), 120, 90);
byte[] gradientPng    = MakeGradientPng(120, 90);

// ── Shared helpers ────────────────────────────────────────────────────────────────────────────────

static Paragraph Body(string text) => new Paragraph(text);

static Paragraph BodyRuns(params Run[] runs)
{
    var p = new Paragraph();
    foreach (var r in runs) p.Runs.Add(r);
    return p;
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-01: Floating image — Square + Tight text wrap
// Expected: gradient image appears at left margin near top of text; checker image appears on right;
//           text flows around each image (square/tight margin); images are visible not missing.
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Blocks.Add(Body("F2-01: Floating image with Square and Tight text wrap. " +
        "The RED image (Square wrap) should appear at the left, with text flowing around it. " +
        "The BLUE image (Tight wrap) should appear further right. " +
        "Both images must be VISIBLE — absence means composite floating layer is not rendered."));

    // Square-wrapped floating image
    var floatSquare = new InlineImage(solidRedPng, 108, 81)
    {
        Wrapping           = ImageWrapping.Square,
        HorizontalOffsetPt = 40,
        VerticalOffsetPt   = 60,
        HorizontalAnchor   = HorizontalAnchor.Margin,
        VerticalAnchor     = VerticalAnchor.Page,
        ZOrderIndex        = 10,
        AltText            = "Float Square Red",
    };

    var body1 = new Paragraph();
    body1.Runs.Add(Run.FromImage(floatSquare));
    body1.Runs.Add(new Run(LoremText(8)));
    doc.Blocks.Add(body1);

    // Tight-wrapped floating image
    var floatTight = new InlineImage(solidBluePng, 108, 81)
    {
        Wrapping           = ImageWrapping.Tight,
        HorizontalOffsetPt = 300,
        VerticalOffsetPt   = 60,
        HorizontalAnchor   = HorizontalAnchor.Margin,
        VerticalAnchor     = VerticalAnchor.Page,
        ZOrderIndex        = 11,
        AltText            = "Float Tight Blue",
    };

    var body2 = new Paragraph();
    body2.Runs.Add(Run.FromImage(floatTight));
    body2.Runs.Add(new Run(LoremText(8)));
    doc.Blocks.Add(body2);

    for (int i = 0; i < 4; i++)
        doc.Blocks.Add(Body(LoremText(10)));

    // Keep this exploratory corpus distinct from FidelityRender's canonical
    // f2-01-float-wrap Word-baseline fixture in the same run directory.
    DocxWriter.Write(doc, Path.Combine(outDir, "f2-objects-01-float-wrap.docx"));
    Console.WriteLine("wrote f2-objects-01-float-wrap.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-02: Overlapping floating shapes, z-order
// Expected: Blue rect (z=1) lowest, Orange ellipse (z=2) in middle, Green rect (z=3) on top.
//           All three shapes must be VISIBLE and stacked in the correct z-order.
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Blocks.Add(Body("F2-02: Overlapping floating shapes. " +
        "Three shapes must overlap at the left: " +
        "BLUE rectangle (z=1) should be behind ORANGE ellipse (z=2) behind GREEN roundrect (z=3). " +
        "Absence of all shapes = floating canvas not composited. Wrong z-order = sort bug."));

    var anchorPara = new Paragraph();

    // Blue rectangle — bottom (z=1)
    anchorPara.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Rectangle,
        WidthPt         = 130,
        HeightPt        = 95,
        FillColorHex    = "#4472C4",
        OutlineColorHex = "#1F3864",
        OutlineWidthPt  = 2.0,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 50,
            VerticalOffsetPt   = 80,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
            ZOrderIndex        = 1,
        }
    }));

    // Orange ellipse — middle (z=2)
    anchorPara.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Ellipse,
        WidthPt         = 130,
        HeightPt        = 95,
        FillColorHex    = "#ED7D31",
        OutlineColorHex = "#843C0C",
        OutlineWidthPt  = 1.5,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 110,
            VerticalOffsetPt   = 110,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
            ZOrderIndex        = 2,
        }
    }));

    // Green rounded rect — top (z=3)
    anchorPara.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.RoundedRectangle,
        WidthPt         = 130,
        HeightPt        = 75,
        FillColorHex    = "#70AD47",
        OutlineColorHex = "#375623",
        OutlineWidthPt  = 1.5,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 80,
            VerticalOffsetPt   = 140,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
            ZOrderIndex        = 3,
        }
    }));

    anchorPara.Runs.Add(new Run(LoremText(10)));
    doc.Blocks.Add(anchorPara);

    for (int i = 0; i < 3; i++)
        doc.Blocks.Add(Body(LoremText(10)));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-02-float-zorder.docx"));
    Console.WriteLine("wrote f2-02-float-zorder.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-03: WPF effects on objects — shadow + glow on shapes; shadow on inline image
// Expected: shapes visible; composite per-child DrawImage path for floating shapes; effects MAY or
//           MAY NOT render (the key question). The shapes themselves must at minimum be present.
//           If shadow/glow appear: CONFIRMED. If shapes present but no effect halo: effect-overflow
//           clipping is the confirmed gap (not a new bug, but now precisely documented).
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Blocks.Add(Body("F2-03: WPF effects on objects (drop shadow, glow). " +
        "Inline shapes below: RED rect with drop shadow; TEAL ellipse with blue glow. " +
        "Floating IMAGE with shadow at right. " +
        "Inspect: do shadow/glow halos appear? They are WPF Effects that may be clipped."));

    // Inline shape — drop shadow
    doc.Blocks.Add(BodyRuns(
        Run.FromShape(new Shape
        {
            Kind            = ShapeKind.Rectangle,
            WidthPt         = 120,
            HeightPt        = 90,
            FillColorHex    = "#C00000",
            OutlineColorHex = "#600000",
            OutlineWidthPt  = 1.0,
            Effects         = new ShapeEffectLst
            {
                HasShadow      = true,
                ShadowColorHex = "242424",
                ShadowAlpha    = 50000,
                ShadowBlurRad  = 76200,   // 6 pt
                ShadowDist     = 63500,   // 5 pt
                ShadowDir      = 2700000, // 45 deg
            }
        }),
        new Run("   (shape with drop shadow)   "),
        Run.FromShape(new Shape
        {
            Kind            = ShapeKind.Ellipse,
            WidthPt         = 120,
            HeightPt        = 90,
            FillColorHex    = "#008080",
            OutlineWidthPt  = 0,
            Effects         = new ShapeEffectLst
            {
                HasGlow      = true,
                GlowColorHex = "4472C4",
                GlowRad      = 76200,  // 6 pt
                GlowAlpha    = 65000,
            }
        }),
        new Run("   (shape with glow)")
    ));

    // Floating image with shadow effect — tests whether DrawImage path carries effects
    var floatShadowImg = new InlineImage(gradientPng, 108, 81)
    {
        Wrapping           = ImageWrapping.Square,
        HorizontalOffsetPt = 350,
        VerticalOffsetPt   = 80,
        HorizontalAnchor   = HorizontalAnchor.Margin,
        VerticalAnchor     = VerticalAnchor.Page,
        ZOrderIndex        = 1,
        ShadowPreset       = 2,
        AltText            = "Floating gradient with shadow",
    };

    var body2 = new Paragraph();
    body2.Runs.Add(Run.FromImage(floatShadowImg));
    body2.Runs.Add(new Run(LoremText(8) + " (text alongside floating image with shadow preset)"));
    doc.Blocks.Add(body2);

    for (int i = 0; i < 3; i++)
        doc.Blocks.Add(Body(LoremText(10)));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-03-object-effects.docx"));
    Console.WriteLine("wrote f2-03-object-effects.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-04: Page border + diagonal watermark
// Expected: navy rectangle border visible around page edges; "DRAFT" text tiled diagonally at 40%
//           opacity across the page background. Both absent = chrome layers not composited.
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Page.PageBorder       = new PageBorder("#000080", 3.0);
    doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
    {
        FontColorHex = "#808080",
        Opacity      = 0.40,
        Layout       = WatermarkLayout.Diagonal,
    };

    doc.Blocks.Add(Body("F2-04: Page border (navy, 3 pt) + DRAFT watermark (diagonal, 40% grey). " +
        "A navy rectangle border must be visible around all four page edges. " +
        "DRAFT must appear tiled diagonally in grey across the page. " +
        "Both absent = composite chrome layers (Layer 1b and Layer 3) not rendering."));

    for (int i = 0; i < 6; i++)
        doc.Blocks.Add(Body(LoremText(12)));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-04-border-watermark.docx"));
    Console.WriteLine("wrote f2-04-border-watermark.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-05: 2-column layout with column rule
// Expected: page body splits into two equal columns; a thin grey vertical rule between them.
//           Single-column = ApplyColumnLayout not invoked in composite path.
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Page.ColumnCount          = 2;
    doc.Page.ColumnSpacingPt      = 36;
    doc.Page.ColumnsLineBetween   = true;

    doc.Blocks.Add(Body("F2-05: Two-column layout with column rule. " +
        "This text must split into two columns separated by a thin grey vertical rule. " +
        "Single column = ApplyColumnLayout was not called in composite render path."));

    for (int i = 0; i < 35; i++)
        doc.Blocks.Add(Body($"Paragraph {i + 1}: {LoremText(12)}"));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-05-columns-2.docx"));
    Console.WriteLine("wrote f2-05-columns-2.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-06: 3-column layout with column rule
// Expected: body splits into three equal columns; vertical rules between each pair.
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Page.ColumnCount        = 3;
    doc.Page.ColumnSpacingPt    = 24;
    doc.Page.ColumnsLineBetween = true;

    doc.Blocks.Add(Body("F2-06: Three-column layout. " +
        "Body must render in three equal columns with vertical rules between them."));

    for (int i = 0; i < 50; i++)
        doc.Blocks.Add(Body($"Para {i + 1}: {LoremText(10)}"));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-06-columns-3.docx"));
    Console.WriteLine("wrote f2-06-columns-3.docx");
}

// ════════════════════════════════════════════════════════════════════════════════════════════════════
// FILE f2-07: Combined — page border + watermark + 2-col + floating shape
// Expected: all four features active simultaneously:
//   - Navy border around page edges
//   - CONFIDENTIAL watermark diagonal
//   - Two-column body
//   - Green floating rectangle in front of text (z=1)
// ════════════════════════════════════════════════════════════════════════════════════════════════════
{
    var doc = new TextDocument();
    doc.Blocks.Clear();
    doc.Page.PageBorder         = new PageBorder("#003366", 2.0);
    doc.Page.WatermarkOptions   = new WatermarkOptions("CONFIDENTIAL")
    {
        FontColorHex = "#CC0000",
        Opacity      = 0.25,
        Layout       = WatermarkLayout.Diagonal,
    };
    doc.Page.ColumnCount        = 2;
    doc.Page.ColumnSpacingPt    = 36;
    doc.Page.ColumnsLineBetween = true;

    doc.Blocks.Add(Body("F2-07: Combined — page border + watermark + 2-col + floating shape. " +
        "All four composite layers must be active at once. " +
        "Green floating rectangle (InFront z=1) should overlay column text. " +
        "Border: dark navy. Watermark: CONFIDENTIAL red at 25 pct opacity."));

    // Floating green rect overlaid on the column text
    var anchorPara = new Paragraph();
    anchorPara.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Rectangle,
        WidthPt         = 100,
        HeightPt        = 70,
        FillColorHex    = "#70AD47",
        OutlineColorHex = "#375623",
        OutlineWidthPt  = 1.5,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 60,
            VerticalOffsetPt   = 120,
            HorizontalAnchor   = HorizontalAnchor.Margin,
            VerticalAnchor     = VerticalAnchor.Page,
            ZOrderIndex        = 1,
        }
    }));
    anchorPara.Runs.Add(new Run(LoremText(8)));
    doc.Blocks.Add(anchorPara);

    for (int i = 0; i < 30; i++)
        doc.Blocks.Add(Body($"Para {i + 1}: {LoremText(10)}"));

    DocxWriter.Write(doc, Path.Combine(outDir, "f2-07-combined.docx"));
    Console.WriteLine("wrote f2-07-combined.docx");
}

Console.WriteLine($"\nDone — 7 files in {outDir}");
return 0;

// ── PNG helpers ───────────────────────────────────────────────────────────────────────────────────

static byte[] MakeSolidColorPng(Color color, int width, int height)
{
    var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    var dv  = new System.Windows.Media.DrawingVisual();
    using (var dc = dv.RenderOpen())
        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
    bmp.Render(dv);
    return EncodePng(bmp);
}

static byte[] MakeGradientPng(int width, int height)
{
    var bmp    = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    for (int x = 0; x < width; x++)
    {
        double tx = (double)x / (width  - 1);
        double ty = (double)y / (height - 1);
        byte r = (byte)(255 * (1 - tx) * (1 - ty * 0.3));
        byte g = (byte)(80  * tx       * (1 - ty * 0.3));
        byte b = (byte)(255 * tx       * (1 - ty * 0.3));
        int idx = (y * width + x) * 4;
        pixels[idx + 0] = b;   // Pbgra32: B G R A
        pixels[idx + 1] = g;
        pixels[idx + 2] = r;
        pixels[idx + 3] = 255;
    }
    bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return EncodePng(bmp);
}

static byte[] EncodePng(BitmapSource bmp)
{
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}

static string LoremText(int words)
{
    var lorem = "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor " +
                "incididunt ut labore et dolore magna aliqua Ut enim ad minim veniam quis nostrud " +
                "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat Duis aute irure " +
                "dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur ".Split(' ');
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < words; i++)
    {
        if (i > 0) sb.Append(' ');
        sb.Append(lorem[i % lorem.Length]);
    }
    return sb.ToString();
}
