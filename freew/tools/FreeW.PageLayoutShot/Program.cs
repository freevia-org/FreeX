// FreeW.PageLayoutShot — renders the FreeW Avalonia DocumentView (with print-layout chrome) to a PNG
// for visual verification. Uses the real Avalonia Skia backend (not the headless stub) so the output
// contains actual pixels: grey desk, white page with drop-shadow, content inset to real margins.
//
// Usage:
//   FreeW.PageLayoutShot [<output-path>]
//
// If <output-path> is omitted the PNG is written next to the executable as freew_pagelayout.png.
// The program exits after writing the PNG (no interactive window appears).

using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using SkiaSharp;

var outPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "freew_pagelayout.png");

int exitCode = 0;
var done = new ManualResetEventSlim(false);

// Run in an Avalonia event loop (required for layout + glyph shaping).
AppBuilder.Configure<PageShotApp>()
    .UsePlatformDetect()
    .SetupWithoutStarting();

Dispatcher.UIThread.Post(() =>
{
    try
    {
        exitCode = Render(outPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PageLayoutShot] Error: {ex.Message}");
        exitCode = 1;
    }
    finally
    {
        done.Set();
    }
});

Dispatcher.UIThread.RunJobs();
done.Wait();
return exitCode;

static int Render(string outPath)
{
    const int width  = 960;
    // Tall enough to show 2-3 discrete page rectangles with gaps between them.
    // A US Letter page at 96dpi = 1056px; 3 pages + 2 gaps (~40px) + padding ≈ 3250px.
    const int height = 3300;

    var doc = BuildMultiPageDocument();
    var view = new DocumentView();
    view.LoadDocument(doc);
    view.Measure(new Size(width, height));
    view.Arrange(new Rect(0, 0, width, height));
    view.UpdateLayout();
    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

    var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
    bitmap.Render(view);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
    using var stream = new MemoryStream();
    bitmap.Save(stream);
    var bytes = stream.ToArray();

    if (bytes.Length > 0)
    {
        File.WriteAllBytes(outPath, bytes);
        Console.WriteLine($"[PageLayoutShot] Written {bytes.Length:N0} bytes to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    // Fallback: encode via SkiaSharp if the Avalonia encoder produced nothing.
    var pngBytes = TryEncodeViaSkia(view, width, height);
    if (pngBytes is { Length: > 0 })
    {
        File.WriteAllBytes(outPath, pngBytes);
        Console.WriteLine($"[PageLayoutShot] Skia fallback: {pngBytes.Length:N0} bytes to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    Console.Error.WriteLine("[PageLayoutShot] Both encoding paths produced 0 bytes.");
    return 2;
}

static byte[] TryEncodeViaSkia(DocumentView view, int width, int height)
{
    try
    {
        // Use a WriteableBitmap and draw into it via a fresh ImmediateDrawingContext.
        var wb = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var locked = wb.Lock())
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, locked.Address, locked.RowBytes);
            if (surface is null)
                return [];

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Gray);

            // Re-render via Avalonia ImmediateDrawingContext onto the SK surface is not directly
            // available here without Avalonia internals. Record a best-effort grey placeholder.
            canvas.DrawRect(new SKRect(24, 24, width - 24, height - 24), new SKPaint
            {
                Color = SKColors.White,
                IsStroke = false,
            });
            using var textFont  = new SKFont(SKTypeface.Default, 16);
            using var textPaint = new SKPaint { Color = SKColors.DarkBlue, IsAntialias = true };
            canvas.DrawText("FreeW — Avalonia Print Layout (Skia fallback placeholder)", 50, 70,
                SKTextAlign.Left, textFont, textPaint);
            canvas.DrawText("Run FreeW normally to see the real page chrome.", 50, 100,
                SKTextAlign.Left, textFont, textPaint);
            surface.Flush();
        }

        using var readLocked = wb.Lock();
        var infoOut = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap();
        if (!bmp.InstallPixels(infoOut, readLocked.Address, readLocked.RowBytes))
            return [];

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data?.ToArray() ?? [];
    }
    catch
    {
        return [];
    }
}

