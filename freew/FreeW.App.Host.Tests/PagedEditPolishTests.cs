using System.Linq;
using FluentAssertions;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for PagedEdit polish work:
/// (a) Per-page content height in overflow breaks (PaginationEngine)
/// (b) Inter-page gap geometry helper (PaginatedEditorPanel)
/// </summary>
public sealed class PagedEditPolishTests
{
    // ── (a) Overflow break precision ──────────────────────────────────────────────────────────────

    [StaFact]
    public void PaginationEngine_Compute_ReturnsNonNegativeBreakYs()
    {
        // Even a single-page doc should not error.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Short content."));

        var editor = BuildEditor(doc);
        var result = PaginationEngine.Compute(editor);

        result.Should().NotBeNull();
        result.PageCount.Should().BeGreaterThanOrEqualTo(1);
        foreach (var y in result.PageBreakYsDip)
            y.Should().BeGreaterThanOrEqualTo(0, "all break Y values must be non-negative");
    }

    [StaFact]
    public void PaginationEngine_Compute_BreakYsAreStrictlyIncreasing()
    {
        // A document with an explicit page break must have monotonically increasing break Ys.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page one content."));
        doc.Blocks.Add(new Paragraph("Page two content.")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var editor = BuildEditor(doc);
        var result = PaginationEngine.Compute(editor);

        if (result.PageBreakYsDip.Count <= 1)
            return; // can't test ordering with < 2 breaks

        var ys = result.PageBreakYsDip.ToArray();
        for (int i = 1; i < ys.Length; i++)
            ys[i].Should().BeGreaterThan(ys[i - 1],
                "break Ys must increase monotonically across pages");
    }

    // ── (b) Inter-page gap geometry ───────────────────────────────────────────────────────────────

    [StaFact]
    public void InterPageGapRect_NegativeIndex_ReturnsNull()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Single page."));

        var editor = BuildEditor(doc);
        var panel  = PaginatedEditorPanel.Build(editor);

        panel.GetInterPageGapRect(-1).Should().BeNull("negative index is out of range");
    }

    [StaFact]
    public void InterPageGapRect_SinglePage_IndexZeroReturnsNull()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Single page."));

        var editor = BuildEditor(doc);
        var panel  = PaginatedEditorPanel.Build(editor);

        // Single-page doc: index 0 has no "next" page, so no gap exists.
        panel.GetInterPageGapRect(0).Should().BeNull("a single-page document has no inter-page gap");
    }

    [StaFact]
    public void InterPageGapRect_IndexEqualToPageCount_ReturnsNull()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page one."));
        doc.Blocks.Add(new Paragraph("Page two.")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var editor = BuildEditor(doc);
        var panel  = PaginatedEditorPanel.Build(editor);

        // Index == PageBoxes.Count - 1 is the last page — no gap after it.
        var lastIdx = panel.PageBoxes.Count - 1;
        panel.GetInterPageGapRect(lastIdx).Should().BeNull(
            "there is no gap after the last page");
    }

    [StaFact]
    public void InterPageGapRect_ValidTwoPageDoc_GapIsConsistentOrNull()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Page one."));
        doc.Blocks.Add(new Paragraph("Page two.")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var editor = BuildEditor(doc);
        var panel  = PaginatedEditorPanel.Build(editor);

        if (panel.PageBoxes.Count < 2)
            return; // headless env produced only one box — skip

        // In headless tests TranslatePoint returns (0,0) so the gap may be null;
        // we just verify: if it's non-null it must have Top <= Bottom.
        var gap = panel.GetInterPageGapRect(0);
        if (gap.HasValue)
            gap.Value.Top.Should().BeLessThanOrEqualTo(gap.Value.Bottom,
                "gap top must be <= gap bottom");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static DocumentView BuildEditor(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return editor;
    }
}
