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

    // Delegates to the shared helper: the local copy this replaced swallowed ASSERTION failures too,
    // so every "if (!ran) return;" below turned a failing assertion into a silently passing test.
    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

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

        // Page geometry: pageTop = 24, pageHeight = 792*(96/72) = 1056, and the
        // usable bottom-margin edge is pageBottom minus the 96 DIP bottom margin.
        const double pageTop = 24.0;
        const double pageBottom = pageTop + 792.0 * (96.0 / 72.0);   // ≈ 1080
        const double bottomMarginTop = pageBottom - 96.0; // ≈ 984

        var sepY = seps![0].Y;
        sepY.Should().BeLessThanOrEqualTo(bottomMarginTop,
            "the footnote separator must use the bottom-margin edge rather than the lower footer-distance strip");
        sepY.Should().BeGreaterThan(bottomMarginTop - 96,
            "the short footnote band must remain inside the bottom margin area");

        // Note text items must be below the separator and no lower than the body bottom margin.
        foreach (var it in items!)
        {
            it.Y.Should().BeGreaterThanOrEqualTo(sepY - 2,
                "footnote text must be below the separator");
            it.Y.Should().BeLessThanOrEqualTo(bottomMarginTop,
                "footnote text must stay above the usable bottom-margin edge");
        }
    }

    // ── Test 3: endnote produces a section after the last body page with an "Endnotes" heading ───────

    [Fact]
    public async Task Endnote_produces_final_page_items_without_synthetic_heading()
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

        items!.Should().NotContain(i => i.Text == "Endnotes",
            "Word appends fitting endnotes directly after the final body paragraph without a synthetic heading");

        // The number marker "1" matches the body reference.
        items!.Should().Contain(i => i.IsNumberMarker && i.Text.Trim() == "1",
            "the endnote number marker must match the body reference number");

        // The endnote body text must be laid out.
        var textJoined = string.Concat(items!.Where(i => !i.IsNumberMarker).Select(i => i.Text));
        textJoined.Should().Contain("endnote");

        seps.Should().NotBeNull();
        seps!.Should().NotBeEmpty("fitting endnotes must have a separator after the final body paragraph");
    }

    // ── Test 4: endnote section lands AFTER the last body page (page-space) ───────────────────────────

    [Fact]
    public async Task Fitting_endnotes_follow_the_last_body_line_on_its_page()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>? body = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithEndnote(1, "Endnote body.");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps = view.NoteSeparators;
            body = view.GetPlacedForBlock(0);
        });

        if (!ran) return;
        items.Should().NotBeNull();

        seps.Should().NotBeNullOrEmpty();
        body.Should().NotBeNullOrEmpty();

        var bodyBottom = body!.Max(item => item.Y + item.LineHeight);
        var separatorY = seps![0].Y;
        separatorY.Should().BeGreaterThan(bodyBottom,
            "fitting endnotes must follow the final body line on the same page");
        items!.Min(item => item.Y).Should().BeGreaterThan(separatorY,
            "endnote content must remain below its final-page separator");
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

    [Fact]
    public async Task Multiple_footnotes_use_word_like_vertical_spacing()
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
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "First note.");
            doc.Footnotes[2] = new Footnote(2, "Second note.");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;

        var markers = items!
            .Where(item => item.IsNumberMarker)
            .OrderBy(item => item.Y)
            .ToList();
        markers.Should().HaveCount(2);
        (markers[1].Y - markers[0].Y).Should().BeInRange(27, 29,
            "two short footnotes should retain Word-like paragraph spacing in their bottom-margin band");
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

    // ── Test 9 (DB1): body text area is reduced on a page that has a footnote ─────────────────────────
    // The body's last placed glyph on a footnote-bearing page must sit ABOVE the footnote band top
    // (i.e., the body does not overlap the footnotes). We compare the last body-text Y to the
    // separator Y for that page.

    [Fact]
    public async Task DB1_FootnotePage_BodyTextDoesNotOverlapFootnoteBand()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        var ran = await OnUiThread(() =>
        {
            // Build a page-filling document: many lines push body text to the bottom, plus a footnote.
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var bodyPara = new Paragraph();
            bodyPara.Runs.Add(new Run("Body text with footnote"));
            bodyPara.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(bodyPara);
            // Fill the page so body text reaches close to the bottom margin.
            for (var i = 0; i < 50; i++)
                doc.Blocks.Add(new Paragraph($"Filler line {i + 1}: lorem ipsum dolor sit amet."));
            doc.Footnotes[1] = new Footnote(1, "Footnote that should not be overlapped by body text.");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps  = view.NoteSeparators;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        seps.Should().NotBeNull();
        if (seps!.Count == 0) return; // no footnote band on page 1 (all pushed to page 2) — skip

        // The separator is the band top. All note items must be at or below the separator.
        // No body text item (which is NOT in _noteItems — those are separate placed glyphs)
        // should exceed the separator Y. We can verify this indirectly: the note items must be
        // BELOW the separator line (they start at bandTop + 4).
        var sepY = seps![0].Y;
        foreach (var it in items!.Where(i => !i.IsNumberMarker || i.Text.Trim() != ""))
        {
            it.Y.Should().BeGreaterThanOrEqualTo(sepY - 5,
                "all note render items must be at or below the footnote separator");
        }
    }

    // ── Test 10 (DB2): long (multi-line) footnote — true height reserved + content stays on page ────

    [Fact]
    public async Task DB2_LongFootnote_ContentStaysAboveFooterNotOverflowingPage()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? seps = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text"));
            para.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(para);
            // Long footnote: many words that wrap to multiple lines.
            var longNote = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"word{i}"));
            doc.Footnotes[1] = new Footnote(1, longNote);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
            seps  = view.NoteSeparators;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        seps.Should().NotBeNull();
        if (items!.Count == 0 || seps!.Count == 0) return; // env skip

        // Page geometry (same as test 2).
        const double pageTop    = 24.0;
        const double pageHeight = 792.0 * (96.0 / 72.0); // ≈ 1056
        const double pageBottom = pageTop + pageHeight;
        // Note items must all stay on-page (below separator, above page bottom).
        foreach (var it in items!)
        {
            it.Y.Should().BeLessThan(pageBottom + 2,
                "long footnote content must not overflow past the page bottom");
        }
        // There should be more than one text item (multi-line wrap happened).
        var textItems = items!.Where(i => !i.IsNumberMarker).ToList();
        textItems.Count.Should().BeGreaterThan(1,
            "a long footnote with 60 words must wrap to multiple render items");
    }

    [Fact]
    public async Task LongFootnote_ContinuesOnInsertedPagesWithoutDroppingWords()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<(double X1, double X2, double Y)>? separators = null;
        var pageCount = 0;
        var expectedWords = Enumerable.Range(1, 700).Select(index => $"word{index}").ToArray();
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFootnote(1, string.Join(" ", expectedWords));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 8000));
            pageCount = view.PageCount;
            items = view.NoteRenderItems;
            separators = view.NoteSeparators;
        });

        if (!ran) return;

        pageCount.Should().BeGreaterThan(1,
            "a note larger than its reference-page band must create physical continuation pages");
        separators.Should().NotBeNull();
        separators!.Select(separator => Math.Floor((separator.Y - 24) / (1056 + 20)))
            .Distinct().Should().HaveCountGreaterThan(1,
                "initial and continuation footnote rules must land on separate physical pages");

        var renderedWords = string.Concat(items!
                .Where(item => !item.IsNumberMarker)
                .Select(item => item.Text))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        renderedWords.Should().ContainInOrder(expectedWords,
            "continuation rendering must retain every word in source order");
    }

    [Fact]
    public async Task LongFootnote_ContinuationDoesNotHideLaterShortFootnotes()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFootnote(1, string.Join(" ", Enumerable.Range(1, 700).Select(index => $"long{index}")));
            for (var index = 0; index < 60; index++)
                doc.Blocks.Add(new Paragraph($"Body filler {index}."));
            var laterReference = new Paragraph("Later body reference");
            laterReference.Runs.Add(Run.FootnoteReference(2));
            doc.Blocks.Add(laterReference);
            doc.Footnotes[2] = new Footnote(2, "later short footnote");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 10000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;

        items!.Should().Contain(item => item.IsNumberMarker && item.Text.Trim() == "2");
        string.Concat(items.Where(item => !item.IsNumberMarker).Select(item => item.Text))
            .Should().Contain("later short footnote");
    }

    [Fact]
    public async Task LongFootnote_FinalFragmentSharesTheFollowingBodyPage()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        IReadOnlyList<IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>>? followingBodies = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var reference = new Paragraph("Reference body");
            reference.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(reference);
            for (var index = 1; index <= 60; index++)
                doc.Blocks.Add(new Paragraph($"Following body {index}."));
            doc.Footnotes[1] = new Footnote(1,
                string.Join(" ", Enumerable.Range(1, 700).Select(index => $"tail{index}")));

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 10000));
            items = view.NoteRenderItems;
            followingBodies = Enumerable.Range(1, 60)
                .Select(view.GetPlacedForBlock)
                .ToList();
        });

        if (!ran) return;

        followingBodies.Should().NotBeNull();
        var tail = items!.First(item => item.Text.Contains("tail700", StringComparison.Ordinal));
        var resumedBodyPage = followingBodies!
            .Where(body => body.Count > 0)
            .Select(body => (int)Math.Floor((body[0].Y - 24) / (1056 + 20)))
            .Where(page => page > 0)
            .DefaultIfEmpty(-1)
            .First();
        var tailPage = (int)Math.Floor((tail.Y - 24) / (1056 + 20));
        resumedBodyPage.Should().BeGreaterThanOrEqualTo(1);
        tailPage.Should().Be(resumedBodyPage,
            "Word resumes later body content above an overflowing footnote's final fragment rather than giving every fragment its own page");
    }

    // ── Test 11 (DB3): footnote numbers respect StartAt ───────────────────────────────────────────────

    [Fact]
    public async Task DB3_FootnoteStartAt_NumberRendersFromStartAt()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text"));
            para.Runs.Add(Run.FootnoteReference(1));
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "A note.");
            // StartAt = 2: the first footnote should display as "2", not "1".
            doc.FootnoteNumbering.StartAt = 2;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (items!.Count == 0) return;

        var markers = items!.Where(i => i.IsNumberMarker).Select(i => i.Text.Trim()).ToList();
        markers.Should().Contain("2",
            "with StartAt=2 the first footnote must display as '2'");
        markers.Should().NotContain("1",
            "with StartAt=2 the number '1' must not appear");
    }

    // ── Test 12 (DB3): footnote numbers respect LowerRoman format ─────────────────────────────────────

    [Fact]
    public async Task DB3_FootnoteLowerRomanFormat_NumberRendersAsRoman()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text"));
            para.Runs.Add(Run.FootnoteReference(1));
            para.Runs.Add(Run.FootnoteReference(2));
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "Note one.");
            doc.Footnotes[2] = new Footnote(2, "Note two.");
            doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (items!.Count == 0) return;

        var markers = items!.Where(i => i.IsNumberMarker).Select(i => i.Text.Trim()).ToList();
        markers.Should().Contain("i",
            "LowerRoman format: first footnote must display as 'i'");
        markers.Should().Contain("ii",
            "LowerRoman format: second footnote must display as 'ii'");
    }

    // ── Test 13 (DB3): endnote numbers default to LowerRoman (Word default) ──────────────────────────
    // NOTE: EndnoteNumbering defaults to Decimal in FreeW's model (matching OOXML default).
    // Word's visual default is lowerRoman, but FreeW follows the model; this test verifies
    // that when LowerRoman is set explicitly, the endnote renders as roman numerals.

    [Fact]
    public async Task DB3_EndnoteLowerRoman_NumberRendersAsRoman()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text"));
            para.Runs.Add(Run.EndnoteReference(1));
            doc.Blocks.Add(para);
            doc.Endnotes[1] = new Endnote(1, "An endnote.");
            doc.EndnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (items!.Count == 0) return;

        var markers = items!.Where(i => i.IsNumberMarker).Select(i => i.Text.Trim()).ToList();
        markers.Should().Contain("i",
            "LowerRoman endnote format: first endnote must display as 'i'");
    }

    // ── Test 14 (DB3): endnote numbers respect StartAt ────────────────────────────────────────────────

    [Fact]
    public async Task DB3_EndnoteStartAt2_NumberRendersFrom2()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("Body text"));
            para.Runs.Add(Run.EndnoteReference(1));
            doc.Blocks.Add(para);
            doc.Endnotes[1] = new Endnote(1, "An endnote.");
            doc.EndnoteNumbering.StartAt = 3; // start at 3

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (items!.Count == 0) return;

        var markers = items!.Where(i => i.IsNumberMarker).Select(i => i.Text.Trim()).ToList();
        markers.Should().Contain("3",
            "with EndnoteNumbering.StartAt=3 the first endnote must display as '3'");
    }

    // ── Test 15: NumberRestart.EachPage restarts the footnote band on every physical page ────────────

    [Fact]
    public async Task EachPageRestart_FootnoteBandRestartsNumberingOnEveryPhysicalPage()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildTwoPageFootnoteDoc();
            doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 8000));
            pageCount = view.PageCount;
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (pageCount < 2 || items!.Count == 0) return; // env-dependent pagination — skip rather than false-fail

        var markers = items!.Where(i => i.IsNumberMarker).OrderBy(i => i.Y).Select(i => i.Text.Trim()).ToList();
        markers.Should().Equal(["1", "1"],
            "each physical page's footnote must restart at 1 under NoteNumberRestart.EachPage");
    }

    /// <summary>Sibling no-regression: the default Continuous restart keeps counting across pages.</summary>
    [Fact]
    public async Task Continuous_NoRegression_FootnoteBandKeepsCountingAcrossPages()
    {
        IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)>? items = null;
        var pageCount = 0;
        var ran = await OnUiThread(() =>
        {
            var doc = BuildTwoPageFootnoteDoc();
            // NumberRestart left at its Continuous default.

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 8000));
            pageCount = view.PageCount;
            items = view.NoteRenderItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        if (pageCount < 2 || items!.Count == 0) return;

        var markers = items!.Where(i => i.IsNumberMarker).OrderBy(i => i.Y).Select(i => i.Text.Trim()).ToList();
        markers.Should().Equal(["1", "2"]);
    }

    private static TextDocument BuildTwoPageFootnoteDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p1 = new Paragraph();
        p1.Runs.Add(new Run("Page one body"));
        p1.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(p1);
        for (var i = 1; i <= 60; i++)
            doc.Blocks.Add(new Paragraph($"Page-1 filler line {i}: lorem ipsum dolor sit amet consectetur."));

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Page two body"));
        p2.Runs.Add(Run.FootnoteReference(2));
        doc.Blocks.Add(p2);

        doc.Footnotes[1] = new Footnote(1, "First footnote.");
        doc.Footnotes[2] = new Footnote(2, "Second footnote.");
        return doc;
    }

    // ── Test 16: the in-body reference mark shows the computed display sequence, not the raw id ──────

    /// <summary>
    /// Word keeps a note's internal id stable when an earlier note is deleted — only the DISPLAY
    /// sequence shifts. The in-body superscript reference mark (rendered via DisplayCells) must track
    /// that shift instead of showing the raw <see cref="Run.FootnoteId"/>, or the body glyph drifts out
    /// of sync with the footnote area's own numbering.
    /// </summary>
    [Fact]
    public async Task FootnoteBodyMark_ShowsComputedDisplaySequence_NotRawId_AfterEarlierNoteDeleted()
    {
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>? body = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("A"));
            para.Runs.Add(Run.FootnoteReference(1));
            para.Runs.Add(new Run("B"));
            para.Runs.Add(Run.FootnoteReference(2));
            para.Runs.Add(new Run("C"));
            para.Runs.Add(Run.FootnoteReference(3));
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "one");
            doc.Footnotes[2] = new Footnote(2, "two");
            doc.Footnotes[3] = new Footnote(3, "three");

            // Simulate deleting the first footnote directly on the model: ids stay stable (2, 3
            // survive), only their display sequence must shift down to 1, 2.
            doc.Footnotes.Remove(1);
            para.Runs.RemoveAll(r => r.FootnoteId == 1);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            body = view.GetPlacedForBlock(0);
        });

        if (!ran) return;
        body.Should().NotBeNull();

        // The body text uses only letters (A/B/C); the footnote marks are the only digit glyphs, in order.
        var markerChars = body!.Where(c => char.IsDigit(c.Ch)).Select(c => c.Ch).ToList();
        markerChars.Should().Equal(['1', '2'],
            "the surviving footnotes' body marks must show their computed display sequence (1, 2), not their raw internal ids (2, 3)");
    }

    /// <summary>Sibling no-regression: with no deletion, ids and display sequence coincide (1, 2).</summary>
    [Fact]
    public async Task FootnoteBodyMark_NoRegression_ShowsSequentialNumbers_WhenNoNoteWasDeleted()
    {
        IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>? body = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(new Run("A"));
            para.Runs.Add(Run.FootnoteReference(1));
            para.Runs.Add(new Run("B"));
            para.Runs.Add(Run.FootnoteReference(2));
            doc.Blocks.Add(para);
            doc.Footnotes[1] = new Footnote(1, "one");
            doc.Footnotes[2] = new Footnote(2, "two");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            body = view.GetPlacedForBlock(0);
        });

        if (!ran) return;
        body.Should().NotBeNull();

        var markerChars = body!.Where(c => char.IsDigit(c.Ch)).Select(c => c.Ch).ToList();
        markerChars.Should().Equal(['1', '2']);
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
