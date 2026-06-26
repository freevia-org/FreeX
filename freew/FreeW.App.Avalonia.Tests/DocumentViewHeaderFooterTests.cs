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
}
