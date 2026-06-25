// _corpus_drawing — generates 15 FreeW drawing-fidelity corpus .docx files.
// Usage: _corpus_drawing <outDir>
// Writes files to outDir (default: freew-fidelity-corpus/files/drawing relative to cwd).

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.Core.IO;
using FreeW.Core.Model;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: _corpus_drawing <outDir>");
    return 1;
}
string outDir = args[0];
Directory.CreateDirectory(outDir);

// ── Generate sample PNG bytes (used by all image corpus files) ───────────────────────────────────
// We create two sample PNGs: a gradient and a checker pattern.
// WPF bitmap encoding needs STA; we build a minimal 120×90 Pbgra32 WriteableBitmap here.

byte[] gradientPng = MakeGradientPng(120, 90);
byte[] checkerPng  = MakeCheckerPng(120, 90);

// ── Helper: build a Paragraph with a label + an image run ────────────────────────────────────────

static Paragraph Heading(string text) => new(text)
{
    StyleId = "Heading2"
};

static Paragraph Body(string text) => new Paragraph(text);

static Paragraph ImagePara(InlineImage img) =>
    new Paragraph { Runs = { Run.FromImage(img) } };


static Paragraph WordArtPara(WordArt wa) =>
    new Paragraph { Runs = { Run.FromWordArt(wa) } };

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 01 — inline image with a picture border
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("01 · Inline image — picture border"));
    doc.Blocks.Add(Body("The gradient image below has a 2.25 pt solid red border."));

    var img = new InlineImage(gradientPng, 144, 108)
    {
        BorderColorHex = "FF0000",
        BorderWidthPt  = 2.25,
        AltText        = "Gradient with red border",
    };
    doc.Blocks.Add(ImagePara(img));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "01-image-border.docx"));
    Console.WriteLine("wrote 01-image-border.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 02 — inline image with a drop shadow
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("02 · Inline image — drop shadow (preset 2)"));
    doc.Blocks.Add(Body("The checker image has shadow preset 2 (6 pt blur, 5 pt offset)."));

    var img = new InlineImage(checkerPng, 144, 108)
    {
        ShadowPreset = 2,
        AltText      = "Checker with drop shadow",
    };
    doc.Blocks.Add(ImagePara(img));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "02-image-shadow.docx"));
    Console.WriteLine("wrote 02-image-shadow.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 03 — inline image with a glow
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("03 · Inline image — glow (8 pt blue)"));
    doc.Blocks.Add(Body("The gradient image has an 8 pt blue glow."));

    var img = new InlineImage(gradientPng, 144, 108)
    {
        GlowSizePt   = 8,
        GlowColorHex = "4472C4",
        AltText      = "Gradient with blue glow",
    };
    doc.Blocks.Add(ImagePara(img));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "03-image-glow.docx"));
    Console.WriteLine("wrote 03-image-glow.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 04 — inline image with reflection
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("04 · Inline image — reflection (preset 2)"));
    doc.Blocks.Add(Body("The checker image has a reflection below it (preset 2: half, 4 pt gap)."));

    var img = new InlineImage(checkerPng, 144, 108)
    {
        ReflectionPreset = 2,
        AltText          = "Checker with reflection",
    };
    doc.Blocks.Add(ImagePara(img));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "04-image-reflection.docx"));
    Console.WriteLine("wrote 04-image-reflection.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 05 — inline image with soft-edge + bevel
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("05 · Inline image — soft edge (5 pt) and bevel (preset 1)"));
    doc.Blocks.Add(Body("Two images: one with 5 pt soft edge, one with circle bevel."));

    var imgSoft = new InlineImage(gradientPng, 120, 90)
    {
        SoftEdgePt = 5,
        AltText    = "Soft edge gradient",
    };
    var imgBevel = new InlineImage(checkerPng, 120, 90)
    {
        BevelPreset = 1,
        AltText     = "Bevel checker",
    };
    var para = new Paragraph();
    para.Runs.Add(Run.FromImage(imgSoft));
    para.Runs.Add(new Run("   "));
    para.Runs.Add(Run.FromImage(imgBevel));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "05-image-softedge-bevel.docx"));
    Console.WriteLine("wrote 05-image-softedge-bevel.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 06 — image recolor / duotone / color-tone
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("06 · Image recolor — grayscale, sepia, washout, warm tone"));
    doc.Blocks.Add(Body("Four gradient images with different recolor modes."));

    var para = new Paragraph();
    foreach (var (mode, label) in new[]
    {
        (ImageRecolorMode.Grayscale, "Grayscale"),
        (ImageRecolorMode.Sepia,     "Sepia"),
        (ImageRecolorMode.Washout,   "Washout"),
        (ImageRecolorMode.BlackWhite,"B&W"),
    })
    {
        para.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 90, 68)
        {
            RecolorMode = mode,
            AltText     = label,
        }));
        para.Runs.Add(new Run(" "));
    }
    doc.Blocks.Add(para);

    // color temperature
    var para2 = new Paragraph();
    para2.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 90, 68)
        { ColorTemperature = 60, AltText = "Warm +60" }));
    para2.Runs.Add(new Run(" "));
    para2.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 90, 68)
        { ColorTemperature = -60, AltText = "Cool -60" }));
    doc.Blocks.Add(para2);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "06-image-recolor.docx"));
    Console.WriteLine("wrote 06-image-recolor.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 07 — artistic effects
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("07 · Artistic effects — blur, pencil sketch, paintbrush, photocopy"));
    doc.Blocks.Add(Body("Four gradient images with artistic effects applied."));

    var para = new Paragraph();
    foreach (var (effect, label) in new[]
    {
        (ImageArtisticEffect.Blur,           "Blur"),
        (ImageArtisticEffect.PencilGrayscale,"Pencil Grayscale"),
        (ImageArtisticEffect.Paintbrush,     "Paintbrush"),
        (ImageArtisticEffect.Photocopy,      "Photocopy"),
    })
    {
        para.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 100, 75)
        {
            ArtisticEffect = effect,
            AltText        = label,
        }));
        para.Runs.Add(new Run(" "));
    }
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "07-image-artistic.docx"));
    Console.WriteLine("wrote 07-image-artistic.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 08 — image crop + rotate + flip
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("08 · Image crop + rotate + flip"));
    doc.Blocks.Add(Body("Three gradient images: cropped left/right 20%, rotated 30°, flipped H."));

    var para = new Paragraph();
    // Cropped
    para.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 144, 108)
    {
        CropLeft  = 0.20,
        CropRight = 0.20,
        AltText   = "Cropped 20% L+R",
    }));
    para.Runs.Add(new Run("  "));
    // Rotated
    para.Runs.Add(Run.FromImage(new InlineImage(gradientPng, 100, 75)
    {
        RotationAngle = 30,
        AltText       = "Rotated 30°",
    }));
    para.Runs.Add(new Run("  "));
    // Flipped
    para.Runs.Add(Run.FromImage(new InlineImage(checkerPng, 100, 75)
    {
        FlipH   = true,
        AltText = "Flip horizontal",
    }));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "08-image-crop-rotate-flip.docx"));
    Console.WriteLine("wrote 08-image-crop-rotate-flip.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 09 — shape with solid fill + outline
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("09 · Shape — solid fill + outline (rectangle + ellipse)"));
    doc.Blocks.Add(Body("A blue rectangle and an orange ellipse with a 2 pt outline."));

    var para = new Paragraph();
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind           = ShapeKind.Rectangle,
        WidthPt        = 120,
        HeightPt       = 80,
        FillColorHex   = "#4472C4",
        OutlineColorHex = "#1F3864",
        OutlineWidthPt = 2.0,
    }));
    para.Runs.Add(new Run("  "));
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Ellipse,
        WidthPt         = 120,
        HeightPt        = 80,
        FillColorHex    = "#ED7D31",
        OutlineColorHex = "#843C0C",
        OutlineWidthPt  = 2.0,
    }));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "09-shape-solid-outline.docx"));
    Console.WriteLine("wrote 09-shape-solid-outline.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 10 — shape with gradient fill
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("10 · Shape — gradient fill (top-to-bottom and diagonal)"));
    doc.Blocks.Add(Body("Two rectangles: vertical gradient (blue→orange), diagonal gradient (green→white)."));

    var para = new Paragraph();
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind         = ShapeKind.Rectangle,
        WidthPt      = 120,
        HeightPt     = 100,
        ExtendedFill = ShapeFill.LinearGradient(5400000,              // 90° = top→bottom
            new FreeW.Core.Model.GradientStop(0,      "#4472C4"),
            new FreeW.Core.Model.GradientStop(100000, "#ED7D31")),
    }));
    para.Runs.Add(new Run("  "));
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind         = ShapeKind.RoundedRectangle,
        WidthPt      = 120,
        HeightPt     = 100,
        ExtendedFill = ShapeFill.LinearGradient(2700000,              // 45° diagonal
            new FreeW.Core.Model.GradientStop(0,      "#70AD47"),
            new FreeW.Core.Model.GradientStop(100000, "#FFFFFF")),
    }));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "10-shape-gradient.docx"));
    Console.WriteLine("wrote 10-shape-gradient.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 11 — shape with pattern fill
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("11 · Shape — pattern fill (diagonal cross-hatch)"));
    doc.Blocks.Add(Body("Two shapes: diagonal-cross hatch (blue/white) and no-fill outlined ellipse."));

    var para = new Paragraph();
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Rectangle,
        WidthPt         = 120,
        HeightPt        = 100,
        ExtendedFill    = ShapeFill.Patterned("diagCross", "#4472C4", "#FFFFFF"),
        OutlineColorHex = "#4472C4",
        OutlineWidthPt  = 1.0,
    }));
    para.Runs.Add(new Run("  "));
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Ellipse,
        WidthPt         = 120,
        HeightPt        = 100,
        ExtendedFill    = ShapeFill.Patterned("horzBrick", "#A9D18E", "#F4F8F0"),
        OutlineColorHex = "#548235",
        OutlineWidthPt  = 1.5,
    }));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "11-shape-pattern.docx"));
    Console.WriteLine("wrote 11-shape-pattern.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 12 — shape with shadow + glow effect
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("12 · Shape — drop shadow and glow effects"));
    doc.Blocks.Add(Body("A red rectangle with a drop shadow; a teal ellipse with a cyan glow."));

    var para = new Paragraph();
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Rectangle,
        WidthPt         = 120,
        HeightPt        = 90,
        FillColorHex    = "#FF0000",
        OutlineColorHex = "#C00000",
        OutlineWidthPt  = 1.0,
        Effects         = new ShapeEffectLst
        {
            HasShadow     = true,
            ShadowColorHex = "242424",
            ShadowAlpha   = 50000,
            ShadowBlurRad = 63500,
            ShadowDist    = 50800,
            ShadowDir     = 2700000, // 45°
        }
    }));
    para.Runs.Add(new Run("  "));
    para.Runs.Add(Run.FromShape(new Shape
    {
        Kind            = ShapeKind.Ellipse,
        WidthPt         = 120,
        HeightPt        = 90,
        FillColorHex    = "#008080",
        OutlineWidthPt  = 0,
        Effects         = new ShapeEffectLst
        {
            HasGlow       = true,
            GlowColorHex  = "00BFFF",
            GlowRad       = 76200,
            GlowAlpha     = 65000,
        }
    }));
    doc.Blocks.Add(para);
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "12-shape-effects.docx"));
    Console.WriteLine("wrote 12-shape-effects.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 13 — WordArt with style presets and a warp transform
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("13 · WordArt — style presets and warp transforms"));
    doc.Blocks.Add(Body("Four WordArt runs: GradientFill, Shadow, GlowBlue+ArchUp warp, Outline+Wave1 warp."));

    doc.Blocks.Add(WordArtPara(new WordArt
    {
        Text       = "WordArt GradientFill",
        Style      = WordArtStyle.GradientFill,
        FontSizePt = 36,
        Warp       = WordArtWarp.None,
    }));
    doc.Blocks.Add(WordArtPara(new WordArt
    {
        Text       = "WordArt Shadow",
        Style      = WordArtStyle.Shadow,
        FontSizePt = 36,
        Warp       = WordArtWarp.None,
    }));
    doc.Blocks.Add(WordArtPara(new WordArt
    {
        Text       = "ARCH UP",
        Style      = WordArtStyle.GlowBlue,
        FontSizePt = 36,
        Warp       = WordArtWarp.ArchUp,
    }));
    doc.Blocks.Add(WordArtPara(new WordArt
    {
        Text       = "Wave One",
        Style      = WordArtStyle.ChromeOne,
        FontSizePt = 36,
        Warp       = WordArtWarp.Wave1,
    }));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "13-wordart-style-warp.docx"));
    Console.WriteLine("wrote 13-wordart-style-warp.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 14 — floating image with text wrap (Square + Tight)
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("14 · Floating image — square wrap and tight wrap"));
    doc.Blocks.Add(Body("Two floating images with text flowing around them. First uses Square wrap, second Tight."));

    // Square-wrapped floating image inline anchor in first body paragraph
    var floatSquare = new InlineImage(gradientPng, 108, 81)
    {
        Wrapping           = ImageWrapping.Square,
        HorizontalOffsetPt = 30,
        VerticalOffsetPt   = 30,
        HorizontalAnchor   = HorizontalAnchor.Column,
        VerticalAnchor     = VerticalAnchor.Paragraph,
        ZOrderIndex        = 10,
        AltText            = "Float Square",
    };

    var body1 = new Paragraph();
    body1.Runs.Add(Run.FromImage(floatSquare));
    body1.Runs.Add(new Run("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco. Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod."));
    doc.Blocks.Add(body1);

    // Tight-wrapped floating image
    var floatTight = new InlineImage(checkerPng, 108, 81)
    {
        Wrapping           = ImageWrapping.Tight,
        HorizontalOffsetPt = 260,
        VerticalOffsetPt   = 30,
        HorizontalAnchor   = HorizontalAnchor.Column,
        VerticalAnchor     = VerticalAnchor.Paragraph,
        ZOrderIndex        = 11,
        AltText            = "Float Tight",
    };

    var body2 = new Paragraph();
    body2.Runs.Add(Run.FromImage(floatTight));
    body2.Runs.Add(new Run("Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum. More text to demonstrate the tight wrap flowing around the checker image."));
    doc.Blocks.Add(body2);

    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "14-floating-wrap.docx"));
    Console.WriteLine("wrote 14-floating-wrap.docx");
}

