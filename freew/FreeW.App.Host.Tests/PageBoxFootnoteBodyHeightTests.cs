using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for freew-footnote-layout finding F2: the on-screen Page Layout
/// (PagedEdit) view must not grow a page box taller than a footnote-free page when the page
/// carries a footnote or endnote region. <see cref="PageBox"/>'s body <c>MinHeight</c> must be
/// shrunk by the note region's own rendered height (the same "carve out of the fixed content
/// area" strategy Print/Print Preview/PDF use via <c>PagePadding.Bottom</c>), not left at the
/// full nominal page content height with the note region appended below it.
/// </summary>
public sealed class PageBoxFootnoteBodyHeightTests
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 1. Core fix: a page box with a footnote region must be the same total height as one without.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PageBoxHeight_WithFootnote_MatchesPageBoxHeight_WithoutFootnote()
    {
        var withoutFootnote = BuildSingleParagraphDoc(withFootnote: false);
        var withFootnote = BuildSingleParagraphDoc(withFootnote: true);

        var plainHeight = MeasureFirstPageBoxHeight(withoutFootnote);
        var footnoteHeight = MeasureFirstPageBoxHeight(withFootnote);

        footnoteHeight.Should().BeApproximately(plainHeight, 0.5,
            "the footnote region must be carved out of the fixed body content area (like Print/" +
            "PDF), not appended below a full-height body -- the page box must stay the true page " +
            "size regardless of whether it carries a footnote");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 2. Sibling / no-regression: a note-free page box's body must still fill the full content
    //    area (i.e. the shrink logic only engages when a note region is actually present).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PageBoxHeight_NoNotes_BodyMinHeight_StillFillsFullContentArea()
    {
        var doc = BuildSingleParagraphDoc(withFootnote: false);
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);

        var box = panel.PageBoxes[0];
        var (_, contentHeight) = PageLayout.ContentAreaDip(doc.Page);
        var (_, marginTop, _, marginBottom) = PageLayout.MarginsDip(doc.Page);

        box.Body.MinHeight.Should().BeApproximately(contentHeight + marginTop + marginBottom, 0.01,
            "a page box carrying no footnote/endnote region must keep the pre-fix full-page-height " +
            "body floor unchanged");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 3. Regression coverage: the note region must be pre-measured at the SAME width it actually
    //    renders at (the full page width, since the note region's own TextBlocks re-apply the page
    //    margins via their Margin), not at contentWidth (which has the margins already removed --
    //    double-subtracting them wraps ~30% narrower than reality). A single-line note cannot tell
    //    the two widths apart, so this note is deliberately long enough to wrap to multiple lines
    //    at the true width.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private const string LongWrappingNoteText =
        "This is a long footnote reference note that is deliberately written to wrap across " +
        "several lines when it is rendered inside the note region of a printed or on-screen " +
        "paginated document page box, even at the full available content width of the page.";

    [StaFact]
    public void PageBoxHeight_WithWrappingFootnote_MatchesPageBoxHeight_WithoutFootnote()
    {
        var withoutFootnote = BuildSingleParagraphDoc(withFootnote: false);
        var withFootnote = BuildSingleParagraphDoc(withFootnote: true, noteText: LongWrappingNoteText);

        var plainHeight = MeasureFirstPageBoxHeight(withoutFootnote);
        var footnoteHeight = MeasureFirstPageBoxHeight(withFootnote);

        footnoteHeight.Should().BeApproximately(plainHeight, 0.5,
            "a footnote long enough to wrap to multiple lines must be pre-measured at the width " +
            "the note region actually renders at (the full page width -- its TextBlocks re-apply " +
            "the margins themselves), not at contentWidth, which already has the margins removed " +
            "and so double-subtracts them, wraps narrower than reality, and over-reduces " +
            "Body.MinHeight -- making the page box come out shorter than the true page");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // 4. Sibling / no-regression: the same fix must hold up when there are multiple wrapping notes
    //    on one page (the discrepancy compounds across notes if the pre-measure width is wrong).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PageBoxHeight_WithMultipleWrappingFootnotes_MatchesPageBoxHeight_WithoutFootnotes()
    {
        var withoutFootnotes = BuildSingleParagraphDoc(withFootnote: false);
        var withFootnotes = BuildTwoFootnoteParagraphDoc(LongWrappingNoteText, LongWrappingNoteText);

        var plainHeight = MeasureFirstPageBoxHeight(withoutFootnotes);
        var footnoteHeight = MeasureFirstPageBoxHeight(withFootnotes);

        footnoteHeight.Should().BeApproximately(plainHeight, 0.5,
            "the pre-measure width fix must hold up across multiple wrapping notes on the same " +
            "page, not just a single one -- a wrong pre-measure width compounds with each " +
            "additional note");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static double MeasureFirstPageBoxHeight(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        var panel = PaginatedEditorPanel.Build(editor);

        var box = panel.PageBoxes[0];
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return box.DesiredSize.Height;
    }

    private static TextDocument BuildSingleParagraphDoc(bool withFootnote, string? noteText = null)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var para = new Paragraph();
        para.Runs.Add(new Run("Body text."));
        if (withFootnote)
        {
            doc.Footnotes[1] = new Footnote(1, noteText ?? "A single footnote.");
            para.Runs.Add(Run.FootnoteReference(1));
        }

        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument BuildTwoFootnoteParagraphDoc(string noteText1, string noteText2)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var para = new Paragraph();
        para.Runs.Add(new Run("Body text."));
        doc.Footnotes[1] = new Footnote(1, noteText1);
        para.Runs.Add(Run.FootnoteReference(1));
        para.Runs.Add(new Run(" More body text."));
        doc.Footnotes[2] = new Footnote(2, noteText2);
        para.Runs.Add(Run.FootnoteReference(2));

        doc.Blocks.Add(para);
        return doc;
    }
}
