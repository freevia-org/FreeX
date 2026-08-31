using FluentAssertions;
using Free.Shared.Pdf;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r173 remediation. The round-173 fix made the notes-page PDF exporter position styled runs with
/// real Helvetica advance widths instead of a flat chars * fontSize * 0.55 guess. The word-wrap
/// planner beside it still budgeted line length with that same flat guess, and the two had
/// previously agreed only because they shared the constant -- the wrap planner's own comment
/// claimed it "deliberately over-estimates slightly so wrapped lines never run past the notes-box
/// width in the rendered PDF", which stopped being true the moment one side started measuring.
///
/// A scope auditor demonstrated the consequence: a wrapped line containing a bold capitalised run
/// that the planner believed fitted was drawn at x=694pt on a 540pt-wide page -- entirely off the
/// sheet, so the notes text simply vanished from the exported PDF. That is strictly worse than the
/// misalignment the fix was for.
///
/// Both sides now measure with the same Helvetica tables, the wrap side taking the wider of the
/// regular and bold advance per character since it breaks the line before faces are assigned.
/// These tests assert the invariant directly: nothing the exporter draws may extend past the
/// notes box.
/// </summary>
public sealed class R173_NotesPageWrapMatchesRenderTests
{
    private static Presentation PresentationWithStyledNote(params (string Text, bool Bold)[] runs)
    {
        var presentation = new Presentation();
        var slide = new Slide { Title = "Slide" };
        var notes = new TextBody();
        var paragraph = new Paragraph();
        foreach (var (text, bold) in runs)
            paragraph.Runs.Add(new Run { Text = text, Bold = bold });
        notes.Paragraphs.Add(paragraph);
        slide.Notes = notes;
        presentation.Slides.Add(slide);
        return presentation;
    }

    [Fact]
    public void Bold_capitalised_run_in_a_wrapped_note_never_renders_past_the_notes_box()
    {
        // Capitals in Helvetica Bold are far wider than the old 0.55-per-character average, so a
        // line the character-count wrapper accepted overflowed once runs were positioned by real
        // metrics. Ordinary prose around it makes the line reach the wrap boundary.
        // Bold Helvetica capitals run about 0.94em wide against the 0.55em the old character-count
        // budget assumed, so a line of them that the counter accepted overflows by roughly 70% once
        // runs are positioned by real metrics. A deliberately narrow notes page puts the wrap
        // boundary in reach of an ordinary sentence.
        var presentation = PresentationWithStyledNote(
            ("Notes: ", false),
            ("ALPHA BRAVO CHARLIE DELTA ECHO FOXTROT GOLF HOTEL INDIA JULIET KILO LIMA", true),
            (" end.", false));

        var request = new PresentationNotesPagePdfExportRequest(PageWidth: 360, PageHeight: 360);
        var pages = PresentationNotesPagePdfExporter.BuildDocument(presentation, request).Pages;
        var texts = pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
        texts.Should().NotBeEmpty("the note must actually be exported for this to test anything");

        var preview = PresentationNotesPagePreviewPlanner.Build(presentation, 0, pageWidth: 360, pageHeight: 360);
        var boxRightPt = preview.NotesBounds.Right - PresentationNotesPagePdfExporter.NotesInset;

        foreach (var text in texts)
        {
            var width = PresentationNotesPagePdfExporter.MeasureWidestFaceRunWidth(text.Text, text.FontSize);
            (text.X + width).Should().BeLessThanOrEqualTo(
                boxRightPt,
                "no exported notes text may be drawn past the notes box -- the wrap planner promises " +
                "exactly this, and it stopped being true once only the exporter measured");
        }
    }

    [Fact]
    public void Ordinary_unstyled_note_still_wraps_and_stays_on_the_page()
    {
        // Sibling/no-regression: the common case must not start wrapping absurdly early now that
        // the budget is measured rather than counted.
        var presentation = PresentationWithStyledNote(
            (string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit amet", 12)), false));

        var request = new PresentationNotesPagePdfExportRequest(PageWidth: 360, PageHeight: 360);
        var pages = PresentationNotesPagePdfExporter.BuildDocument(presentation, request).Pages;
        var texts = pages.SelectMany(page => page.Ops).OfType<PdfText>().ToArray();
        texts.Should().NotBeEmpty();

        var preview = PresentationNotesPagePreviewPlanner.Build(presentation, 0, pageWidth: 360, pageHeight: 360);
        var boxRightPt = preview.NotesBounds.Right - PresentationNotesPagePdfExporter.NotesInset;
        foreach (var text in texts)
        {
            var width = PresentationNotesPagePdfExporter.MeasureWidestFaceRunWidth(text.Text, text.FontSize);
            (text.X + width).Should().BeLessThanOrEqualTo(boxRightPt + 0.01);
        }

        // and the lines must still be reasonably full -- a budget that collapsed to a few
        // characters per line would satisfy the bound above while ruining the output.
        texts.Select(t => t.Text.Length).Max().Should().BeGreaterThan(
            20,
            "measured wrapping must not collapse ordinary prose into very short lines");
    }
}
