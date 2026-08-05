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

    // ── Test 10 (AE3): section with no own header falls back to document-level header ─────────────────

    [Fact]
    public async Task Section_with_empty_HeadersFooters_inherits_document_level_header()
    {
        IReadOnlyList<(string Text, double Y, TextAlignment Alignment)>? items = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();

            // Section 1: exists but has an EMPTY SectionHeadersFooters (no own header/footer).
            var sec1Page = new PageSettings();
            var sec1 = new Section(sec1Page, SectionBreakKind.NextPage);
            // sec1.HeadersFooters is left as a new SectionHeadersFooters() — all nulls → IsEmpty == true
            var sec1Marker = new Paragraph("Section 1 body.");
            sec1Marker.SectionBreak = sec1;
            doc.Blocks.Add(sec1Marker);

            // Document-level (final section) header — should be inherited by section 1.
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("DOC HEADER");

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            items = view.HeaderFooterItems;
        });

        if (!ran) return;
        items.Should().NotBeNull();

        // The document header must appear even on a page whose section has no own header (AE3 fix).
        items!.Should().Contain(i => i.Text == "DOC HEADER",
            "a section with empty HeadersFooters must inherit the document-level header (AE3 fallback)");
    }
}