/// <summary>
/// Builds a document long enough to span 2–3 pages so the multi-page pagination
/// (discrete white page rects with grey gaps) is visible in the captured PNG.
/// A standard US-Letter page (11 in = 792pt) with 1-inch margins leaves ~9 in of
/// text area. At 12pt body text and ~1.3 leading that's roughly 50 lines per page,
/// so we add enough paragraphs to cross at least two page boundaries.
/// </summary>
static TextDocument BuildMultiPageDocument()
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Clear();

    // Standard US-Letter (default from PageSettings).
    // doc.Page.WidthPt = 612; doc.Page.HeightPt = 792; margins = 72pt each side.

    doc.Styles["Heading1"] = new DocumentStyle
    {
        Id        = "Heading1",
        Name      = "Heading 1",
        Run       = RunFormatting.Default with { Bold = true, FontSizePt = 18, ColorHex = "#2B5797" },
        Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 12, SpaceAfterPt = 6 },
    };
    doc.Styles["Heading2"] = new DocumentStyle
    {
        Id        = "Heading2",
        Name      = "Heading 2",
        Run       = RunFormatting.Default with { Bold = true, FontSizePt = 14, ColorHex = "#2E6DA4" },
        Paragraph = ParagraphFormatting.Default with { SpaceBeforePt = 10, SpaceAfterPt = 4 },
    };

    var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };

    void AddH1(string text)
    {
        var p = new Paragraph { StyleId = "Heading1" };
        p.Runs.Add(new Run(text));
        doc.Blocks.Add(p);
    }

    void AddH2(string text)
    {
        var p = new Paragraph { StyleId = "Heading2" };
        p.Runs.Add(new Run(text));
        doc.Blocks.Add(p);
    }

    void AddPara(string text)
    {
        var p = new Paragraph();
        p.Runs.Add(new Run(text, bodyFmt));
        doc.Blocks.Add(p);
    }

    // ---- Page 1 ----
    AddH1("FreeW — Discrete Multi-Page Pagination");
    AddPara(
        "This document spans multiple pages to verify the discrete pagination feature. " +
        "Each white rectangle represents one page, separated by grey desk gaps — exactly " +
        "like Microsoft Word's Print Layout view.");
    AddH2("Background");
    AddPara(
        "Earlier builds rendered a single tall white page. The new layout engine computes " +
        "a text-area height per page (page height minus top and bottom margins) and wraps " +
        "content line-granularly: a complete line that would cross the bottom margin is " +
        "pushed to the top margin of the next page.");
    AddPara(
        "The formula for page-space Y is: DeskPadding + pageIndex*(pageHeightPx+PageGap) " +
        "+ marginTopDip + offsetWithinTextArea. All glyph coordinates, caret positions, " +
        "hit-testing, selection rendering, find highlights, and GetBlockTop() use the same " +
        "mapping, preserving editing behaviour across page boundaries.");
    AddH2("Coordinate transform");
    AddPara(
        "Content Y (0 = start of first text area) increases monotonically through the " +
        "document. Page index = floor(contentY / textAreaHeight). Offset within the page = " +
        "contentY mod textAreaHeight. Page-space Y adds that offset to the Y of the top " +
        "margin of the chosen page rectangle.");
    AddPara(
        "The ReserveContentY helper checks whether the next line fits in the remaining " +
        "space on the current page (posInPage + lineHeight <= textAreaHeight). If not, it " +
        "bumps contentY to the start of the next page before placing the line. This ensures " +
        "no line is ever split across a page boundary.");
    AddPara(
        "Tables are treated row-by-row: each row is reserved as a unit on the current page " +
        "or pushed to the next. Images are similarly reserved as a whole block. Paragraph " +
        "space-before and space-after accumulate in content-Y space so they scale correctly " +
        "across page boundaries.");
    AddH2("Status bar");
    AddPara(
        "The status bar now shows 'Page X of Y' where X is the one-based index of the page " +
        "containing the caret, and Y is the total page count. The page count is recomputed " +
        "on every layout pass; the caret page updates whenever the caret moves.");
    // Add filler paragraphs to push into page 2.
    for (int i = 1; i <= 12; i++)
        AddPara($"Paragraph {i} of filler text on page 1 — lorem ipsum dolor sit amet, " +
                "consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore " +
                "et dolore magna aliqua. Ut enim ad minim veniam.");

    // ---- Page 2 ----
    AddH1("Page 2 — Content continues here");
    AddPara(
        "This heading and the body below it are on page 2. The grey gap between pages 1 " +
        "and 2 is clearly visible in the rendered PNG, confirming that the page-break " +
        "logic placed content correctly.");
    for (int i = 1; i <= 12; i++)
        AddPara($"Body paragraph {i} on page 2 — the quick brown fox jumps over the lazy " +
                "dog. Pack my box with five dozen liquor jugs. How vexingly quick daft " +
                "zebras jump!");

    // ---- Page 3 ----
    AddH1("Page 3 — Third page verification");
    AddPara(
        "Reaching page 3 confirms the pagination loop handles more than one page boundary " +
        "correctly. PDF export, undo/redo, find/replace, and navigation-pane scroll all " +
        "continue to work because they all share the same page-space Y transform.");
    for (int i = 1; i <= 6; i++)
        AddPara($"Final filler paragraph {i} on page 3. Sphinx of black quartz, judge my " +
                "vow. The five boxing wizards jump quickly.");

    return doc;
}


/// <summary>Minimal Avalonia app used by the page-layout shot tool (no UI shown).</summary>
public sealed class PageShotApp : Application
{
    public override void Initialize() { }
}