// ────────────────────────────────────────────────────────────────────────────────────────────────
// FILE 15 — overlapping floating shapes with z-order
// ────────────────────────────────────────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();
    doc.Blocks.Add(Heading("15 · Floating shapes — z-order overlap"));
    doc.Blocks.Add(Body("Three shapes overlap. Bottom: blue rect (z=1), Middle: orange ellipse (z=2, should be above blue), Top: green rect (z=3, should be above both)."));

    var anchorPara = new Paragraph();

    // Blue rectangle — lowest (z=1)
    var shapeBlue = new Shape
    {
        Kind            = ShapeKind.Rectangle,
        WidthPt         = 120,
        HeightPt        = 90,
        FillColorHex    = "#4472C4",
        OutlineColorHex = "#1F3864",
        OutlineWidthPt  = 2.0,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 40,
            VerticalOffsetPt   = 50,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = 1,
        }
    };
    anchorPara.Runs.Add(Run.FromShape(shapeBlue));

    // Orange ellipse — middle (z=2)
    var shapeOrange = new Shape
    {
        Kind            = ShapeKind.Ellipse,
        WidthPt         = 120,
        HeightPt        = 90,
        FillColorHex    = "#ED7D31",
        OutlineColorHex = "#843C0C",
        OutlineWidthPt  = 1.5,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 100,
            VerticalOffsetPt   = 80,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = 2,
        }
    };
    anchorPara.Runs.Add(Run.FromShape(shapeOrange));

    // Green rounded rect — topmost (z=3)
    var shapeGreen = new Shape
    {
        Kind            = ShapeKind.RoundedRectangle,
        WidthPt         = 120,
        HeightPt        = 70,
        FillColorHex    = "#70AD47",
        OutlineColorHex = "#375623",
        OutlineWidthPt  = 1.5,
        Placement       = new FloatingPlacement
        {
            Wrapping           = ImageWrapping.InFront,
            HorizontalOffsetPt = 70,
            VerticalOffsetPt   = 115,
            HorizontalAnchor   = HorizontalAnchor.Column,
            VerticalAnchor     = VerticalAnchor.Paragraph,
            ZOrderIndex        = 3,
        }
    };
    anchorPara.Runs.Add(Run.FromShape(shapeGreen));

    // Filler text so the anchoring paragraph has content
    anchorPara.Runs.Add(new Run("Text paragraph anchoring the three overlapping floating shapes. The shapes should overlap with z-order: blue below orange below green."));
    doc.Blocks.Add(anchorPara);

    // Extra blank paragraph so the floating shapes have room to display
    doc.Blocks.Add(new Paragraph(""));
    doc.Blocks.Add(new Paragraph(""));
    doc.Blocks.Add(Body("End of file."));
    DocxWriter.Write(doc, Path.Combine(outDir, "15-floating-zorder.docx"));
    Console.WriteLine("wrote 15-floating-zorder.docx");
}

