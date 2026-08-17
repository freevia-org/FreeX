using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round-140 remediation coverage: <see cref="DocumentNoteRegionPlanner.BuildRows"/> was fixed to keep
/// producing a row for an empty (or whitespace-only) note -- Word still prints the separator plus a
/// blank numbered line for it -- but <c>PrintPreviewWindow.BuildNotesAtFoot</c> still threw that row
/// away via a pre-existing <c>.Where(n =&gt; !string.IsNullOrEmpty(n.Text))</c> filter neither the
/// original bug fixer nor the row-preserving fixer touched. That filter fed BOTH of
/// <c>BuildNotesAtFoot</c>'s call sites: the ordinary per-page footnote band every printed page uses,
/// and the dedicated endnote page. These tests drive the real <see cref="PrintLayout.BuildPaginator"/>
/// paginator -- the same one Print, Print Preview, PDF and XPS all consume -- rather than calling
/// <c>DocumentNoteRegionPlanner.BuildRows</c> in isolation.
/// </summary>
public sealed class PrintNotesAtFootEmptyNoteTests
{
    // ── ordinary per-page footnote band (HeaderFooterPaginator.GetPage's own wrap, not the
    //    dedicated-endnote-page branch) ────────────────────────────────────────────────────────────

    [StaFact]
    public void PrintPage_EmptyFootnote_StillDrawsSeparatorAndBlankNumberedLine()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();

        // An empty footnote: it owns a reference mark in the body but no note text at all.
        model.Footnotes[1] = new Footnote(1);

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text with one empty note"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        model.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();
        var page = paginator.GetPage(0);

        var text = ExtractDrawnText((Visual)page.Visual);

        // The label "1." must still be drawn (the separator + numbered line survive even though the
        // note's own text is empty) -- before the fix, BuildNotesAtFoot's late filter discarded the
        // whole row because its Text was empty, so the foot of the page showed nothing at all.
        text.Should().Contain("1.",
            "an existing-but-empty footnote must still render its numbered line on the ordinary " +
            "print/print-preview/PDF/XPS page, matching the interactive Page Layout view");
    }

    [StaFact]
    public void PrintPage_EmptyEndnoteOnOrdinaryPage_StillDrawsBlankNumberedLine()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();

        // An empty endnote, small enough that it renders in the ordinary per-page band (the last body
        // page) rather than forcing a dedicated endnote page.
        model.Endnotes[1] = new Endnote(1);

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body text with one empty endnote"));
        paragraph.Runs.Add(Run.EndnoteReference(1));
        model.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        var headerFooterPaginator = Assert.IsType<HeaderFooterPaginator>(paginator);
        headerFooterPaginator.RequiresDedicatedEndnotePage.Should().BeFalse(
            "one short empty endnote must fit at the foot of the single body page, not force a " +
            "dedicated endnote page -- this test targets BuildNotesAtFoot's ordinary-page call site");

        var page = paginator.GetPage(0);
        var text = ExtractDrawnText((Visual)page.Visual);

        text.Should().Contain("1.",
            "an existing-but-empty endnote must still render its numbered line on the ordinary " +
            "print/print-preview/PDF/XPS page");
    }

    // ── dedicated endnote page (HeaderFooterPaginator.BuildDedicatedEndnotePage) ─────────────────

    [StaFact]
    public void DedicatedEndnotePage_TrailingEmptyEndnote_StillDrawsBlankNumberedLine()
    {
        var model = TextDocument.CreateEmpty();
        model.Blocks.Clear();
        model.Page.WidthPt = 612;
        model.Page.HeightPt = 792;
        model.Page.MarginLeftPt = 72;
        model.Page.MarginRightPt = 72;
        model.Page.MarginTopPt = 72;
        model.Page.MarginBottomPt = 72;

        // Body content that nearly fills the whole 648pt content area of the single body page, so the
        // page has almost no room left at its foot -- but the endnotes below stay modest, so a FRESH
        // dedicated endnote page (which starts back at the top margin, not after this body text) has
        // plenty of room for every one of them. This isolates "does the dedicated page drop the
        // trailing empty note" from "does an overflowing note list get truncated for lack of room",
        // which is a separate, pre-existing limitation this gap is not about.
        for (var i = 1; i <= 26; i++)
            model.Blocks.Add(new Paragraph($"Body line {i:00} filling the page"));

        var referencingParagraph = (Paragraph)model.Blocks[^1];

        // A handful of modest endnotes to overflow the almost-full body page, followed by one trailing
        // empty endnote -- the row the pre-existing filter discarded.
        for (var id = 1; id <= 4; id++)
        {
            model.Endnotes[id] = new Endnote(id, $"endnote body text number {id}");
            referencingParagraph.Runs.Add(Run.EndnoteReference(id));
        }
        model.Endnotes[5] = new Endnote(5);
        referencingParagraph.Runs.Add(Run.EndnoteReference(5));

        var view = new DocumentView();
        view.LoadModel(model);
        var paginator = PrintLayout.BuildPaginator(view);
        paginator.ComputePageCount();

        var headerFooterPaginator = Assert.IsType<HeaderFooterPaginator>(paginator);
        headerFooterPaginator.RequiresDedicatedEndnotePage.Should().BeTrue(
            "the near-full body page must have too little room left for even these five modest " +
            "endnotes, forcing a dedicated endnote page -- this test targets BuildNotesAtFoot's " +
            "OTHER call site, BuildDedicatedEndnotePage");

        var dedicatedPage = paginator.GetPage(paginator.PageCount - 1);
        var text = ExtractDrawnText((Visual)dedicatedPage.Visual);

        text.Should().Contain("1. endnote body text number 1",
            "sibling/neighbour check: the dedicated page must still show the real endnotes normally");
        text.Should().Contain("5.",
            "the trailing empty endnote (id 5) must still render its numbered line on the " +
            "dedicated endnote page, not be silently dropped because its own text is empty");
    }

    /// <summary>
    /// Walks a visual tree and its <see cref="Drawing"/> content, concatenating every
    /// <see cref="GlyphRunDrawing"/>'s original characters -- the same characters
    /// <see cref="System.Windows.Media.FormattedText"/>/<c>DrawingContext.DrawText</c> stamped onto the
    /// glyph run, so this recovers the literal printed text without needing an on-screen render.
    /// </summary>
    private static string ExtractDrawnText(Visual visual)
    {
        var sb = new StringBuilder();
        AppendDrawingText(VisualTreeHelper.GetDrawing(visual), sb);
        var childCount = VisualTreeHelper.GetChildrenCount(visual);
        for (var i = 0; i < childCount; i++)
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                sb.Append(ExtractDrawnText(child));
        return sb.ToString();
    }

    private static void AppendDrawingText(Drawing? drawing, StringBuilder sb)
    {
        switch (drawing)
        {
            case System.Windows.Media.DrawingGroup group:
                foreach (var child in group.Children)
                    AppendDrawingText(child, sb);
                break;
            case GlyphRunDrawing { GlyphRun.Characters: { } characters }:
                sb.Append(characters.ToArray());
                break;
        }
    }
}
