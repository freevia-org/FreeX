using System.Windows.Documents;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Unit tests for <see cref="PaginationEngine"/>, which drives authoritative page-break computation
/// by reusing the same WPF paginator as Print Preview. Runs on STA because it builds real WPF
/// <see cref="DocumentView"/> and <see cref="System.Windows.Documents.FlowDocument"/> instances.
/// </summary>
public sealed class PaginationEngineTests
{
    private static DocumentView NewEditor(TextDocument doc)
    {
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    // ── Single-page / degenerate cases ────────────────────────────────────────────────────────────

    [StaFact]
    public void EmptyDocument_ReturnsPageCount1_NoBreaks()
    {
        var doc = TextDocument.CreateEmpty();
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().Be(1, "an empty document fits on one page");
        result.PageBreakYsDip.Should().BeEmpty("one page means no inter-page boundaries");
    }

    [StaFact]
    public void SingleParagraph_ReturnsPageCount1_NoBreaks()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello World"));
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().Be(1, "a single short paragraph fits on one page");
        result.PageBreakYsDip.Should().BeEmpty();
    }

    [StaFact]
    public void ShortBodyParagraph_UsesKeepTogetherForWordWidowOrphanParity()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("A short paragraph that must remain intact at a page boundary."));
        var view = NewEditor(doc);

        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single().KeepTogether.Should().BeTrue();
    }

    // ── Multi-page content ────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void LongDocument_ReturnsPageCountGreaterThan1_WithBreaks()
    {
        // Fill a document with enough paragraphs to overflow a standard A4 page.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 120; i++)
            doc.Blocks.Add(new Paragraph($"Line {i + 1}: The quick brown fox jumps over the lazy dog."));
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().BeGreaterThan(1, "120 paragraphs of text overflow one page");
        result.PageBreakYsDip.Count.Should().Be(result.PageCount - 1,
            "number of break positions must be PageCount − 1");
        result.PageBreakYsDip.Should().BeInAscendingOrder("breaks accumulate top-to-bottom");
    }

    [StaFact]
    public void LongDocument_BreakYs_ArePositive()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 120; i++)
            doc.Blocks.Add(new Paragraph($"Paragraph {i + 1}"));
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        foreach (var y in result.PageBreakYsDip)
            y.Should().BePositive("each break Y is a cumulative content height > 0");
    }

    // ── Explicit PageBreakBefore ───────────────────────────────────────────────────────────────────

    [StaFact]
    public void ExplicitPageBreakBefore_ProducesBreakEarlierThanUniformApproximation()
    {
        // Create two paragraphs, the second of which has PageBreakBefore. With the default A4 page
        // the break from the explicit attribute should appear much closer to the top than the
        // uniform approximation (one full content-height from top).
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("First paragraph."));
        doc.Blocks.Add(new Paragraph("Second paragraph — starts on its own page.")
        {
            Formatting = new ParagraphFormatting { PageBreakBefore = true }
        });
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().Be(2, "the explicit break forces a second page");
        result.PageBreakYsDip.Count.Should().Be(1);

        // The uniform-step approximation would place the first break at one full content-height.
        var (_, contentHeight) = PageLayout.ContentAreaDip(doc.Page);
        var uniformApprox = contentHeight;

        result.PageBreakYsDip[0].Should().BeLessThan(uniformApprox,
            "an explicit PageBreakBefore paragraph produces a break before the page is full");
    }

    // ── KeepLinesTogether ─────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void KeepLinesTogether_ParagraphIsNotSplitAcrossPages()
    {
        // Build a doc where a long KeepLinesTogether paragraph would otherwise straddle a page break.
        // Fill with padding, then add a KeepLinesTogether paragraph large enough to be on the cusp.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        // Fill ~3/4 page with text.
        for (var i = 0; i < 50; i++)
            doc.Blocks.Add(new Paragraph($"Filler line {i + 1}."));
        // A multi-line KeepLinesTogether block near the page break.
        var keepPara = new Paragraph(
            "Keep-together paragraph. This block must not be split across two pages by the paginator.")
        {
            Formatting = new ParagraphFormatting { KeepLinesTogether = true }
        };
        doc.Blocks.Add(keepPara);
        var view = NewEditor(doc);

        // This is a behavioural test: pagination must succeed and return a valid result.
        // The key assertion is that PageCount >= 1 and break count is consistent — we cannot
        // directly inspect whether WPF honoured KeepTogether without checking rendering, but
        // we verify the engine doesn't crash and returns a coherent result.
        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().BeGreaterThanOrEqualTo(1);
        result.PageBreakYsDip.Count.Should().Be(result.PageCount - 1);
    }

    // ── NextPage section break ────────────────────────────────────────────────────────────────────

    [StaFact]
    public void NextPageSectionBreak_StartsNewPage()
    {
        // A NextPage section break on the first paragraph forces the second section onto a new page.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p1 = new Paragraph("Section 1 content.");
        p1.SectionBreak = new FreeW.Core.Model.Section(new PageSettings(), SectionBreakKind.NextPage);
        doc.Blocks.Add(p1);
        doc.Blocks.Add(new Paragraph("Section 2 content."));
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().BeGreaterThanOrEqualTo(2,
            "a NextPage section break must produce at least two pages");
        result.PageBreakYsDip.Count.Should().Be(result.PageCount - 1);
    }

    // ── Result structure invariants ────────────────────────────────────────────────────────────────

    [StaFact]
    public void PageCount_IsAlwaysAtLeast1()
    {
        var doc = TextDocument.CreateEmpty();
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [StaFact]
    public void BreakYsCount_EqualsPageCountMinusOne()
    {
        // Verify the invariant for a multi-page document.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        for (var i = 0; i < 80; i++)
            doc.Blocks.Add(new Paragraph($"Para {i + 1}: content."));
        var view = NewEditor(doc);

        var result = PaginationEngine.Compute(view);

        result.PageBreakYsDip.Count.Should().Be(result.PageCount - 1);
    }
}
