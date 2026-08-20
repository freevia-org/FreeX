using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for freew-footnote-numbering F3: under
/// <see cref="NoteNumberRestart.EachPage"/>, the in-body superscript reference mark
/// (<see cref="DocumentView.ResolveNoteBodyMarkDisplayNumber"/>, exercised here through the real
/// <see cref="PageBox"/> production path built by <see cref="PaginatedEditorPanel.Build"/>) must show
/// the same page-relative number the footnote region at the foot of that same page shows, not the
/// document-wide continuous count.
///
/// <para>Runs on STA because tests create real WPF <see cref="DocumentView"/> / <see cref="PageBox"/>
/// instances.</para>
/// </summary>
public sealed class FootnoteEachPageBodyMarkTests
{
    [StaFact]
    public void EachPageRestart_BodyMarkOnPage2_MatchesPageRelativeFootnoteNumber()
    {
        var doc = BuildTwoPageFootnoteDoc();
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

        var editor = BuildEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);

        panel.PageBoxes.Should().HaveCountGreaterThanOrEqualTo(2,
            "the explicit page break must place footnote 2's paragraph on a second page box");

        var page2Box = panel.PageBoxes[1];
        var markers = DocumentView.CollectFootnoteMarkers(page2Box.Body.Document.Blocks);
        markers.Should().ContainSingle(m => m.FootnoteId == 2,
            "footnote 2's reference run must be on the second page box");

        var displayed = MarkerText(markers.Single(m => m.FootnoteId == 2));

        displayed.Should().Be("1",
            "under NoteNumberRestart.EachPage the body mark for the page's only footnote must show " +
            "'1' — the same page-relative number the footnote region at the foot of the same page " +
            "shows — not the document-wide continuous count '2'");
    }

    /// <summary>Sibling no-regression: the default Continuous restart keeps counting across pages.</summary>
    [StaFact]
    public void Continuous_NoRegression_BodyMarkOnPage2_KeepsDocumentWideCount()
    {
        var doc = BuildTwoPageFootnoteDoc();
        // NumberRestart left at its Continuous default.

        var editor = BuildEditor(doc);
        var panel = PaginatedEditorPanel.Build(editor);
        panel.PageBoxes.Should().HaveCountGreaterThanOrEqualTo(2);

        var page2Box = panel.PageBoxes[1];
        var markers = DocumentView.CollectFootnoteMarkers(page2Box.Body.Document.Blocks);
        var displayed = MarkerText(markers.Single(m => m.FootnoteId == 2));

        displayed.Should().Be("2",
            "sibling no-regression: the default Continuous restart must keep counting across pages, " +
            "unaffected by the EachPage fix");
    }

    private static string MarkerText(DocumentView.FootnoteMarkerPosition marker) =>
        new TextRange(
            marker.Position,
            marker.Position.GetPositionAtOffset(1, LogicalDirection.Forward)).Text;

    private static DocumentView BuildEditor(TextDocument doc)
    {
        var editor = new DocumentView();
        editor.LoadModel(doc);
        editor.CommitToModel();
        return editor;
    }

    private static TextDocument BuildTwoPageFootnoteDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Footnotes[1] = new Footnote(1, "Footnote one.");
        doc.Footnotes[2] = new Footnote(2, "Footnote two.");

        var page1 = new Paragraph();
        page1.Runs.Add(new Run("Page one text."));
        page1.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(page1);

        var page2 = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        };
        page2.Runs.Add(new Run("Page two text."));
        page2.Runs.Add(Run.FootnoteReference(2));
        doc.Blocks.Add(page2);

        return doc;
    }
}
