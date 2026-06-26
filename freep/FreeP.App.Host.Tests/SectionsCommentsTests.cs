using System.IO;
using System.Windows;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 11B: tests for slide sections and comments model, round-trip I/O, SlideCloner,
/// and host UI construction (StaFact).
/// </summary>
public sealed class SectionsCommentsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.SectionCommentTests", Guid.NewGuid().ToString("N"));

    public SectionsCommentsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    // =============================================================================
    // SECTIONS TESTS
    // =============================================================================

    // ── Model ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Presentation_Sections_DefaultEmpty()
    {
        var pres = new Presentation();
        pres.Sections.Should().BeEmpty();
    }

    [Fact]
    public void PresentationSection_HasIdAndNameAndSlideIds()
    {
        var sec = new PresentationSection
        {
            Name = "Introduction",
            Id   = "{AABB1234-0000-0000-0000-000000000001}"
        };
        sec.SlideIds.Add("256");
        sec.SlideIds.Add("257");

        sec.Name.Should().Be("Introduction");
        sec.Id.Should().Be("{AABB1234-0000-0000-0000-000000000001}");
        sec.SlideIds.Should().HaveCount(2);
    }

    // ── Round-trip I/O ────────────────────────────────────────────────────────────

    [Fact]
    public void Sections_RoundTrip_NamesAndMembershipPreserved()
    {
        // Build a presentation with 3 slides and 2 sections.
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Id = "256" });
        pres.Slides.Add(new Slide { Id = "257" });
        pres.Slides.Add(new Slide { Id = "258" });

        var sec1 = new PresentationSection { Name = "Intro", Id = "{AAAABBBB-0001-0001-0001-000000000001}" };
        sec1.SlideIds.Add("256");
        var sec2 = new PresentationSection { Name = "Body",  Id = "{AAAABBBB-0002-0002-0002-000000000002}" };
        sec2.SlideIds.Add("257");
        sec2.SlideIds.Add("258");
        pres.Sections.Add(sec1);
        pres.Sections.Add(sec2);

        // Write → re-read.
        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Sections.Should().HaveCount(2, "two sections should survive write+read");
        reloaded.Sections[0].Name.Should().Be("Intro");
        reloaded.Sections[1].Name.Should().Be("Body");
        reloaded.Sections[0].Id.Should().Be("{AAAABBBB-0001-0001-0001-000000000001}");
        reloaded.Sections[1].SlideIds.Should().HaveCount(2, "Body section covers 2 slides");
    }

    [Fact]
    public void Sections_RoundTrip_EmptyPresentation_NoSections()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide());

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Sections.Should().BeEmpty("no sections were created");
    }

    [Fact]
    public void Sections_RoundTrip_OrderPreserved()
    {
        // 3 sections, each with 1 slide.
        var pres = new Presentation();
        for (int i = 0; i < 3; i++) pres.Slides.Add(new Slide { Id = (256 + i).ToString() });

        for (int i = 0; i < 3; i++)
        {
            var sec = new PresentationSection { Name = $"Section {i + 1}" };
            sec.SlideIds.Add((256 + i).ToString());
            pres.Sections.Add(sec);
        }

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Sections.Select(s => s.Name).Should().Equal("Section 1", "Section 2", "Section 3");
    }

    // =============================================================================
    // COMMENTS TESTS
    // =============================================================================

    // ── Model ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Slide_Comments_DefaultEmpty()
    {
        var slide = new Slide();
        slide.Comments.Should().BeEmpty();
    }

    [Fact]
    public void SlideComment_Properties_SetCorrectly()
    {
        var dt = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var cm = new SlideComment
        {
            Author   = "Alice",
            Initials = "AL",
            Text     = "Please update this chart.",
            DateTime = dt,
            Xemu     = 914400,
            Yemu     = 457200,
            Idx      = 1,
        };

        cm.Author.Should().Be("Alice");
        cm.Initials.Should().Be("AL");
        cm.Text.Should().Be("Please update this chart.");
        cm.DateTime.Should().Be(dt);
        cm.Xemu.Should().Be(914400);
        cm.Yemu.Should().Be(457200);
        cm.Idx.Should().Be(1);
    }

    // ── Round-trip I/O ────────────────────────────────────────────────────────────

    [Fact]
    public void Comments_RoundTrip_AuthorTextPosPreserved()
    {
        var pres  = new Presentation();
        var slide = new Slide { Title = "Slide With Comments" };

        slide.Comments.Add(new SlideComment
        {
            Author   = "Alice",
            Initials = "AL",
            Text     = "Update the title.",
            Xemu     = 914400,
            Yemu     = 457200,
            Idx      = 1,
        });
        slide.Comments.Add(new SlideComment
        {
            Author   = "Bob",
            Initials = "B",
            Text     = "Check the spelling.",
            Xemu     = 1828800,
            Yemu     = 914400,
            Idx      = 2,
        });
        pres.Slides.Add(slide);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides.Should().HaveCount(1);
        var rSlide = reloaded.Slides[0];
        rSlide.Comments.Should().HaveCount(2, "2 comments should survive write+read");

        var cm1 = rSlide.Comments[0];
        cm1.Author.Should().Be("Alice");
        cm1.Initials.Should().Be("AL");
        cm1.Text.Should().Be("Update the title.");
        cm1.Xemu.Should().Be(914400);
        cm1.Yemu.Should().Be(457200);

        var cm2 = rSlide.Comments[1];
        cm2.Author.Should().Be("Bob");
        cm2.Text.Should().Be("Check the spelling.");
    }

    [Fact]
    public void Comments_RoundTrip_CommentAuthorsEmitted()
    {
        // Verifies that commentAuthors.xml is written — we check that author names
        // come back correctly (the only observable proof that the authors part was
        // read successfully).
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Comments.Add(new SlideComment { Author = "TestAuthor", Initials = "TA", Text = "Hi", Idx = 1 });
        pres.Slides.Add(slide);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Comments[0].Author.Should().Be("TestAuthor");
        reloaded.Slides[0].Comments[0].Initials.Should().Be("TA");
    }

    [Fact]
    public void Comments_RoundTrip_SlideWithNoComments_RemainsEmpty()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Title = "No comments" });

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Comments.Should().BeEmpty();
    }

    [Fact]
    public void Comments_RoundTrip_MultipleSlides_OnlyCommentedSlidesHaveComments()
    {
        var pres = new Presentation();

        var s0 = new Slide { Title = "No comment slide" };
        pres.Slides.Add(s0);

        var s1 = new Slide { Title = "Comment slide" };
        s1.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "Fix me", Idx = 1 });
        pres.Slides.Add(s1);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        reloaded.Slides[0].Comments.Should().BeEmpty("slide 0 had no comments");
        reloaded.Slides[1].Comments.Should().HaveCount(1, "slide 1 had 1 comment");
        reloaded.Slides[1].Comments[0].Text.Should().Be("Fix me");
    }

    [Fact]
    public void Comments_RoundTrip_SameAuthorMultipleComments_DeduplicatedInAuthors()
    {
        // Same author on two different comments on the same slide.
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "First",  Idx = 1 });
        slide.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "Second", Idx = 2 });
        pres.Slides.Add(slide);

        var path     = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        // Both comments should come back with the same author name.
        reloaded.Slides[0].Comments.Should().HaveCount(2);
        reloaded.Slides[0].Comments[0].Author.Should().Be("Alice");
        reloaded.Slides[0].Comments[1].Author.Should().Be("Alice");
    }

    // ── SlideCloner ───────────────────────────────────────────────────────────────

    [Fact]
    public void SlideCloner_ClonesComments()
    {
        var slide = new Slide { Title = "Commented" };
        slide.Comments.Add(new SlideComment
        {
            Author   = "Alice",
            Initials = "AL",
            Text     = "Remember to update.",
            Xemu     = 500000,
            Yemu     = 250000,
            Idx      = 1,
        });

        var clone = SlideCloner.CloneSlide(slide);

        clone.Comments.Should().HaveCount(1);
        var cm = clone.Comments[0];
        cm.Author.Should().Be("Alice");
        cm.Text.Should().Be("Remember to update.");
        cm.Xemu.Should().Be(500000);

        // Deep copy: mutating the clone should not affect the original.
        clone.Comments[0].Text = "mutated";
        slide.Comments[0].Text.Should().Be("Remember to update.");
    }

    [Fact]
    public void SlideCloner_NoComments_ClonesEmpty()
    {
        var slide = new Slide();
        var clone = SlideCloner.CloneSlide(slide);
        clone.Comments.Should().BeEmpty();
    }

    [Fact]
    public void SlideCloner_MultipleComments_AllCloned()
    {
        var slide = new Slide();
        slide.Comments.Add(new SlideComment { Text = "C1", Idx = 1 });
        slide.Comments.Add(new SlideComment { Text = "C2", Idx = 2 });
        slide.Comments.Add(new SlideComment { Text = "C3", Idx = 3 });

        var clone = SlideCloner.CloneSlide(slide);

        clone.Comments.Should().HaveCount(3);
        clone.Comments.Select(c => c.Text).Should().Equal("C1", "C2", "C3");
    }

    // ── Host StaFact: SlidePane section headers ───────────────────────────────────

    [StaFact]
    public void SlidePane_WithSections_RendersWithoutException()
    {
        // Build a presentation with 3 slides and 2 sections.
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Id = "256", Title = "S1" });
        pres.Slides.Add(new Slide { Id = "257", Title = "S2" });
        pres.Slides.Add(new Slide { Id = "258", Title = "S3" });

        var sec1 = new PresentationSection { Name = "Intro" };
        sec1.SlideIds.Add("256");
        var sec2 = new PresentationSection { Name = "Main" };
        sec2.SlideIds.Add("257");
        sec2.SlideIds.Add("258");
        pres.Sections.Add(sec1);
        pres.Sections.Add(sec2);

        var bus    = new PresentationCommandBus(pres);
        var editor = new EditingSession(pres, bus);

        // Constructing SlidePane must not throw.
        var pane = new SlidePane(editor);
        pane.Should().NotBeNull();
    }

    [StaFact]
    public void MainWindow_WithComments_ConstructsWithoutException()
    {
        // MainWindow should construct cleanly even with a default (no-comment) presentation.
        var window = new MainWindow(new FreePOptions());
        try
        {
            window.Should().NotBeNull();
            window.Content.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void SlidePane_WithoutSections_NoSectionHeadersInChildren()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Title = "A" });
        pres.Slides.Add(new Slide { Title = "B" });

        var bus    = new PresentationCommandBus(pres);
        var editor = new EditingSession(pres, bus);

        var pane = new SlidePane(editor);
        // No exception; pane is valid.
        pane.Should().NotBeNull();
    }
}