Console.WriteLine($"\nDone — {15} files in {outDir}");
return 0;

// ── PNG generators ───────────────────────────────────────────────────────────────────────────────

static byte[] MakeGradientPng(int width, int height)
{
    // Simple gradient: left=red, right=blue, top=brighter, bottom=darker
    var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            double tx = (double)x / (width - 1);
            double ty = (double)y / (height - 1);
            byte r = (byte)(255 * (1 - tx) * (1 - ty * 0.3));
            byte g = (byte)(80  * tx * (1 - ty * 0.3));
            byte b = (byte)(255 * tx * (1 - ty * 0.3));
            byte a = 255;
            int idx = (y * width + x) * 4;
            pixels[idx + 0] = b;  // Pbgra32: B G R A
            pixels[idx + 1] = g;
            pixels[idx + 2] = r;
            pixels[idx + 3] = a;
        }
    }
    bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return EncodePng(bmp);
}

static byte[] MakeCheckerPng(int width, int height)
{
    // 10×10 checker: dark-blue and light-yellow squares
    var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
    var pixels = new byte[width * height * 4];
    int cell = 10;
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            bool dark = ((x / cell) + (y / cell)) % 2 == 0;
            byte r = dark ? (byte)0x1F : (byte)0xFF;
            byte g = dark ? (byte)0x4E : (byte)0xC0;
            byte b_c = dark ? (byte)0x79 : (byte)0x00;
            int idx = (y * width + x) * 4;
            pixels[idx + 0] = b_c;
            pixels[idx + 1] = g;
            pixels[idx + 2] = r;
            pixels[idx + 3] = 255;
        }
    }
    bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    return EncodePng(bmp);
}

static byte[] EncodePng(BitmapSource bmp)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}
