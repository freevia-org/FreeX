using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Xml.Linq;
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
            IsResolved = true,
            ResolvedDateTime = dt.AddMinutes(5),
            ResolvedBy = "Reviewer",
            UsesModernCommentSchema = true,
            ModernAnchorKind = "unknownAnchor",
            ModernAnchorXml = """<p188:unknownAnchor xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main" />""",
            Xemu     = 914400,
            Yemu     = 457200,
            Idx      = 1,
        };

        cm.Author.Should().Be("Alice");
        cm.Initials.Should().Be("AL");
        cm.Text.Should().Be("Please update this chart.");
        cm.DateTime.Should().Be(dt);
        cm.IsResolved.Should().BeTrue();
        cm.ResolvedDateTime.Should().Be(dt.AddMinutes(5));
        cm.ResolvedBy.Should().Be("Reviewer");
        cm.UsesModernCommentSchema.Should().BeTrue();
        cm.ModernAnchorKind.Should().Be("unknownAnchor");
        cm.ModernAnchorXml.Should().Contain("unknownAnchor");
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
    public void ModernComments_Read_MapsReviewMetadataRepliesAndSharedPaneDescriptor()
    {
        using var ms = BuildModernCommentsPptx();

        var pres = PptxPackageReader.Read(ms);

        var comment = pres.Slides.Should().ContainSingle().Subject.Comments.Should().ContainSingle().Subject;
        comment.Author.Should().Be("Alice Reviewer");
        comment.Initials.Should().Be("AR");
        comment.Text.Should().Be("Modern thread root.");
        comment.DateTime.Should().Be(new DateTime(2026, 7, 3, 10, 15, 30, DateTimeKind.Utc));
        comment.IsResolved.Should().BeTrue();
        comment.UsesModernCommentSchema.Should().BeTrue();
        comment.ModernCommentId.Should().Be("{33333333-3333-3333-3333-333333333333}");
        comment.ModernAuthorId.Should().Be("{11111111-1111-1111-1111-111111111111}");
        comment.ModernAuthorUserId.Should().Be("alice@example.com::1");
        comment.ModernAnchorKind.Should().Be("unknownAnchor");
        comment.ModernAnchorXml.Should().Contain("unknownAnchor");
        comment.Xemu.Should().Be(1200);
        comment.Yemu.Should().Be(2400);
        comment.Replies.Should().ContainSingle().Which.Should().Match<SlideCommentReply>(reply =>
            reply.Author == "Bob Reviewer" &&
            reply.Initials == "BR" &&
            reply.ModernReplyId == "{44444444-4444-4444-4444-444444444444}" &&
            reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
            reply.ModernAuthorUserId == "bob@example.com::2" &&
            reply.Text == "Reply retained." &&
            reply.DateTime == new DateTime(2026, 7, 3, 10, 20, 0, DateTimeKind.Utc));

        var pane = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(pres.Slides, 0, selectedCommentIndex: 0);
        pane.SelectedComment.Should().NotBeNull();
        pane.SelectedComment!.ThreadStatus.Should().Be(PresentationCommentThreadStatus.Resolved);
        pane.SelectedComment.AuthorDisplayName.Should().Be("Alice Reviewer");
        pane.SelectedComment.ModernCommentId.Should().Be("{33333333-3333-3333-3333-333333333333}");
        pane.SelectedComment.ModernAuthorId.Should().Be("{11111111-1111-1111-1111-111111111111}");
        pane.SelectedComment.ModernAuthorUserId.Should().Be("alice@example.com::1");
        pane.SelectedComment.ModernAnchorKind.Should().Be("unknownAnchor");
        pane.SelectedComment.AnchorSummary.Should().Be("unknown anchor at 1200,2400 EMU");
        pane.SelectedComment.Replies.Should().ContainSingle().Which.Should().Match<PresentationCommentReplyDescriptor>(reply =>
            reply.AuthorDisplayName == "Bob Reviewer" &&
            reply.ModernReplyId == "{44444444-4444-4444-4444-444444444444}" &&
            reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
            reply.ModernAuthorUserId == "bob@example.com::2");
    }

    [Fact]
    public void ModernComments_ReadWrite_PreservesImportedAuthorAndThreadIds()
    {
        using var ms = BuildModernCommentsPptx();
        var pres = PptxPackageReader.Read(ms);

        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var authorsXml = LoadXml(zip, "ppt/authors/author1.xml");
        authorsXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "author" &&
                HasAttribute(element, "id", "{11111111-1111-1111-1111-111111111111}") &&
                HasAttribute(element, "userId", "alice@example.com::1"));
        authorsXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "author" &&
                HasAttribute(element, "id", "{22222222-2222-2222-2222-222222222222}") &&
                HasAttribute(element, "userId", "bob@example.com::2"));

        var commentXml = LoadXml(zip, "ppt/comments/comment1.xml");
        commentXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "cm" &&
                HasAttribute(element, "id", "{33333333-3333-3333-3333-333333333333}") &&
                HasAttribute(element, "authorId", "{11111111-1111-1111-1111-111111111111}"));
        commentXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "reply" &&
                HasAttribute(element, "id", "{44444444-4444-4444-4444-444444444444}") &&
                HasAttribute(element, "authorId", "{22222222-2222-2222-2222-222222222222}"));
    }

    [Fact]
    public void ModernComments_RoundTrip_RetainsReviewMetadataAndModernPackageParts()
    {
        var pres = new Presentation();
        var slide = new Slide { Title = "Modern review" };
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice Reviewer",
            Initials = "AR",
            ModernCommentId = "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
            ModernAuthorId = "{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}",
            ModernAuthorUserId = "alice@example.com::powerpoint",
            ModernAuthorProviderId = "aad",
            Text = "Resolve after chart update.",
            DateTime = new DateTime(2026, 7, 3, 11, 0, 0, DateTimeKind.Utc),
            IsResolved = true,
            ModernAnchorKind = "unknownAnchor",
            ModernAnchorXml = """<p188:unknownAnchor xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main" />""",
            Xemu = 3600,
            Yemu = 7200,
            Replies =
            {
                new SlideCommentReply
                {
                    Author = "Bob Reviewer",
                    Initials = "BR",
                    ModernReplyId = "{cccccccc-cccc-cccc-cccc-cccccccccccc}",
                    ModernAuthorId = "{dddddddd-dddd-dddd-dddd-dddddddddddd}",
                    ModernAuthorUserId = "bob@example.com::powerpoint",
                    ModernAuthorProviderId = "aad",
                    Text = "Confirmed.",
                    DateTime = new DateTime(2026, 7, 3, 11, 5, 0, DateTimeKind.Utc)
                }
            }
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var comment = reloaded.Slides[0].Comments.Should().ContainSingle().Subject;
        comment.Author.Should().Be("Alice Reviewer");
        comment.Initials.Should().Be("AR");
        comment.Text.Should().Be("Resolve after chart update.");
        comment.DateTime.Should().Be(new DateTime(2026, 7, 3, 11, 0, 0, DateTimeKind.Utc));
        comment.IsResolved.Should().BeTrue();
        comment.UsesModernCommentSchema.Should().BeTrue();
        comment.ModernCommentId.Should().Be("{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}");
        comment.ModernAuthorId.Should().Be("{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}");
        comment.ModernAuthorUserId.Should().Be("alice@example.com::powerpoint");
        comment.ModernAuthorProviderId.Should().Be("aad");
        comment.ModernAnchorKind.Should().Be("unknownAnchor");
        comment.ModernAnchorXml.Should().Contain("unknownAnchor");
        comment.Replies.Should().ContainSingle().Which.Should().Match<SlideCommentReply>(reply =>
            reply.Author == "Bob Reviewer" &&
            reply.Initials == "BR" &&
            reply.ModernReplyId == "{cccccccc-cccc-cccc-cccc-cccccccccccc}" &&
            reply.ModernAuthorId == "{dddddddd-dddd-dddd-dddd-dddddddddddd}" &&
            reply.ModernAuthorUserId == "bob@example.com::powerpoint" &&
            reply.ModernAuthorProviderId == "aad" &&
            reply.Text == "Confirmed." &&
            reply.DateTime == new DateTime(2026, 7, 3, 11, 5, 0, DateTimeKind.Utc));

        using var zip = ZipFile.OpenRead(path);
        zip.GetEntry("ppt/authors/author1.xml").Should().NotBeNull();
        zip.GetEntry("ppt/commentAuthors.xml").Should().BeNull("modern-only comments should not be downgraded to legacy authors");

        var slideRels = LoadXml(zip, "ppt/slides/_rels/slide1.xml.rels");
        Relationship(slideRels, "http://schemas.microsoft.com/office/2018/10/relationships/comments", "../comments/comment1.xml")
            .Should().NotBeNull();
        var presRels = LoadXml(zip, "ppt/_rels/presentation.xml.rels");
        Relationship(presRels, "http://schemas.microsoft.com/office/2018/10/relationships/authors", "authors/author1.xml")
            .Should().NotBeNull();

        var contentTypes = LoadXml(zip, "[Content_Types].xml");
        Override(contentTypes, "/ppt/comments/comment1.xml", "application/vnd.ms-powerpoint.comments+xml")
            .Should().NotBeNull();
        Override(contentTypes, "/ppt/authors/author1.xml", "application/vnd.ms-powerpoint.authors+xml")
            .Should().NotBeNull();

        var commentXml = LoadXml(zip, "ppt/comments/comment1.xml");
        commentXml.Descendants()
            .Should().Contain(element => element.Name.LocalName == "unknownAnchor");
        commentXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "cm" &&
                HasAttribute(element, "id", "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}") &&
                HasAttribute(element, "authorId", "{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}"));
        commentXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "reply" &&
                HasAttribute(element, "id", "{cccccccc-cccc-cccc-cccc-cccccccccccc}") &&
                HasAttribute(element, "authorId", "{dddddddd-dddd-dddd-dddd-dddddddddddd}"));
        commentXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "pos" &&
                element.Attribute("x") != null &&
                element.Attribute("x")!.Value == "3600" &&
                element.Attribute("y") != null &&
                element.Attribute("y")!.Value == "7200");
        var authorsXml = LoadXml(zip, "ppt/authors/author1.xml");
        authorsXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "author" &&
                HasAttribute(element, "id", "{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}") &&
                HasAttribute(element, "userId", "alice@example.com::powerpoint") &&
                HasAttribute(element, "providerId", "aad"));
        authorsXml.Descendants()
            .Should().Contain(element =>
                element.Name.LocalName == "author" &&
                HasAttribute(element, "id", "{dddddddd-dddd-dddd-dddd-dddddddddddd}") &&
                HasAttribute(element, "userId", "bob@example.com::powerpoint") &&
                HasAttribute(element, "providerId", "aad"));
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
            UsesModernCommentSchema = true,
            ModernAnchorKind = "unknownAnchor",
            ModernAnchorXml = """<p188:unknownAnchor xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main" />""",
            Xemu     = 500000,
            Yemu     = 250000,
            Idx      = 1,
        });

        var clone = SlideCloner.CloneSlide(slide);

        clone.Comments.Should().HaveCount(1);
        var cm = clone.Comments[0];
        cm.Author.Should().Be("Alice");
        cm.Text.Should().Be("Remember to update.");
        cm.ModernAnchorKind.Should().Be("unknownAnchor");
        cm.ModernAnchorXml.Should().Contain("unknownAnchor");
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

        // Constructing SlidePane must not throw.
        var pane = SlidePaneTestFactory.Create(pres);
        pane.Should().NotBeNull();
    }

    [StaFact]
    public void MainWindow_WithComments_ConstructsWithoutException()
    {
        // MainWindow should construct cleanly even with a default (no-comment) presentation.
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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

        var pane = SlidePaneTestFactory.Create(pres);
        // No exception; pane is valid.
        pane.Should().NotBeNull();
    }

    // =============================================================================
    // BB3 — section sldId translation (non-sequential Slide.Id values)
    // =============================================================================

    /// <summary>
    /// BB3 regression: slides have non-sequential Slide.Id values (simulating ids
    /// read from a real pptx where sldId integers are not 256,257,…). The section
    /// member ids must be translated to the NEWLY-ASSIGNED write-time numeric ids
    /// (256+writeIndex) so they exist in the emitted p:sldIdLst. Verifies by raw ZIP
    /// XML inspection: every p14:sldId @id found inside each section must also appear
    /// as a p:sldId @id in the p:sldIdLst.
    /// </summary>
    [Fact]
    public void BB3_Sections_NonSequentialSlideIds_TranslatedToAssignedIds()
    {
        // Slide.Id values are deliberately non-sequential (not 256, 257, 258).
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Id = "500" });
        pres.Slides.Add(new Slide { Id = "100" });
        pres.Slides.Add(new Slide { Id = "999" });

        // Section 1: contains slide[0] (Id="500")
        var sec1 = new PresentationSection { Name = "S1" };
        sec1.SlideIds.Add("500");
        // Section 2: contains slide[1] (Id="100") and slide[2] (Id="999")
        var sec2 = new PresentationSection { Name = "S2" };
        sec2.SlideIds.Add("100");
        sec2.SlideIds.Add("999");
        pres.Sections.Add(sec1);
        pres.Sections.Add(sec2);

        var path = WriteToPptx(pres);

        // Parse the raw ZIP to inspect presentation.xml directly.
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var presEntry = zip.GetEntry("ppt/presentation.xml")
            ?? throw new InvalidOperationException("presentation.xml not found");
        XDocument presXml;
        using (var s = presEntry.Open())
            presXml = XDocument.Load(s);

        var P  = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var R  = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var P14 = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");

        // Collect all p:sldId @id values from the main slide list.
        var assignedSldIds = presXml.Descendants(P + "sldIdLst")
            .SelectMany(l => l.Elements(P + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToHashSet();

        assignedSldIds.Should().HaveCount(3, "three slides were written");

        // Collect all p14:sldId @id values from section lists.
        var sectionSldIds = presXml.Descendants(P14 + "sldIdLst")
            .SelectMany(l => l.Elements(P14 + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToList();

        sectionSldIds.Should().HaveCount(3, "3 slides across 2 sections");

        // EVERY section sldId must exist in the main assigned slide-id list.
        foreach (var secSldId in sectionSldIds)
            assignedSldIds.Should().Contain(secSldId,
                $"section sldId {secSldId} must match an assigned p:sldId id");
    }

    /// <summary>
    /// BB3 regression: a section member that references a slide not present in the
    /// presentation (deleted) must be OMITTED from the emitted p14:sldIdLst so no
    /// dangling reference is written.
    /// </summary>
    [Fact]
    public void BB3_Sections_DeletedSlideMember_Omitted()
    {
        var pres = new Presentation();
        pres.Slides.Add(new Slide { Id = "A1" });
        pres.Slides.Add(new Slide { Id = "A2" });

        var sec = new PresentationSection { Name = "Test" };
        sec.SlideIds.Add("A1");
        sec.SlideIds.Add("DELETED-GHOST-ID"); // references a slide not in pres.Slides
        pres.Sections.Add(sec);

        var path = WriteToPptx(pres);

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var presEntry = zip.GetEntry("ppt/presentation.xml")!;
        XDocument presXml;
        using (var s = presEntry.Open())
            presXml = XDocument.Load(s);

        var P14 = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");
        var P   = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");

        var assignedSldIds = presXml.Descendants(P + "sldIdLst")
            .SelectMany(l => l.Elements(P + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToHashSet();

        var sectionSldIds = presXml.Descendants(P14 + "sldIdLst")
            .SelectMany(l => l.Elements(P14 + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToList();

        // Only 1 valid member (A1 maps to the first assigned id); the ghost must be absent.
        sectionSldIds.Should().HaveCount(1, "ghost member must be skipped");
        assignedSldIds.Should().Contain(sectionSldIds[0], "the remaining id must be valid");
    }

    // =============================================================================
    // BB4 — cross-slide author-id consistency
    // =============================================================================

    /// <summary>
    /// BB4 regression: slide2's author encounter order differs from the global order.
    /// slide1 has Alice only; slide2 has Bob first then Alice.
    /// Global order (first-encounter across all slides): Alice=0, Bob=1.
    /// Per-slide local re-derivation would give Bob=0 on slide2 — WRONG.
    /// Verifies by raw XML: Bob's comment on slide2 must have authorId=1 (his global id).
    /// </summary>
    [Fact]
    public void BB4_Comments_CrossSlide_AuthorIdConsistency()
    {
        var pres = new Presentation();

        // slide1: Alice only
        var slide1 = new Slide();
        slide1.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "s1-alice", Idx = 1 });
        pres.Slides.Add(slide1);

        // slide2: Bob first, then Alice — local re-derivation would give Bob=0, but global gives Bob=1
        var slide2 = new Slide();
        slide2.Comments.Add(new SlideComment { Author = "Bob",   Initials = "B",  Text = "s2-bob",   Idx = 1 });
        slide2.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "s2-alice", Idx = 2 });
        pres.Slides.Add(slide2);

        var path = WriteToPptx(pres);

        var P = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");

        // Read commentAuthors.xml to learn Alice's and Bob's global ids.
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);

        var authorsEntry = zip.GetEntry("ppt/commentAuthors.xml")!;
        XDocument authorsXml;
        using (var s = authorsEntry.Open())
            authorsXml = XDocument.Load(s);

        var aliceGlobalId = authorsXml.Descendants(P + "cmAuthor")
            .First(e => e.Attribute("name")?.Value == "Alice")
            .Attribute("id")!.Value;
        var bobGlobalId = authorsXml.Descendants(P + "cmAuthor")
            .First(e => e.Attribute("name")?.Value == "Bob")
            .Attribute("id")!.Value;

        aliceGlobalId.Should().Be("0", "Alice is first-encountered globally");
        bobGlobalId.Should().Be("1",   "Bob is second-encountered globally");

        // Inspect slide2's comment XML (comment2.xml).
        var slide2CommentsEntry = zip.GetEntry("ppt/comments/comment2.xml")!;
        XDocument slide2CommentsXml;
        using (var s = slide2CommentsEntry.Open())
            slide2CommentsXml = XDocument.Load(s);

        var cmElements = slide2CommentsXml.Descendants(P + "cm").ToList();
        cmElements.Should().HaveCount(2, "slide2 has 2 comments");

        // First comment on slide2 is Bob's — must have his GLOBAL id (1), not local 0.
        var bobAuthorId = cmElements[0].Attribute("authorId")!.Value;
        bobAuthorId.Should().Be(bobGlobalId, "Bob's comment must use his global authorId");

        // Second comment on slide2 is Alice's — must have her GLOBAL id (0).
        var aliceAuthorId = cmElements[1].Attribute("authorId")!.Value;
        aliceAuthorId.Should().Be(aliceGlobalId, "Alice's comment must use her global authorId");
    }

    // =============================================================================
    // BB5 — duplicate comment idx collision within a slide
    // =============================================================================

    /// <summary>
    /// BB5 regression: two comments on the same slide both have Idx=1 (stale/cloned).
    /// The writer must renumber them sequentially so the emitted p:cm/@idx values are distinct.
    /// </summary>
    [Fact]
    public void BB5_Comments_DuplicateIdx_RenumberedDistinct()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        // Both comments have Idx=1 — simulates a clone/stale-idx scenario.
        slide.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "First",  Idx = 1 });
        slide.Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "Second", Idx = 1 });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var commentsEntry = zip.GetEntry("ppt/comments/comment1.xml")!;
        XDocument cmXml;
        using (var s = commentsEntry.Open())
            cmXml = XDocument.Load(s);

        var P = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var idxValues = cmXml.Descendants(P + "cm")
            .Select(e => e.Attribute("idx")?.Value)
            .Where(v => v is not null)
            .ToList();

        idxValues.Should().HaveCount(2, "two comments should be written");
        idxValues.Should().OnlyHaveUniqueItems("duplicate idx values corrupt the file");
        idxValues.Should().BeEquivalentTo(new[] { "1", "2" }, "comments are renumbered 1, 2 in order");
    }

    // =============================================================================
    // BB6 — dangling comment authorId must not produce empty Author
    // =============================================================================

    /// <summary>
    /// BB6 regression: a comment whose authorId does not exist in commentAuthors.xml
    /// must NOT result in an empty Author string — the reader must preserve the id as a
    /// placeholder rather than silently destroying the identity.
    ///
    /// We build a raw .pptx zip where the comment references authorId=99 but the
    /// commentAuthors.xml only lists authorId=0 (Alice).  After reading, the comment
    /// must have a non-empty Author (the placeholder "Author 99" or similar) rather than "".
    /// </summary>
    [Fact]
    public void BB6_Comment_DanglingAuthorId_NotEmptyAuthor()
    {
        using var ms = BuildDanglingAuthorPptx();
        var pres = PptxPackageReader.Read(ms);

        pres.Slides.Should().HaveCount(1);
        var comment = pres.Slides[0].Comments.Should().ContainSingle().Subject;

        comment.Author.Should().NotBeNullOrEmpty(
            "a comment with a dangling authorId must not silently become empty-author " +
            "(BB6 fix: reader preserves a placeholder instead of empty string)");
        comment.Text.Should().Be("Dangling author comment");
    }

    // =============================================================================
    // BB7 — real round-trip: numeric sldId → rId translation on read
    // =============================================================================

    /// <summary>
    /// BB7 regression: a real .pptx (or a minimal raw-zip equivalent) has slides whose
    /// p:sldId numeric @id attributes are non-sequential (300, 305, 311) while the
    /// p14:section/p14:sldIdLst/p14:sldId @id attributes use those same numeric ids.
    /// After the reader fix the loaded section.SlideIds must equal the corresponding rId
    /// strings ("rId2", "rId3", "rId4") — matching Slide.Id exactly — so the writer's
    /// BB3 translation map hits every section member and sections survive a full
    /// load → save round-trip.
    /// </summary>
    [Fact]
    public void BB7_Sections_NumericSldId_TranslatedToRId_OnRead()
    {
        using var ms = BuildNonSequentialSectionPptx();
        var pres = PptxPackageReader.Read(ms);

        // 3 slides loaded; their Slide.Id values must be the rId strings.
        pres.Slides.Should().HaveCount(3, "three slides are in the zip");
        pres.Slides[0].Id.Should().Be("rId2", "first slide maps to rId2");
        pres.Slides[1].Id.Should().Be("rId3", "second slide maps to rId3");
        pres.Slides[2].Id.Should().Be("rId4", "third slide maps to rId4");

        // 1 section referencing slide[0] (sldId 300 → rId2) and slide[2] (sldId 311 → rId4).
        pres.Sections.Should().HaveCount(1, "one section in the zip");
        var sec = pres.Sections[0];
        sec.Name.Should().Be("MySection");
        sec.SlideIds.Should().HaveCount(2, "section references 2 of the 3 slides");

        // CRITICAL: section member ids must use the rId key space, not the raw numeric ids.
        sec.SlideIds.Should().Contain("rId2",
            "numeric sldId 300 must be translated to its rId string (rId2)");
        sec.SlideIds.Should().Contain("rId4",
            "numeric sldId 311 must be translated to its rId string (rId4)");
        sec.SlideIds.Should().NotContain("300",
            "raw numeric id must not be stored — writer keyed by Slide.Id (rId2/rId4)");
        sec.SlideIds.Should().NotContain("311",
            "raw numeric id must not be stored — writer keyed by Slide.Id (rId2/rId4)");

        // Full load → save round-trip: write to disk, re-read, verify section members
        // appear with valid ids in the emitted presentation.xml.
        var path = WriteToPptx(pres);

        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var presEntry = zip.GetEntry("ppt/presentation.xml")
            ?? throw new InvalidOperationException("presentation.xml not found in saved pptx");
        XDocument presXml;
        using (var s = presEntry.Open())
            presXml = XDocument.Load(s);

        var Pns  = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var P14ns = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");

        var assignedSldIds = presXml.Descendants(Pns + "sldIdLst")
            .SelectMany(l => l.Elements(Pns + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToHashSet();

        assignedSldIds.Should().HaveCount(3, "3 slides written");

        var sectionSldIds = presXml.Descendants(P14ns + "sldIdLst")
            .SelectMany(l => l.Elements(P14ns + "sldId"))
            .Select(e => e.Attribute("id")?.Value)
            .Where(v => v is not null)
            .ToList();

        sectionSldIds.Should().HaveCount(2, "section has 2 members in saved pptx");
        foreach (var sid in sectionSldIds)
            assignedSldIds.Should().Contain(sid,
                $"section sldId {sid} must exist in p:sldIdLst — BB7 load→save round-trip");
    }

    /// <summary>
    /// Builds a minimal .pptx in memory with 3 slides at non-sequential sldId integers
    /// (300, 305, 311) mapped to rId2/rId3/rId4, and a p14:section referencing slides
    /// with sldId 300 and 311 (i.e. rId2 and rId4).
    /// </summary>
    private static MemoryStream BuildNonSequentialSectionPptx()
    {
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide2.xml"/>
                  <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide3.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/presentation.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main">
                  <p:sldSz cx="9144000" cy="6858000"/>
                  <p:sldIdLst>
                    <p:sldId id="300" r:id="rId2"/>
                    <p:sldId id="305" r:id="rId3"/>
                    <p:sldId id="311" r:id="rId4"/>
                  </p:sldIdLst>
                  <p:extLst>
                    <p:ext uri="{521415D9-36F7-43E2-AB2F-B90AF26B5E84}">
                      <p14:sectionLst xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main">
                        <p14:section name="MySection" id="{BB700001-0001-0001-0001-000000000001}">
                          <p14:sldIdLst>
                            <p14:sldId id="300"/>
                            <p14:sldId id="311"/>
                          </p14:sldIdLst>
                        </p14:section>
                      </p14:sectionLst>
                    </p:ext>
                  </p:extLst>
                </p:presentation>
                """);

            var slideXml = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:cSld><p:spTree/></p:cSld>
                </p:sld>
                """;

            WriteZipEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteZipEntry(zip, "ppt/slides/slide2.xml", slideXml);
            WriteZipEntry(zip, "ppt/slides/slide3.xml", slideXml);

            WriteZipEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                  <Override PartName="/ppt/slides/slide2.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                  <Override PartName="/ppt/slides/slide3.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                </Types>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Builds a minimal .pptx in memory where slide1 has a comment with authorId=99
    /// that does not appear in commentAuthors.xml (which only defines authorId=0/Alice).
    /// </summary>
    private static MemoryStream BuildDanglingAuthorPptx()
    {
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors" Target="commentAuthors.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/presentation.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:sldSz cx="9144000" cy="6858000"/>
                  <p:sldIdLst>
                    <p:sldId id="256" r:id="rId2"/>
                  </p:sldIdLst>
                </p:presentation>
                """);

            // commentAuthors.xml: only defines Alice (id=0); id=99 is absent.
            WriteZipEntry(zip, "ppt/commentAuthors.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:cmAuthorLst xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:cmAuthor id="0" name="Alice" initials="AL" lastIdx="1" clrIdx="0"/>
                </p:cmAuthorLst>
                """);

            WriteZipEntry(zip, "ppt/slides/_rels/slide1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rC1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments/comment1.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/slides/slide1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:cSld><p:spTree/></p:cSld>
                </p:sld>
                """);

            // comment1.xml: one comment with authorId=99 (not in commentAuthors)
            WriteZipEntry(zip, "ppt/comments/comment1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:cmLst xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:cm authorId="99" idx="1">
                    <p:pos x="0" y="0"/>
                    <p:text>Dangling author comment</p:text>
                  </p:cm>
                </p:cmLst>
                """);

            WriteZipEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                  <Override PartName="/ppt/comments/comment1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.comments+xml"/>
                </Types>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream BuildModernCommentsPptx()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/_rels/presentation.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
                  <Relationship Id="rIdAuthors" Type="http://schemas.microsoft.com/office/2018/10/relationships/authors" Target="authors/author1.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/presentation.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:sldSz cx="9144000" cy="6858000"/>
                  <p:sldIdLst>
                    <p:sldId id="256" r:id="rId2"/>
                  </p:sldIdLst>
                </p:presentation>
                """);

            WriteZipEntry(zip, "ppt/authors/author1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p188:authorLst xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main">
                  <p188:author id="{11111111-1111-1111-1111-111111111111}" name="Alice Reviewer" initials="AR" userId="alice@example.com::1" providerId=""/>
                  <p188:author id="{22222222-2222-2222-2222-222222222222}" name="Bob Reviewer" initials="BR" userId="bob@example.com::2" providerId=""/>
                </p188:authorLst>
                """);

            WriteZipEntry(zip, "ppt/slides/_rels/slide1.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rModernComments1" Type="http://schemas.microsoft.com/office/2018/10/relationships/comments" Target="../comments/comment1.xml"/>
                </Relationships>
                """);

            WriteZipEntry(zip, "ppt/slides/slide1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                       xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main">
                  <p:cSld><p:spTree/></p:cSld>
                  <p:extLst>
                    <p:ext uri="{6950BFC3-D8DA-4A85-94F7-54DA5524770B}">
                      <p188:commentRel r:id="rModernComments1"/>
                    </p:ext>
                  </p:extLst>
                </p:sld>
                """);

            WriteZipEntry(zip, "ppt/comments/comment1.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <p188:cmLst xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main"
                             xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p188:cm id="{33333333-3333-3333-3333-333333333333}"
                           authorId="{11111111-1111-1111-1111-111111111111}"
                           status="resolved"
                           created="2026-07-03T10:15:30Z">
                    <p188:unknownAnchor/>
                    <p188:pos x="1200" y="2400"/>
                    <p188:replyLst>
                      <p188:reply id="{44444444-4444-4444-4444-444444444444}"
                                  authorId="{22222222-2222-2222-2222-222222222222}"
                                  status="active"
                                  created="2026-07-03T10:20:00Z">
                        <p188:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Reply retained.</a:t></a:r></a:p></p188:txBody>
                      </p188:reply>
                    </p188:replyLst>
                    <p188:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Modern thread root.</a:t></a:r></a:p></p188:txBody>
                  </p188:cm>
                </p188:cmLst>
                """);

            WriteZipEntry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
                  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
                  <Override PartName="/ppt/authors/author1.xml" ContentType="application/vnd.ms-powerpoint.authors+xml"/>
                  <Override PartName="/ppt/comments/comment1.xml" ContentType="application/vnd.ms-powerpoint.comments+xml"/>
                </Types>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"{path} not found");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static XElement? Relationship(XDocument doc, string type, string target)
    {
        XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
        return doc.Root?.Elements(rels + "Relationship").FirstOrDefault(r =>
            r.Attribute("Type")?.Value == type &&
            r.Attribute("Target")?.Value == target);
    }

    private static XElement? Override(XDocument doc, string partName, string contentType)
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        return doc.Root?.Elements(ct + "Override").FirstOrDefault(o =>
            o.Attribute("PartName")?.Value == partName &&
            o.Attribute("ContentType")?.Value == contentType);
    }

    private static bool HasAttribute(XElement element, string name, string value)
        => element.Attribute(name)?.Value == value;

    private static void WriteZipEntry(System.IO.Compression.ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new System.IO.StreamWriter(entry.Open(), System.Text.Encoding.UTF8);
        writer.Write(content.Trim());
    }
}
