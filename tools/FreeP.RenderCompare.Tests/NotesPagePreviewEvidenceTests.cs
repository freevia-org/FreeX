using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.RenderCompare.Tests;

public sealed class NotesPagePreviewEvidenceTests
{
    [Fact]
    public void CreatePlan_UsesNotesPagePreviewEvidenceRoutes()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-notes-page-preview-");
        var root = temporaryDirectory.Path;
        var deck = Path.Combine(root, "deck.pptx");

        var plan = NotesPagePreviewEvidence.CreatePlan(deck, root);

        plan.DeckPath.Should().Be(Path.GetFullPath(deck));
        plan.OutputDirectory.Should().Be(Path.GetFullPath(root));
        plan.PdfPath.Should().Be(Path.Combine(Path.GetFullPath(root), "freep-notes-page-preview.pdf"));
        plan.SummaryCsvPath.Should().Be(Path.Combine(Path.GetFullPath(root), "notes-page-preview-evidence.csv"));
        plan.RequiresPowerPointBaseline.Should().BeFalse();
    }

    [Fact]
    public void BuildRows_ReportsSharedWpfAvaloniaEvidenceForOverflowingStyledNotes()
    {
        var presentation = BuildOverflowingStyledNotesDeck();
        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);

        var rows = NotesPagePreviewEvidence.BuildRows(renderPlan);

        rows.Should().HaveCountGreaterThan(1);
        rows.Select(row => row.OutputPageNumber).Should().Equal(Enumerable.Range(1, rows.Count));
        rows.Should().OnlyContain(row =>
            row.SlideNumber == 1 &&
            row.WpfEvidence == "shared-notes-page-pdf-render-plan" &&
            row.AvaloniaEvidence == "shared-notes-page-pdf-render-plan" &&
            row.PowerPointBaseline == "not-required-for-local-wpf-avalonia-evidence");
        rows[0].IsContinuation.Should().BeFalse();
        rows[0].StyledRunCount.Should().BeGreaterThan(1);
        rows.Skip(1).Should().OnlyContain(row => row.IsContinuation);
        rows.Sum(row => row.NoteLineCount).Should().Be(renderPlan.PreviewPlans[0].NoteLines.Count);
    }

    [Fact]
    public void WriteSummaryCsv_WritesEvidenceRowsWithEscapedText()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("freep-notes-page-preview-csv-");
        var root = temporaryDirectory.Path;
        var csvPath = Path.Combine(root, "summary.csv");
        var rows = new[]
        {
            new NotesPagePreviewEvidenceRow(
                OutputPageNumber: 1,
                SlideNumber: 2,
                SlideRenderedPageNumber: 1,
                IsContinuation: false,
                FirstNoteLineIndex: 0,
                NoteLineCount: 3,
                ShowsPlaceholder: false,
                StyledRunCount: 2,
                ThumbnailLabel: "Slide 2 notes",
                Detail: "Notes page, with comma",
                WpfEvidence: "shared-notes-page-pdf-render-plan",
                AvaloniaEvidence: "shared-notes-page-pdf-render-plan",
                PowerPointBaseline: "not-required-for-local-wpf-avalonia-evidence")
        };

        NotesPagePreviewEvidence.WriteSummaryCsv(csvPath, rows);

        File.ReadAllLines(csvPath).Should().Equal(
            "outputPage,slideNumber,slideRenderedPage,isContinuation,firstNoteLine,noteLineCount,showsPlaceholder,styledRunCount,thumbnailLabel,detail,wpfEvidence,avaloniaEvidence,powerPointBaseline",
            "1,2,1,false,0,3,false,2,Slide 2 notes,\"Notes page, with comma\",shared-notes-page-pdf-render-plan,shared-notes-page-pdf-render-plan,not-required-for-local-wpf-avalonia-evidence");
    }

    private static Presentation BuildOverflowingStyledNotesDeck()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.NotesPageSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
        presentation.NotesPageSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(360);

        var slide = new Slide { Title = "Presenter notes" };
        var notes = new TextBody();
        var styled = new Paragraph();
        styled.Runs.Add(new Run { Text = "Critical ", Bold = true });
        styled.Runs.Add(new Run
        {
            Text = "launch note",
            Italic = true,
            Color = new ThemeAwareColor(new SrgbColor(0xC0, 0x00, 0x00))
        });
        notes.Paragraphs.Add(styled);

        for (var i = 1; i <= 16; i++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = $"Follow-up talking point {i}." });
            notes.Paragraphs.Add(paragraph);
        }

        slide.Notes = notes;
        presentation.Slides.Add(slide);
        return presentation;
    }
}
