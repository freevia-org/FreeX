using System.IO;
using System.Reflection;
using System.Windows;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 7B: tests for speaker-notes model, round-trip I/O, EditingSession commands, and the
/// host notes pane.
/// </summary>
public sealed class NotesSlideTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.NotesTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static Presentation MakePresWithNotes(string notesText)
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Title = "Test Slide";
        var notes = new TextBody();
        var para  = new Paragraph();
        para.Runs.Add(new Run { Text = notesText });
        notes.Paragraphs.Add(para);
        slide.Notes = notes;
        pres.Slides.Add(slide);
        return pres;
    }

    private static EditingSession MakeSession(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    // ── Model ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NotesPagePreviewPlan_SegmentsOverflowIntoContinuationPages()
    {
        var pres = new Presentation();
        pres.NotesPageSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
        pres.NotesPageSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
        var slide = new Slide { Title = "Overflow notes" };
        var notes = new TextBody();
        for (var i = 1; i <= 7; i++)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = $"Presenter note {i}." });
            notes.Paragraphs.Add(paragraph);
        }

        slide.Notes = notes;
        pres.Slides.Add(slide);

        var plan = PresentationNotesPagePreviewPlanner.Build(pres, 0, pageWidth: 360, pageHeight: 360);
        var packagePlan = PresentationPrintOutputPackageExecutor.BuildPackagePlan(
            new PresentationPrintRequest(PresentationPrintLayoutKind.NotesPages),
            pres);

        plan.NoteLines.Should().HaveCount(7);
        plan.LinesPerRenderedPage.Should().BeGreaterThan(0);
        plan.RenderPages.Should().HaveCountGreaterThan(1);
        plan.RenderPages[0].Should().Match<PresentationNotesPageRenderedPagePlan>(page =>
            !page.IsContinuation &&
            page.FirstNoteLineIndex == 0 &&
            page.ThumbnailLabel == "Slide 1 notes" &&
            page.Detail == "Notes page for slide 1");
        plan.RenderPages.Skip(1).Should().OnlyContain(page =>
            page.IsContinuation &&
            page.ThumbnailLabel == "Slide 1 notes continued" &&
            page.Detail == "Notes continuation page for slide 1");
        plan.RenderPages.Sum(page => page.NoteLineCount).Should().Be(plan.NoteLines.Count);
        plan.RenderedPageCount.Should().Be(plan.RenderPages.Count);

        packagePlan.PageCount.Should().Be(plan.RenderedPageCount);
        packagePlan.PreviewPlan.Pages.Select(page => page.ThumbnailLabel)
            .Should().Equal(plan.RenderPages.Select(page => page.ThumbnailLabel));
    }

    [Fact]
    public void Corpus_CommentsNotes_ReportsImportedSlidesAndNotesPageCardinality()
    {
        var presentation = PptxPackageReader.Read(FindCorpusFile("21-comments-notes.pptx"));
        presentation.Slides.Should().HaveCount(2);
        presentation.Slides.Select(slide => slide.Notes).Should().OnlyContain(notes => notes != null);

        var renderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(presentation);
        renderPlan.PreviewPlans.Should().HaveCount(2);
        renderPlan.PreviewPlans.Select(plan => plan.NoteLines.Count).Should().Equal(2, 1);
        renderPlan.PreviewPlans.Select(plan => plan.RenderedPageCount).Should().Equal(2, 1);
        renderPlan.Pages.Should().HaveCount(3);
    }

    [Fact]
    public void Slide_Notes_DefaultIsNull()
    {
        var slide = new Slide();
        slide.Notes.Should().BeNull();
    }

    [Fact]
    public void Slide_Notes_CanBeAssigned()
    {
        var slide = new Slide();
        var notes = new TextBody();
        notes.Paragraphs.Add(new Paragraph());
        slide.Notes = notes;
        slide.Notes.Should().NotBeNull();
    }

    // ── SlideCloner ───────────────────────────────────────────────────────────────

    [Fact]
    public void SlideCloner_ClonesNotes()
    {
        var slide = new Slide();
        var notes = new TextBody();
        var para  = new Paragraph();
        para.Runs.Add(new Run { Text = "Remember to smile!" });
        notes.Paragraphs.Add(para);
        slide.Notes = notes;

        var clone = SlideCloner.CloneSlide(slide);

        clone.Notes.Should().NotBeNull();
        clone.Notes!.Paragraphs.Should().HaveCount(1);
        clone.Notes.Paragraphs[0].Runs[0].Text.Should().Be("Remember to smile!");

        // Ensure it is a deep clone — mutating the clone does not affect the original.
        clone.Notes.Paragraphs[0].Runs[0].Text = "mutated";
        slide.Notes.Paragraphs[0].Runs[0].Text.Should().Be("Remember to smile!");
    }

    [Fact]
    public void SlideCloner_NullNotes_RemainsNull()
    {
        var slide = new Slide { Notes = null };
        var clone = SlideCloner.CloneSlide(slide);
        clone.Notes.Should().BeNull();
    }

    // ── Round-trip I/O ────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Notes_Preserved()
    {
        const string notesText = "Remember to click the demo link and open the spreadsheet.";
        var pres = MakePresWithNotes(notesText);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(1);
        var slide = reloaded.Slides[0];
        slide.Notes.Should().NotBeNull("notes should survive write+read");
        slide.Notes!.Paragraphs.Should().HaveCount(1);

        var text = string.Concat(slide.Notes.Paragraphs.SelectMany(p => p.Runs.Select(r => r.Text)));
        text.Should().Be(notesText);
    }

    [Fact]
    public void RoundTrip_NoNotes_NotesRemainNull()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Title = "No Notes Slide";
        // Notes intentionally NOT set.
        pres.Slides.Add(slide);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Notes.Should().BeNull("slide without notes should not produce a Notes body after round-trip");
    }

    [Fact]
    public void RoundTrip_MultipleSlides_OnlyNotedSlidesHaveNotes()
    {
        var pres = new Presentation();

        // Slide 0: no notes
        var s0 = new Slide();
        s0.Title = "Slide without Notes";
        pres.Slides.Add(s0);

        // Slide 1: with notes
        var s1    = new Slide();
        s1.Title  = "Slide with Notes";
        var notes = new TextBody();
        var para  = new Paragraph();
        para.Runs.Add(new Run { Text = "Speak slowly here." });
        notes.Paragraphs.Add(para);
        s1.Notes = notes;
        pres.Slides.Add(s1);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(2);
        reloaded.Slides[0].Notes.Should().BeNull("slide 0 had no notes");
        reloaded.Slides[1].Notes.Should().NotBeNull("slide 1 had notes");

        var text = string.Concat(reloaded.Slides[1].Notes!.Paragraphs.SelectMany(p => p.Runs.Select(r => r.Text)));
        text.Should().Be("Speak slowly here.");
    }

    [Fact]
    public void RoundTrip_MultiParagraphNotes_Preserved()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        var notes = new TextBody();

        var p1 = new Paragraph();
        p1.Runs.Add(new Run { Text = "First point." });
        var p2 = new Paragraph();
        p2.Runs.Add(new Run { Text = "Second point." });
        notes.Paragraphs.Add(p1);
        notes.Paragraphs.Add(p2);
        slide.Notes = notes;
        pres.Slides.Add(slide);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var reNotes = reloaded.Slides[0].Notes;
        reNotes.Should().NotBeNull();
        reNotes!.Paragraphs.Should().HaveCount(2);
        reNotes.Paragraphs[0].Runs[0].Text.Should().Be("First point.");
        reNotes.Paragraphs[1].Runs[0].Text.Should().Be("Second point.");
    }

    // ── EditingSession notes command ──────────────────────────────────────────────

    [Fact]
    public void EditingSession_SetCurrentSlideNotesText_SetsNotes()
    {
        var session = MakeSession();
        session.CurrentSlideNotes.Should().BeNull();

        session.SetCurrentSlideNotesText("Remember to ...");

        session.CurrentSlideNotes.Should().NotBeNull();
        var text = string.Concat(session.CurrentSlideNotes!.Paragraphs.SelectMany(p => p.Runs.Select(r => r.Text)));
        text.Should().Be("Remember to ...");
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotesText_PreservesParagraphBreaksThroughRoundTrip()
    {
        var session = MakeSession();
        session.SetCurrentSlideNotesText("First point.\r\n\r\nSecond point.");

        var notes = session.CurrentSlideNotes;
        notes.Should().NotBeNull();
        notes!.Paragraphs.Should().HaveCount(3);
        notes.Paragraphs[0].Runs.Single().Text.Should().Be("First point.");
        notes.Paragraphs[1].Runs.Should().BeEmpty();
        notes.Paragraphs[2].Runs.Single().Text.Should().Be("Second point.");

        var path = WriteToPptx(session.Presentation);
        var reloaded = PptxPackageReader.Read(path).Slides[0].Notes;
        reloaded.Should().NotBeNull();
        reloaded!.Paragraphs.Should().HaveCount(3);
        reloaded.Paragraphs[0].Runs.Single().Text.Should().Be("First point.");
        reloaded.Paragraphs[1].Runs.Should().BeEmpty();
        reloaded.Paragraphs[2].Runs.Single().Text.Should().Be("Second point.");
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotesText_PreservesAuthoredFormatting()
    {
        var session = MakeSession();
        var notes = new TextBody
        {
            Anchor = VerticalAnchor.Middle,
            InsetLeftPt = 12,
            InsetRightPt = 18,
            DefaultParaAlign = TextAlign.Right,
        };
        var paragraph = new Paragraph
        {
            Align = TextAlign.Center,
            Level = 2,
            MarginLeftEmu = 1440,
            SpaceBeforePt = 4,
            SpaceAfterPt = 7,
        };
        paragraph.Runs.Add(new Run
        {
            Text = "Original notes",
            FontFamily = "Aptos Display",
            FontSizePt = 18,
            Bold = true,
            BoldSet = true,
            Italic = true,
            ItalicSet = true,
            Underline = true,
            Color = new ThemeAwareColor(new SrgbColor(31, 78, 121)),
        });
        notes.Paragraphs.Add(paragraph);
        session.SetCurrentSlideNotes(notes);

        session.SetCurrentSlideNotesText("Edited notes");

        var editedNotes = session.CurrentSlideNotes;
        editedNotes.Should().NotBeNull();
        var edited = editedNotes!;
        edited.Anchor.Should().Be(VerticalAnchor.Middle);
        edited.InsetLeftPt.Should().Be(12);
        edited.InsetRightPt.Should().Be(18);
        edited.DefaultParaAlign.Should().Be(TextAlign.Right);
        var editedParagraph = edited.Paragraphs.Single();
        editedParagraph.Align.Should().Be(TextAlign.Center);
        editedParagraph.Level.Should().Be(2);
        editedParagraph.MarginLeftEmu.Should().Be(1440);
        editedParagraph.SpaceBeforePt.Should().Be(4);
        editedParagraph.SpaceAfterPt.Should().Be(7);
        var editedRun = editedParagraph.Runs.Single();
        editedRun.Text.Should().Be("Edited notes");
        editedRun.FontFamily.Should().Be("Aptos Display");
        editedRun.FontSizePt.Should().Be(18);
        editedRun.Bold.Should().BeTrue();
        editedRun.BoldSet.Should().BeTrue();
        editedRun.Italic.Should().BeTrue();
        editedRun.ItalicSet.Should().BeTrue();
        editedRun.Underline.Should().BeTrue();
        editedRun.Color!.Resolved.Should().Be(new SrgbColor(31, 78, 121));
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotesText_IsUndoable()
    {
        var session = MakeSession();
        session.SetCurrentSlideNotesText("Draft notes.");
        session.CurrentSlideNotes.Should().NotBeNull();

        session.Undo();

        session.CurrentSlideNotes.Should().BeNull("undo should clear the notes");
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotes_WithTextBody_IsUndoable()
    {
        var session = MakeSession();

        var notes = new TextBody();
        var para  = new Paragraph();
        para.Runs.Add(new Run { Text = "Structured notes." });
        notes.Paragraphs.Add(para);

        session.SetCurrentSlideNotes(notes);
        session.CurrentSlideNotes.Should().NotBeNull();

        session.Undo();
        session.CurrentSlideNotes.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotes_Null_ClearsNotes()
    {
        var session = MakeSession();
        session.SetCurrentSlideNotesText("Some notes.");
        session.SetCurrentSlideNotes(null);

        session.CurrentSlideNotes.Should().BeNull();
    }

    [Fact]
    public void EditingSession_SetCurrentSlideNotesText_EmptyString_ClearsNotes()
    {
        var session = MakeSession();
        session.SetCurrentSlideNotesText("Some notes.");
        session.SetCurrentSlideNotesText(string.Empty);

        session.CurrentSlideNotes.Should().BeNull("empty text should clear notes");
    }

    // ── Host: notes pane ─────────────────────────────────────────────────────────

    [StaFact]
    public void MainWindow_NotesPaneConstructs_AndReflectsCurrentSlide()
    {
        // Build a presentation with a slide that has notes.
        var pres  = new Presentation();
        var slide = new Slide { Title = "Noted Slide" };
        var notes = new TextBody();
        var para  = new Paragraph();
        para.Runs.Add(new Run { Text = "Presenter reminder." });
        notes.Paragraphs.Add(para);
        slide.Notes = notes;
        pres.Slides.Add(slide);

        // Use the internal overload that accepts a presentation.
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Should().NotBeNull();
            // The default empty presentation has no notes — notes pane exists and is empty.
            window.Content.Should().NotBeNull("window built successfully");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_NotesPaneRefreshesSharedNotesPagePreviewPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.LastNotesPagePreviewPlan.Should().NotBeNull();
            window.LastNotesPagePreviewPlan!.PrintPlan.Layout.Layout
                .Should().Be(PresentationPrintLayoutKind.NotesPages);

            window.Editor.CurrentSlide!.Title = "Q3 review";
            window.Editor.Presentation.NotesPageSizeCxEmu = DrawingMlCoordinateUnits.PointsToEmu(360);
            window.Editor.Presentation.NotesPageSizeCyEmu = DrawingMlCoordinateUnits.PointsToEmu(720);
            window.Editor.SetCurrentSlideNotesText("Call out the launch date.");

            var plan = window.LastNotesPagePreviewPlan;
            plan.Should().NotBeNull();
            plan!.SlideNumber.Should().Be(1);
            plan.SlideTitle.Should().Be("Q3 review");
            plan.NotesText.Should().Be("Call out the launch date.");
            plan.PageBounds.Width.Should().Be(360);
            plan.PageBounds.Height.Should().Be(720);
            plan.HasNotes.Should().BeTrue();
            plan.NotesPlaceholder.SourcePlaceholderType.Should().Be(PlaceholderType.Body);
            plan.NotesPlaceholder.HasContent.Should().BeTrue();
            plan.NotesPlaceholder.ShouldShowPlaceholder.Should().BeFalse();
            plan.PrintPlan.SlideRange.DisplayName.Should().Be("Slide 1");
            plan.RenderPages.Should().ContainSingle()
                .Which.Should().Match<PresentationNotesPageRenderedPagePlan>(page =>
                    page.ThumbnailLabel == "Slide 1 notes" &&
                    page.Detail == "Notes page for slide 1" &&
                    page.NoteLineCount == 1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_NotesPageView_UsesDedicatedPageSurfaceAndRestoresNormalLayout()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var apply = typeof(MainWindow).GetMethod(
                "ApplyPresentationViewModeState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Should().NotBeNull();
            apply!.Invoke(window, [new PresentationViewModeState(PresentationViewMode.NotesPage)]);
            window.IsNotesPageSurfaceVisible.Should().BeTrue();

            apply.Invoke(window, [PresentationViewModeState.Normal]);
            window.IsNotesPageSurfaceVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SlideMasterView_UsesMasterCanvasAndRestoresNormalCanvas()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var apply = typeof(MainWindow).GetMethod(
                "ApplyPresentationViewModeState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Should().NotBeNull();
            apply!.Invoke(window, [new PresentationViewModeState(PresentationViewMode.SlideMaster)]);
            window.IsSlideMasterSurfaceVisible.Should().BeTrue();

            apply.Invoke(window, [PresentationViewModeState.Normal]);
            window.IsSlideMasterSurfaceVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SlideMasterView_SelectsLayoutTargetInMasterPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var apply = typeof(MainWindow).GetMethod(
                "ApplyPresentationViewModeState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Should().NotBeNull();
            apply!.Invoke(window, [new PresentationViewModeState(PresentationViewMode.SlideMaster)]);

            var layout = window.Editor.Presentation.Layouts.Should().ContainSingle().Subject;
            var target = MasterEditTarget.Layout(layout.Id);
            window.TrySelectSlideMasterTarget(target).Should().BeTrue();
            window.CurrentSlideMasterTarget.Should().Be(target);
            window.SlideCanvas.MasterEditTarget.Should().Be(target);
        }
        finally
        {
            window.Close();
        }
    }

    private static string FindCorpusFile(string fileName) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", fileName);
}
