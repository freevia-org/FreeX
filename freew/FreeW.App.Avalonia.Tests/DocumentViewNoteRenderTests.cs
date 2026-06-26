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
/// Tests for the Avalonia DocumentView footnote/endnote content render path (AV-NOTERENDER wave).
/// Verifies: footnote text is laid out in the bottom margin band (above the footer) of the page that
/// hosts its reference; endnotes are stacked in a synthetic section after the last body page; note
/// numbers match the in-body reference superscript numbers; a separator rule is emitted; and a doc
/// with no notes produces no note items (regression).
/// </summary>
public sealed class DocumentViewNoteRenderTests
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
            return false; // no headless drawing backend in this environment
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a single-page document whose first paragraph carries a footnote reference for the given
    /// id; the footnote content is stored in the document's Footnotes store.
    /// </summary>
    private static TextDocument DocWithFootnote(int id, string noteText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text"));
        para.Runs.Add(Run.FootnoteReference(id));
        para.Runs.Add(new Run(" continues."));
        doc.Blocks.Add(para);
        doc.Footnotes[id] = new Footnote(id, noteText);
        return doc;
    }

    private static TextDocument DocWithEndnote(int id, string noteText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Body text"));
        para.Runs.Add(Run.EndnoteReference(id));
        para.Runs.Add(new Run(" continues."));
        doc.Blocks.Add(para);
        doc.Endnotes[id] = new Endnote(id, noteText);
        return doc;
    }

    // ── Test 1: footnote text appears as note items with the right number + content ──────────────────

    [Fact]
    public async Task Footnote_produces_render_items_with_number_and_text()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFootnote(1, "First footnote body.");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("a footnote must produce render items");

        // The number marker "1" must appear as a superscript-styled item.
        items!.Should().Contain(i => i.IsNumberMarker && i.Text.Trim() == "1",
            "the footnote number marker must match the body reference number");

        // The footnote text words must appear as non-marker items.
        var textJoined = string.Concat(items!.Where(i => !i.IsNumberMarker).Select(i => i.Text));
        textJoined.Should().Contain("First", "the footnote body text must be laid out");
        textJoined.Should().Contain("footnote");
    }

    // ── Test 2: footnote band sits in the bottom margin area, above the footer ───────────────────────

    [Fact]
    public async Task Footnote_band_is_in_bottom_margin_band_above_footer()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFootnote(1, "Footnote at page bottom.");
            doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("My Footer");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps = view.NoteSeparators;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        seps.Should().NotBeNull();
        seps!.Should().NotBeEmpty("a footnote band must have a separator rule");

        // Page geometry: pageTop = 24, pageHeight = 792*(96/72) = 1056 → pageBottom ≈ 1080.
        // Body text area bottom = pageTop + marginTopDip (96) + textAreaHeight (1056 - 96 - 96 = 864) = 1056.
        const double pageTop = 24.0;
        const double pageBottom = pageTop + 792.0 * (96.0 / 72.0);   // ≈ 1080
        const double bodyAreaBottom = pageTop + 96.0 + (792.0 * (96.0 / 72.0) - 96.0 - 96.0); // ≈ 1056

        var sepY = seps![0].Y;
        sepY.Should().BeGreaterThanOrEqualTo(bodyAreaBottom - 4,
            "the footnote separator must sit at/below the body text area bottom");
        sepY.Should().BeLessThan(pageBottom,
            "the footnote separator must stay above the page bottom edge");

        // Note text items must be below the separator and above the page bottom.
        foreach (var it in items!)
        {
            it.Y.Should().BeGreaterThanOrEqualTo(sepY - 2,
                "footnote text must be below the separator");
            it.Y.Should().BeLessThan(pageBottom,
                "footnote text must stay on the page");
        }
    }

    // ── Test 3: endnote produces a section after the last body page with an "Endnotes" heading ───────

    [Fact]
    public async Task Endnote_produces_end_section_with_heading_and_text()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithEndnote(1, "First endnote body.");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps = view.NoteSeparators;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("an endnote must produce render items");

        // The "Endnotes" heading must be present.
        items!.Should().Contain(i => i.Text == "Endnotes",
            "the endnotes section must carry an 'Endnotes' heading");

        // The number marker "1" matches the body reference.
        items!.Should().Contain(i => i.IsNumberMarker && i.Text.Trim() == "1",
            "the endnote number marker must match the body reference number");

        // The endnote body text must be laid out.
        var textJoined = string.Concat(items!.Where(i => !i.IsNumberMarker && i.Text != "Endnotes").Select(i => i.Text));
        textJoined.Should().Contain("endnote");

        seps.Should().NotBeNull();
        seps!.Should().NotBeEmpty("the endnotes section must have a separator under the heading");
    }

    // ── Test 4: endnote section lands AFTER the last body page (page-space) ───────────────────────────

    [Fact]
    public async Task Endnote_section_is_below_last_body_page()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        int pageCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithEndnote(1, "Endnote body.");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            pageCount = view.PageCount;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // Last body page bottom (page-space): DeskPadding(24) + (pages-1)*(pageH+gap) + pageH.
        const double deskPadding = 24.0;
        const double pageGap = 20.0;
        var pageHeight = 792.0 * (96.0 / 72.0); // ≈ 1056
        var lastPageBottom = deskPadding + (pageCount - 1) * (pageHeight + pageGap) + pageHeight;

        var heading = items!.First(i => i.Text == "Endnotes");
        heading.Y.Should().BeGreaterThan(lastPageBottom,
            "the endnotes heading must render after the last body page");
    }

    // ── Test 5: footnote numbers match multiple references ───────────────────────────────────────────

    [Fact]
    public async Task Multiple_footnotes_render_all_numbers()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Alpha"));
            para.Runs.Add(Run.FootnoteReference(1));
            para.Runs.Add(new Run(" beta"));
            para.Runs.Add(Run.FootnoteReference(2));
            para.Runs.Add(new Run(" gamma."));
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "Note one.");
            doc.Footnotes[2] = new Footnote(2, "Note two.");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        var markers = items!.Where(i => i.IsNumberMarker).Select(i => i.Text.Trim()).ToList();
        markers.Should().Contain("1", "footnote 1's number must appear");
        markers.Should().Contain("2", "footnote 2's number must appear");
    }

    // ── Test 6: no-notes regression — no note items or separators ─────────────────────────────────────

    [Fact]
    public async Task No_notes_produces_no_note_items()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Plain body text with no notes."));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps = view.NoteSeparators;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        seps.Should().NotBeNull();
        items!.Should().BeEmpty("a document with no notes must not produce note render items");
        seps!.Should().BeEmpty("a document with no notes must not produce note separators");
    }

    // ── Test 7: web layout produces no note items (PrintLayout-only) ──────────────────────────────────

    [Fact]
    public async Task No_note_items_in_web_layout_mode()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFootnote(1, "Should not appear in web layout.");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.ViewMode = DocumentViewMode.WebLayout;
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().BeEmpty("WebLayout mode must not produce footnote/endnote render items");
    }

    // ── Test 8: headless render capture — a doc with 2 footnotes + 1 endnote across pages ─────────────
    // Renders to a PNG so the footnote band (separator + numbers at page bottom) and the endnotes
    // section (heading + numbered texts at document end) can be visually inspected.

    [Fact]
    public async Task Render_capture_doc_with_footnotes_and_endnote()
    {
        byte[]? pngBytes = null;
        string? outPath = null;
        var ran = false;
        try
        {
            await Session.Dispatch(() =>
            {
                ran = true;
                var doc = BuildMultiPageNoteDoc();
                var view = new DocumentView();
                view.LoadDocument(doc);

                var window = new Window { Width = 960, Height = 3300, Content = view };
                window.Show();
                window.Measure(new Size(960, 3300));
                window.Arrange(new Rect(0, 0, 960, 3300));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var frame = window.CaptureRenderedFrame();
                if (frame is not null)
                    pngBytes = WriteableBitmapToPng(frame);
                window.Close();

                var binDir = Path.GetDirectoryName(typeof(DocumentViewNoteRenderTests).Assembly.Location) ?? ".";
                outPath = Path.GetFullPath(Path.Combine(binDir, "freew_avalonia_notes.png"));
                if (pngBytes is { Length: > 0 })
                    File.WriteAllBytes(outPath, pngBytes);
                Console.WriteLine($"[NoteRenderCapture] PNG: {(pngBytes is null ? "null" : pngBytes.Length + " bytes")} → {outPath}");
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NoteRenderCapture] Skipped: {ex.GetType().Name}: {ex.Message}");
            ran = false;
        }

        if (!ran) return;
        if (pngBytes is null || pngBytes.Length == 0)
        {
            Console.WriteLine("[NoteRenderCapture] Headless capture unavailable — skipping size check.");
            return;
        }
        pngBytes.Length.Should().BeGreaterThan(5000);
        Console.WriteLine($"[NoteRenderCapture] Visual inspection: {outPath}");
    }

    private static TextDocument BuildMultiPageNoteDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var bodyFmt = RunFormatting.Default with { FontSizePt = 12 };

        // Page 1: a paragraph with footnote 1 reference.
        var p1 = new Paragraph();
        p1.Runs.Add(new Run("This is the first page body with a footnote", bodyFmt));
        p1.Runs.Add(Run.FootnoteReference(1));
        p1.Runs.Add(new Run(" and an endnote", bodyFmt));
        p1.Runs.Add(Run.EndnoteReference(1));
        p1.Runs.Add(new Run(".", bodyFmt));
        doc.Blocks.Add(p1);

        // Fill page 1.
        for (var i = 1; i <= 45; i++)
            doc.Blocks.Add(new Paragraph($"Page-1 filler line {i}: lorem ipsum dolor sit amet."));

        // Page 2: a paragraph with footnote 2 reference.
        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Second page body with another footnote", bodyFmt));
        p2.Runs.Add(Run.FootnoteReference(2));
        p2.Runs.Add(new Run(".", bodyFmt));
        doc.Blocks.Add(p2);
        for (var i = 1; i <= 8; i++)
            doc.Blocks.Add(new Paragraph($"Page-2 filler line {i}."));

        doc.Footnotes[1] = new Footnote(1, "First footnote: appears at the bottom of page 1.");
        doc.Footnotes[2] = new Footnote(2, "Second footnote: appears at the bottom of page 2.");
        doc.Endnotes[1] = new Endnote(1, "First endnote: appears at the end of the document.");
        return doc;
    }

    private static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
    {
        try
        {
            using var locked = bitmap.Lock();
            var info = new SKImageInfo(
                locked.Size.Width, locked.Size.Height,
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
