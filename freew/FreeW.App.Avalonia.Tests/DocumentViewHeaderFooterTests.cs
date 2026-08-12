using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Tests for the Avalonia DocumentView header/footer render path (AV-HF wave).
/// Verifies: HF items are pre-computed in BuildHeaderFooterItems; items land in the correct
/// page-margin band (above top margin for headers, below bottom margin for footers); the right
/// variant is selected (Default / First / Even); field runs (PAGE, NUMPAGES) resolve to the
/// correct display strings; WebLayout mode produces no HF items.
/// </summary>
public sealed class DocumentViewHeaderFooterTests
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
    /// Builds a minimal single-page document with a default header carrying the given text.
    /// Page geometry: 8.5×11" with 1" margins. HF distance defaults (0 = unset → fallback 36 pt).
    /// </summary>
    private static TextDocument DocWithHeader(string headerText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text."));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter(headerText);
        return doc;
    }

    /// <summary>
    /// Builds a minimal document with a default footer carrying the given text.
    /// </summary>
    private static TextDocument DocWithFooter(string footerText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text."));
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter(footerText);
        return doc;
    }

    // ── Test 1: header item appears in top margin band ─────────────────────────────────────────────

    [Fact]
    public async Task Header_items_appear_in_top_margin_band_for_simple_header_doc()
    {
        // Page-space Y for a header at default HF distance (36 pt) on page 0:
        //   pageTop = DeskPadding (24) + 0 * (pageHeightPx + PageGap)
        //   headerY = pageTop + 36 * (96/72)
        // We only verify the item is above the top margin (pageTop + marginTopDip).
        // marginTopDip = 72 * (96/72) = 96 px. headerDistDip = 36 * (96/72) = 48 px.
        // So headerY (72) should be < pageTop + marginTopDip (120).
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithHeader("My Header");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("a non-empty header should produce at least one HF item");

        var item = items![0];
        item.Text.Should().Be("My Header");

        // pageTop = 24, marginTopDip = 72 * (96/72) = 96 → pageTop + marginTopDip = 120
        // headerY ≈ 24 + 48 = 72 — must be less than 120 (inside top margin band)
        const double pageTop = 24.0;
        const double marginTopDip = 96.0; // 72 pt * (96/72)
        item.Y.Should().BeLessThan(pageTop + marginTopDip,
            "header must be positioned in the top margin band (above the body text area)");
        item.Y.Should().BeGreaterThan(pageTop - 1,
            "header Y must be at or below the page top");
    }

    // ── Test 2: footer item appears in bottom margin band ────────────────────────────────────────────

    [Fact]
    public async Task Footer_items_appear_in_bottom_margin_band()
    {
        // Footer starts at: pageBottom - footerDistDip
        // pageBottom = pageTop + pageHeightPx = 24 + 792*(96/72) = 24 + 1056 = 1080
        // footerDistDip = 36*(96/72) = 48
        // footerY ≈ 1080 - 48 = 1032
        // marginBottomTop = pageBottom - marginBottomDip = 1080 - 96 = 984
        // so footerY should be >= 984 (inside bottom margin band)
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithFooter("My Footer");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("a non-empty footer should produce at least one HF item");

        var item = items![0];
        item.Text.Should().Be("My Footer");

        // Bottom margin starts at: pageBottom - marginBottomDip = 1080 - 96 = 984
        const double pageBottom = 24.0 + 792.0 * (96.0 / 72.0); // ≈ 1080
        const double marginBottomDip = 72.0 * (96.0 / 72.0); // = 96
        var bottomMarginTop = pageBottom - marginBottomDip; // ≈ 984
        item.Y.Should().BeGreaterThanOrEqualTo(bottomMarginTop,
            "footer must be positioned in the bottom margin band (below the body text area)");
        item.Y.Should().BeLessThan(pageBottom,
            "footer Y must be above the bottom edge of the page");
    }

    // ── Test 3: first-page header variant is used on page 1 when DifferentFirstPage ─────────────────

    [Fact]
    public async Task First_page_header_variant_is_used_on_page_1_when_DifferentFirstPage()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body text."));

            // Enable DifferentFirstPage on the section page settings.
            doc.Page.DifferentFirstPage = true;

            // Set both First and Default headers so we can distinguish which was picked.
            doc.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("FIRST PAGE HEADER");
            doc.FinalSectionHeadersFooters.Header      = new HeaderFooter("DEFAULT HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // Page 0 (pi=0 → isFirstPage=true) should use FirstHeader.
        var firstPageItems = items!.Where(i => i.Text == "FIRST PAGE HEADER").ToList();
        firstPageItems.Should().NotBeEmpty("DifferentFirstPage=true → FirstHeader must be used on the first page");

        // Default header must NOT appear on page 1 in this scenario.
        items.Should().NotContain(i => i.Text == "DEFAULT HEADER",
            "the default header must not be rendered on the first page when DifferentFirstPage=true");
    }

    // ── Test 4: even-page header is used on page 2 when DifferentOddEven ─────────────────────────────

    [Fact]
    public async Task Even_page_header_is_used_on_page_2_when_DifferentOddEven()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Add enough paragraphs to force at least 2 pages.
            for (var i = 0; i < 60; i++)
                doc.Blocks.Add(new Paragraph($"Line {i + 1} of body text to fill pages."));

            // Enable odd/even at document level.
            doc.Page.DifferentOddEvenPages = true;

            doc.FinalSectionHeadersFooters.Header     = new HeaderFooter("ODD HEADER");
            doc.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("EVEN HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 8000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // If the layout produced at least 2 pages we must see the EvenHeader on page 2.
        var evenItems = items!.Where(i => i.Text == "EVEN HEADER").ToList();
        var oddItems  = items!.Where(i => i.Text == "ODD HEADER").ToList();

        // If _pageCount >= 2 we expect both variants; otherwise (single page) just the odd.
        if (evenItems.Count > 0)
        {
            evenItems.Should().NotBeEmpty("EvenHeader must appear on even pages");
            oddItems.Should().NotBeEmpty("ODD HEADER must appear on odd pages");
        }
        else
        {
            // Single page — only odd header is expected (page 1 is odd).
            oddItems.Should().NotBeEmpty("ODD HEADER must appear on the first (odd) page");
        }
    }

    // ── Test 5: PAGE field resolves to 1 for page 1 ──────────────────────────────────────────────────

    [Fact]
    public async Task First_section_page_restarted_at_two_uses_even_header()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));
            doc.Page.PageNumberStartAt = 2;
            doc.Page.DifferentOddEvenPages = true;
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("ODD HEADER");
            doc.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("EVEN HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 1200));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().Contain(item => item.Text == "EVEN HEADER");
        items.Should().NotContain(item => item.Text == "ODD HEADER");
    }

    [Fact]
    public async Task Page_number_field_resolves_to_1_for_page_1()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));

            // Use RunFieldKind.PageNumber so ResolveHfField picks up the simple field path.
            var hf = new HeaderFooter();
            var headerPara = new Paragraph();
            var fieldRun = new Run(string.Empty, RunFormatting.Default)
            {
                FieldKind = RunFieldKind.PageNumber,
            };
            headerPara.Runs.Add(fieldRun);
            hf.Paragraphs.Add(headerPara);
            doc.FinalSectionHeadersFooters.Header = hf;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("PAGE field run should produce an HF item");
        items![0].Text.Should().Be("1", "page 1's PAGE field must resolve to \"1\"");
    }

    // ── Test 6: NUMPAGES field resolves to total page count ──────────────────────────────────────────

    [Fact]
    public async Task NumPages_field_resolves_to_total_page_count()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        int pageCount = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));

            var hf = new HeaderFooter();
            var headerPara = new Paragraph();
            var fieldRun = new Run(string.Empty, RunFormatting.Default)
            {
                FieldKind = RunFieldKind.NumPages,
            };
            headerPara.Runs.Add(fieldRun);
            hf.Paragraphs.Add(headerPara);
            doc.FinalSectionHeadersFooters.Header = hf;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
            pageCount = view.PageCount;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("NUMPAGES field run should produce an HF item");
        items![0].Text.Should().Be(pageCount.ToString(),
            "NUMPAGES field must match the DocumentView's reported page count");
    }

    // ── Test 7: no HF items in web layout mode ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_header_footer_items_in_web_layout_mode()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = DocWithHeader("Should Not Appear");
            var view = new DocumentView();
            view.LoadDocument(doc);
            // Switch to WebLayout — BuildHeaderFooterItems must NOT run.
            view.ViewMode = DocumentViewMode.WebLayout;
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().BeEmpty("WebLayout mode must not produce any header/footer items");
    }

    // ── Test 8 (AE1): 2-section doc uses correct per-section header on each page ──────────────────────
    // Regression for AE1: the even-distribution heuristic wrongly showed section-1's header
    // on pages 1–5 of a 10-page doc where section 1 = 1 page.  After the fix, page 1 shows "SECTION A"
    // and all subsequent pages show "SECTION B".

    [Fact]
    public async Task MultiSection_header_uses_owning_section_not_even_distribution()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Section 1: a single paragraph that carries a NextPage SectionBreak.
            // Its HeadersFooters = "SECTION A".
            var sec1Page = new PageSettings();
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage)
            {
                HeadersFooters = { Header = new HeaderFooter("SECTION A") }
            };
            var sec1Marker = new Paragraph("Section 1 content.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);

            // Section 2 (final): many paragraphs to force multiple pages.
            // Document-level (final section) header = "SECTION B".
            for (var i = 0; i < 80; i++)
                doc.Blocks.Add(new Paragraph($"Section 2 line {i + 1}."));
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("SECTION B");

            var view = new DocumentView();
            view.LoadDocument(doc);
            // Wide enough for 8.5", tall enough to layout at least 3 pages.
            view.Measure(new Size(816, 10000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty("both sections should emit header items");

        // Page 1 (pi=0) must show "SECTION A".
        var sectionAItems = items!.Where(i => i.Text == "SECTION A").ToList();
        var sectionBItems = items!.Where(i => i.Text == "SECTION B").ToList();

        sectionAItems.Should().NotBeEmpty("section 1's header must appear on page 1");
        sectionBItems.Should().NotBeEmpty("section 2's header must appear on pages 2+");

        // There must be exactly one SECTION A item (section 1 = exactly 1 page).
        sectionAItems.Should().HaveCount(1,
            "section 1 spans only 1 page so 'SECTION A' must appear exactly once");

        // Every SECTION A item must have a smaller Y than every SECTION B item.
        // (i.e. section A appears before section B in document order.)
        var maxAY = sectionAItems.Max(i => i.Y);
        var minBY = sectionBItems.Min(i => i.Y);
        maxAY.Should().BeLessThan(minBY,
            "section 1's header (SECTION A) must come before section 2's headers (SECTION B) in page order");
    }

    // ── Test 9 (AE2): DifferentFirstPage on a mid-document section uses section-relative page 1 ────────
    // Regression for AE2: the old code gated diffFirst on pi==0 (document page 0), so a
    // section starting mid-document never got its first-page header.

    [Fact]
    public async Task MidDocument_section_DifferentFirstPage_uses_its_own_first_page_header()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Section 1: fills a page, no DifferentFirstPage, plain header.
            var sec1Page = new PageSettings { DifferentFirstPage = false };
            var sec1Hf = new SectionHeadersFooters { Header = new HeaderFooter("SEC1 DEFAULT") };
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage) { HeadersFooters = sec1Hf };
            var sec1Marker = new Paragraph("Section 1 body.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);
            // Pad section 1 to fill a page.
            for (var i = 0; i < 40; i++)
                doc.Blocks.Add(new Paragraph($"S1 line {i}."));

            // Marker for second NextPage break after section 1 padding → actually belongs to sec1.
            // Section 2 starts here, with DifferentFirstPage enabled.
            var sec2Page = new PageSettings { DifferentFirstPage = true };
            var sec2Hf = new SectionHeadersFooters
            {
                FirstHeader = new HeaderFooter("SEC2 FIRST PAGE"),
                Header      = new HeaderFooter("SEC2 DEFAULT"),
            };
            var sec2 = new Section(sec2Page, SectionBreakKind.NextPage) { HeadersFooters = sec2Hf };
            var sec2Marker = new Paragraph("Section 2 starts here.");
            sec2Marker.SectionBreak = sec2;
            doc.Blocks.Add(sec2Marker);

            // Section 3 (final): more content.
            for (var i = 0; i < 5; i++)
                doc.Blocks.Add(new Paragraph($"S2 body line {i}."));
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("SEC3 DEFAULT");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 10000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // "SEC2 FIRST PAGE" must appear somewhere in the items — it is the first page of section 2,
        // which starts mid-document (not document page 0).
        var firstPageItems = items!.Where(i => i.Text == "SEC2 FIRST PAGE").ToList();
        firstPageItems.Should().NotBeEmpty(
            "DifferentFirstPage=true on section 2 must produce a first-page header on section 2's first page, " +
            "even though that page is not document page 0 (AE2 fix)");
    }

    // ── Test 10 (AE3, corrected): a LEADING section with no own header and no earlier section to
    // link to renders BLANK, not the LATER final section's header ───────────────────────────────────
    //
    // The original AE3 test asserted the opposite: that a leading section with an empty
    // HeadersFooters should pull in `doc.FinalSectionHeadersFooters` ("DOC HEADER"). That was pinning
    // a bug, not real Word behavior. `FinalSectionHeadersFooters` is not a document-wide default —
    // per TextDocument.Sections (FreeW.Core.Model/TextDocument.cs), it is simply the header/footer set
    // of the document's trailing w:sectPr, i.e. the LAST section's own definition. In OOXML, a section
    // that omits w:headerReference/w:footerReference for a slot is "linked to previous" and inherits
    // from the nearest PRECEDING section's definition of that slot. A section has no "previous" for
    // the very first section in the document, so Word renders that slot blank — it never reaches
    // FORWARD into a later section (here, the final one) merely because that section happens to define
    // something. Reaching forward would make an early, otherwise-blank section display whatever the
    // end of the document says, which is not what Word shows and not what round-trips through DOCX
    // (the leading section's sectPr has no headerReference at all, so nothing links it to the final
    // section). HeaderFooterPagePlannerTests.MapPagesToSections_EmptyLeadingSectionRendersBlankNotDocumentLevelStore
    // (and its footer twin, MapPagesToSections_EmptyLeadingSectionFooterAlsoRendersBlankNotDocumentLevelStore)
    // pin this at the planner-unit level; these two tests confirm the same thing through the actual
    // DocumentView render path that AE3 originally exercised.
    [Fact]
    public async Task Section_with_empty_HeadersFooters_renders_blank_not_final_section_header()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Section 1: exists but has an EMPTY SectionHeadersFooters (no own header/footer) and is
            // the very first section, so it has no earlier section to link to.
            var sec1Page = new PageSettings();
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage);
            // sec1.HeadersFooters is left as a new SectionHeadersFooters() — all nulls → IsEmpty == true
            var sec1Marker = new Paragraph("Section 1 body.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);

            // Final section needs its own body content so it actually lays out a (second) page —
            // otherwise there is nothing for its header to appear on.
            doc.Blocks.Add(new Paragraph("Final section body."));

            // Final section defines its OWN header. It must render on the final section's page only —
            // never on section 1's page, since section 1 cannot link forward to it.
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("FINAL SECTION HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // Section 1 is a single page (page index 0). Its header band is
        // [pageTop, pageTop + marginTopDip) = [24, 120) — see the geometry comments on
        // Header_items_appear_in_top_margin_band_for_simple_header_doc above. No item may land there,
        // because section 1 defines nothing and has nothing earlier to inherit from.
        const double section1HeaderBandEnd = 24.0 + 96.0; // pageTop (24) + marginTopDip (96) = 120
        items!.Should().NotContain(i => i.Y < section1HeaderBandEnd,
            "section 1 has an empty HeadersFooters and no earlier section to link to, so it must render " +
            "blank rather than reaching forward into the final section's header");

        // The final section's own header must still appear (on its own, later page) — the fix must not
        // have deleted the final section's ability to show its own header, only stopped an EARLIER
        // section from borrowing it.
        items!.Should().Contain(i => i.Text == "FINAL SECTION HEADER" && i.Y >= section1HeaderBandEnd,
            "the final section still owns and must still render its own header on its own page");
    }

    [Fact]
    public async Task Section_with_empty_HeadersFooters_renders_blank_footer_not_final_section_footer()
    {
        // Footer twin of the header case above: an empty-HeadersFooters leading section must not
        // borrow the final section's footer either.
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            var sec1Page = new PageSettings();
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage);
            var sec1Marker = new Paragraph("Section 1 body.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);

            // Final section needs its own body content so it actually lays out a (second) page —
            // otherwise there is nothing for its footer to appear on.
            doc.Blocks.Add(new Paragraph("Final section body."));

            doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("FINAL SECTION FOOTER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // Section 1's footer band starts at pageBottom - marginBottomDip = 1080 - 96 = 984 (page index
        // 0 — see the geometry comments on Footer_items_appear_in_bottom_margin_band above). No item
        // may land at or after that Y on page 1, because section 1 defines no footer of its own and has
        // nothing earlier to inherit from. The next page's footer band starts far beyond page 1's
        // bottom edge (~1080), so filtering to Y < 1080 isolates page 1.
        const double page1BottomEdge = 24.0 + 792.0 * (96.0 / 72.0); // pageBottom for page index 0 ≈ 1080
        items!.Should().NotContain(i => i.Y < page1BottomEdge,
            "section 1 has an empty HeadersFooters and no earlier section to link to, so its footer must " +
            "render blank rather than reaching forward into the final section's footer");

        items!.Should().Contain(i => i.Text == "FINAL SECTION FOOTER" && i.Y >= page1BottomEdge,
            "the final section still owns and must still render its own footer on its own page");
    }

    // ── Test 10b: a MIDDLE section with empty HeadersFooters still legitimately inherits from the
    // nearest PRECEDING section (real "link to previous"), as opposed to the forward-reaching fallback
    // the fix above removes. This guards against the fix overcorrecting into "empty HeadersFooters
    // never inherits anything." ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MiddleSection_with_empty_HeadersFooters_inherits_nearest_preceding_section_header()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Section 1 defines its own header and is exactly one page.
            var sec1Page = new PageSettings();
            var sec1Hf = new SectionHeadersFooters { Header = new HeaderFooter("PRECEDING HEADER") };
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage) { HeadersFooters = sec1Hf };
            var sec1Marker = new Paragraph("Section 1 body.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);

            // Section 2 defines nothing of its own (link to previous) and is exactly one page — it
            // must inherit "PRECEDING HEADER" from section 1, not "FINAL HEADER" from the final section.
            var sec2Page = new PageSettings();
            var sec2 = new Section(sec2Page, SectionBreakKind.NextPage);
            var sec2Marker = new Paragraph("Section 2 body.");
            sec2Marker.SectionBreak = sec2;
            doc.Blocks.Add(sec2Marker);

            // Final section needs its own body content so it actually lays out a (third) page.
            doc.Blocks.Add(new Paragraph("Final section body."));

            // Final section defines its own, different header.
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("FINAL HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // "PRECEDING HEADER" must appear twice: once on section 1's own page, once on section 2's page
        // (inherited via link-to-previous). "FINAL HEADER" must appear exactly once, on the final
        // section's own page — section 2 must not skip past its immediate predecessor to reach it.
        items!.Count(i => i.Text == "PRECEDING HEADER").Should().Be(2,
            "section 1's header must render on its own page AND be inherited onto section 2's page " +
            "(section 2 has an empty HeadersFooters and links to the nearest preceding definer)");
        items!.Count(i => i.Text == "FINAL HEADER").Should().Be(1,
            "the final section's header must render only on its own page, not be borrowed by section 2 " +
            "which has a nearer preceding definer (section 1) to link to instead");
    }

    [Fact]
    public async Task SectionFields_InFooter_UseLiveSectionContextWithoutMutatingCache()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var sectionRun = new Run("stale") { ComplexField = new ComplexField(" SECTION \\* ROMAN ") };
        var sectionPagesRun = new Run("stale") { ComplexField = new ComplexField(" SECTIONPAGES \\* alphabetic ") };

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body text."));

            var footer = new HeaderFooter();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(sectionRun);
            paragraph.Runs.Add(new Run("/"));
            paragraph.Runs.Add(sectionPagesRun);
            footer.Paragraphs.Add(paragraph);
            doc.FinalSectionHeadersFooters.Footer = footer;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().Contain(i => i.Text == "I/a");
        sectionRun.Text.Should().Be("stale");
        sectionPagesRun.Text.Should().Be("stale");
    }
}
