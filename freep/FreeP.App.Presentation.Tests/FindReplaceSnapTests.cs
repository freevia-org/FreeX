using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

// ══════════════════════════════════════════════════════════════════════════════
//  WAVE 12B: Find & Replace + Snap Engine tests
// ══════════════════════════════════════════════════════════════════════════════

// ── Helpers shared by both test classes ─────────────────────────────────────

file static class Helpers
{
    public static Presentation MakePresentation(int slideCount = 1)
    {
        var p = new Presentation();
        for (int i = 0; i < slideCount; i++)
            p.Slides.Add(new Slide());
        return p;
    }

    public static EditingSession MakeSession(int slideCount = 1)
    {
        var p   = MakePresentation(slideCount);
        var bus = new PresentationCommandBus(p);
        return new EditingSession(p, bus);
    }

    public static TextBody MakeBody(params string[] runTexts)
    {
        var body = new TextBody();
        var para = new Paragraph();
        foreach (var t in runTexts)
            para.Runs.Add(new Run { Text = t });
        body.Paragraphs.Add(para);
        return body;
    }

    public static SlideShape MakeShape(uint id, string runText, long offsetXEmu = 0, long offsetYEmu = 0,
        long extentCxEmu = 914400, long extentCyEmu = 685800)
        => new()
        {
            Id          = id,
            Name        = $"S{id}",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = offsetXEmu,
            OffsetYEmu  = offsetYEmu,
            ExtentCxEmu = extentCxEmu,
            ExtentCyEmu = extentCyEmu,
            TextBody    = MakeBody(runText),
        };
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  PresentationTextSearch tests
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public sealed class PresentationTextSearchTests
{
    [Fact]
    public void FindAll_NullQuery_ReturnsEmpty()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "hello world"));

        var results = PresentationTextSearch.FindAll(p, null);

        results.Should().BeEmpty();
    }

    [Fact]
    public void FindAll_EmptyQuery_ReturnsEmpty()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "hello world"));

        var results = PresentationTextSearch.FindAll(p, "");

        results.Should().BeEmpty();
    }

    [Fact]
    public void FindAll_SingleSlide_FindsMatch()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "hello world"));

        var results = PresentationTextSearch.FindAll(p, "hello");

        results.Should().HaveCount(1);
        results[0].SlideIndex.Should().Be(0);
        results[0].ShapeId.Should().Be(1u);
        results[0].Location.Should().Be(TextMatchLocation.ShapeBody);
        results[0].CharStart.Should().Be(0);
        results[0].CharEnd.Should().Be(5);
        results[0].MatchedText.Should().Be("hello");
    }

    [Fact]
    public void FindAll_MultipleOccurrencesInOneRun_ReturnsAll()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "abcabc"));

        var results = PresentationTextSearch.FindAll(p, "abc");

        results.Should().HaveCount(2);
        results[0].CharStart.Should().Be(0);
        results[1].CharStart.Should().Be(3);
    }

    [Fact]
    public void FindAll_MultipleShapes_FindsAcrossShapes()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "cat"));
        p.Slides[0].Shapes.Add(Helpers.MakeShape(2, "dog cat"));

        var results = PresentationTextSearch.FindAll(p, "cat");

        results.Should().HaveCount(2);
        results[0].ShapeId.Should().Be(1u);
        results[1].ShapeId.Should().Be(2u);
    }

    [Fact]
    public void FindAll_MultipleSlides_FindsAcrossSlides()
    {
        var p = Helpers.MakePresentation(3);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "alpha beta"));
        p.Slides[1].Shapes.Add(Helpers.MakeShape(2, "gamma"));
        p.Slides[2].Shapes.Add(Helpers.MakeShape(3, "alpha delta"));

        var results = PresentationTextSearch.FindAll(p, "alpha");

        results.Should().HaveCount(2);
        results[0].SlideIndex.Should().Be(0);
        results[1].SlideIndex.Should().Be(2);
    }

    [Fact]
    public void FindAll_CaseSensitive_DoesNotMatchWrongCase()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "Hello World HELLO"));

        var opts = new TextSearchOptions { MatchCase = true };
        var results = PresentationTextSearch.FindAll(p, "hello", opts);

        results.Should().BeEmpty();
    }

    [Fact]
    public void FindAll_CaseSensitive_MatchesExactCase()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "Hello World HELLO hello"));

        var opts = new TextSearchOptions { MatchCase = true };
        var results = PresentationTextSearch.FindAll(p, "hello", opts);

        results.Should().HaveCount(1);
        results[0].CharStart.Should().Be(18);
    }

    [Fact]
    public void FindAll_WholeWord_DoesNotMatchSubstring()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "catalog categorize cat"));

        var opts = new TextSearchOptions { WholeWord = true };
        var results = PresentationTextSearch.FindAll(p, "cat", opts);

        // Only the standalone "cat" at the end should match.
        // "catalog categorize cat" — 'c' of last "cat" is at index 19.
        results.Should().HaveCount(1);
        results[0].CharStart.Should().Be(19);
    }

    [Fact]
    public void FindAll_WholeWord_MatchesWordAtStart()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "cat on mat"));

        var opts = new TextSearchOptions { WholeWord = true };
        var results = PresentationTextSearch.FindAll(p, "cat", opts);

        results.Should().HaveCount(1);
        results[0].CharStart.Should().Be(0);
    }

    [Fact]
    public void FindAll_TableCell_FindsTextInCell()
    {
        var p = Helpers.MakePresentation(1);

        // Create table shape.
        var cell = new TableCell { TextBody = Helpers.MakeBody("quarterly") };
        var row  = new TableRow();
        row.Cells.Add(cell);
        var table = new TableShape();
        table.Rows.Add(row);

        var tblShape = new SlideShape
        {
            Id          = 10,
            Name        = "Table1",
            Kind        = SlideShapeKind.Table,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 500000,
            Table       = table,
        };
        p.Slides[0].Shapes.Add(tblShape);

        var results = PresentationTextSearch.FindAll(p, "quarter");

        results.Should().HaveCount(1);
        results[0].Location.Should().Be(TextMatchLocation.TableCell);
        results[0].TableRow.Should().Be(0);
        results[0].TableCol.Should().Be(0);
        results[0].ShapeId.Should().Be(10u);
    }

    [Fact]
    public void FindAll_Notes_FindsTextInNotes()
    {
        var p = Helpers.MakePresentation(1);
        p.Slides[0].Notes = Helpers.MakeBody("speaker note text");

        var results = PresentationTextSearch.FindAll(p, "speaker");

        results.Should().HaveCount(1);
        results[0].Location.Should().Be(TextMatchLocation.Notes);
        results[0].SlideIndex.Should().Be(0);
    }

    [Fact]
    public void FindAll_GroupShape_SearchesChildren()
    {
        var p = Helpers.MakePresentation(1);

        var child1 = Helpers.MakeShape(2, "nested text");
        var child2 = Helpers.MakeShape(3, "other");
        var group  = new SlideShape
        {
            Id          = 1,
            Name        = "Group1",
            Kind        = SlideShapeKind.Group,
            ExtentCxEmu = 2000000,
            ExtentCyEmu = 1000000,
        };
        group.Children.Add(child1);
        group.Children.Add(child2);
        p.Slides[0].Shapes.Add(group);

        var results = PresentationTextSearch.FindAll(p, "nested");

        results.Should().HaveCount(1);
        results[0].ShapeId.Should().Be(2u);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  ReplaceOne / ReplaceAll command tests
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public sealed class FindReplaceCommandTests
{
    private static (Presentation pres, EditingSession session) MakeSessionWithShape(
        uint shapeId, string runText, int slideCount = 1)
    {
        var p = Helpers.MakePresentation(slideCount);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(shapeId, runText));
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);
        return (p, sess);
    }

    // ── ReplaceOne ────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceOne_ChangesMatchedText()
    {
        var (p, sess) = MakeSessionWithShape(1, "hello world");
        var matches = sess.FindAll("hello");
        matches.Should().HaveCount(1);

        sess.ReplaceOne(matches[0], "goodbye");

        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("goodbye world");
    }

    [Fact]
    public void ReplaceOne_IsUndoable()
    {
        var (p, sess) = MakeSessionWithShape(1, "hello world");
        var matches = sess.FindAll("hello");

        sess.ReplaceOne(matches[0], "goodbye");
        sess.Undo();

        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("hello world");
    }

    [Fact]
    public void ReplaceOne_OnlyChangesMatchedSubstring_NotRest()
    {
        var (p, sess) = MakeSessionWithShape(1, "aaa bbb aaa");
        // Find only the second "aaa" — CharStart=8
        var matches = sess.FindAll("aaa");
        matches.Should().HaveCount(2);

        // Replace only the second match.
        sess.ReplaceOne(matches[1], "XXX");

        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("aaa bbb XXX");
    }

    // ── ReplaceAll ────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_ReturnsCorrectCount()
    {
        var (p, sess) = MakeSessionWithShape(1, "cat dog cat");

        int count = sess.ReplaceAll("cat", "rat");

        count.Should().Be(2);
    }

    [Fact]
    public void ReplaceAll_ReplacesAllOccurrences()
    {
        var (p, sess) = MakeSessionWithShape(1, "cat dog cat");

        sess.ReplaceAll("cat", "rat");

        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("rat dog rat");
    }

    [Fact]
    public void ReplaceAll_IsUndoableInOneStep()
    {
        var (p, sess) = MakeSessionWithShape(1, "cat dog cat");

        sess.ReplaceAll("cat", "rat");
        sess.Undo();

        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("cat dog cat");
    }

    [Fact]
    public void ReplaceAll_AcrossMultipleSlides_ReplacesAll()
    {
        var p = Helpers.MakePresentation(2);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "foo bar"));
        p.Slides[1].Shapes.Add(Helpers.MakeShape(2, "baz foo"));
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);

        int count = sess.ReplaceAll("foo", "qux");

        count.Should().Be(2);
        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("qux bar");
        p.Slides[1].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("baz qux");
    }

    [Fact]
    public void ReplaceAll_NoMatches_ReturnsZero()
    {
        var (_, sess) = MakeSessionWithShape(1, "hello world");

        int count = sess.ReplaceAll("xyz", "abc");

        count.Should().Be(0);
    }

    [Fact]
    public void ReplaceAll_CaseSensitive_OnlyReplacesMatchingCase()
    {
        var (p, sess) = MakeSessionWithShape(1, "Hello hello HELLO");

        var opts = new TextSearchOptions { MatchCase = true };
        int count = sess.ReplaceAll("hello", "REPLACED", opts);

        count.Should().Be(1);
        p.Slides[0].Shapes[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Hello REPLACED HELLO");
    }

    // ── NavigateTo ────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateTo_SetsCurrentSlideIndex()
    {
        var p = Helpers.MakePresentation(3);
        p.Slides[0].Shapes.Add(Helpers.MakeShape(1, "alpha"));
        p.Slides[2].Shapes.Add(Helpers.MakeShape(2, "beta"));
        var bus  = new PresentationCommandBus(p);
        var sess = new EditingSession(p, bus);

        // Start on slide 0.
        sess.CurrentSlideIndex.Should().Be(0);

        var matches = sess.FindAll("beta");
        matches.Should().HaveCount(1);

        sess.NavigateTo(matches[0]);

        sess.CurrentSlideIndex.Should().Be(2);
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  SnapEngine tests
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

public sealed class SnapEngineTests
{
    // Slide: 720 DIP wide × 540 DIP tall (standard 10in × 7.5in at 72dpi).
    private const double W = 720;
    private const double H = 540;

    // Disable grid to test shape-edge snap in isolation.
    private static SnapResult Snap(
        (double l, double t, double r, double b) rect,
        IEnumerable<SnapCandidate>? candidates = null,
        bool snapEnabled = true,
        double threshold = SnapEngine.DefaultThresholdDip)
        => SnapEngine.Snap(rect, candidates, W, H, snapEnabled, gridPitchDip: 0, thresholdDip: threshold);

    // ── snapEnabled = false ───────────────────────────────────────────────────

    [Fact]
    public void Snap_WhenDisabled_ReturnsNone()
    {
        var result = Snap((10, 10, 110, 80), snapEnabled: false);

        result.SnapDx.Should().Be(0);
        result.SnapDy.Should().Be(0);
        result.Guides.Should().BeEmpty();
    }

    // ── grid snap ─────────────────────────────────────────────────────────────

    [Fact]
    public void Snap_Grid_SnapsToNearestGridLine()
    {
        // Moving rect: narrow rect whose center X is within threshold of a grid line.
        // Center X = 47.5, nearest grid = Round(47.5/7.9375)*7.9375 = 6*7.9375 = 47.625.
        // Delta = 47.625 - 47.5 = +0.125 (center snaps to grid). Dist = 0.125 < threshold = 6.
        // All three probes (47, 47.5, 48) map to grid 47.625 — center has smallest dist.
        // Slide edges (0, 720, 360) are all far away, so grid wins.
        var result = SnapEngine.Snap(
            (47, 0, 48, 10),
            candidates: null,
            slideWidthDip: W, slideHeightDip: H,
            snapEnabled: true,
            gridPitchDip: SnapEngine.DefaultGridPitchDip,
            thresholdDip: SnapEngine.DefaultThresholdDip);

        // Center snaps to 47.625, so SnapDx = +0.125.
        result.SnapDx.Should().BeApproximately(0.125, 0.001);
        result.Guides.Should().NotBeEmpty();
    }

    [Fact]
    public void Snap_Grid_NoSnapBeyondThreshold()
    {
        // Left edge at x=10 — nearest grid line is 7.9375 (delta ~2.06) or 0 (delta=10).
        // With threshold=1, neither qualifies.
        var result = SnapEngine.Snap(
            (10, 10, 110, 90),
            candidates: null,
            slideWidthDip: W, slideHeightDip: H,
            snapEnabled: true,
            gridPitchDip: SnapEngine.DefaultGridPitchDip,
            thresholdDip: 1.0);

        result.Should().Be(SnapResult.None);
    }

    // ── shape-edge snap ───────────────────────────────────────────────────────

    [Fact]
    public void Snap_ShapeEdge_SnapsLeftToNearbyShapeLeft()
    {
        // Stationary shape left edge at x=200.
        var candidates = new[]
        {
            new SnapCandidate { IsHorizontal = false, Position = 200, Label = "left edge" }
        };

        // Moving shape left at x=203 — within default threshold of 6.
        var result = Snap((203, 50, 303, 130), candidates);

        result.SnapDx.Should().BeApproximately(-3.0, 0.001);
        result.Guides.Should().HaveCountGreaterThan(0);
        result.Guides[0].IsHorizontal.Should().BeFalse();
        result.Guides[0].Position.Should().BeApproximately(200, 0.001);
    }

    [Fact]
    public void Snap_ShapeEdge_NoSnapBeyondThreshold()
    {
        var candidates = new[]
        {
            new SnapCandidate { IsHorizontal = false, Position = 200, Label = "left edge" }
        };

        // Moving shape left at x=210 — 10 DIP away, beyond default threshold=6.
        var result = Snap((210, 50, 310, 130), candidates);

        result.SnapDx.Should().Be(0);
    }

    [Fact]
    public void Snap_BothAxes_SnapsXAndY()
    {
        var candidates = new[]
        {
            new SnapCandidate { IsHorizontal = false, Position = 200, Label = "left" },
            new SnapCandidate { IsHorizontal = true,  Position = 100, Label = "top"  },
        };

        // Moving shape: left=203, top=104.
        var result = Snap((203, 104, 303, 184), candidates);

        result.SnapDx.Should().BeApproximately(-3.0, 0.001);
        result.SnapDy.Should().BeApproximately(-4.0, 0.001);
        result.Guides.Should().HaveCount(2);
    }

    [Fact]
    public void Snap_SlideEdge_SnapsToSlideLeft()
    {
        // Left edge of moving shape at x=4 — close to slide left edge (x=0).
        var result = Snap((4, 200, 104, 280), threshold: 6.0);

        result.SnapDx.Should().BeApproximately(-4.0, 0.001);
    }

    [Fact]
    public void Snap_SlideCenter_SnapsToSlideCenter()
    {
        // Center x of moving shape = W/2 + 2 = 362.
        // Moving rect: left=262, right=462, center=362.
        var result = Snap((262, 200, 462, 280), threshold: 6.0);

        // Should snap center x (362) to slide center (360): SnapDx = -2.
        result.SnapDx.Should().BeApproximately(-2.0, 0.001);
    }

    // ── BuildShapeCandidates ──────────────────────────────────────────────────

    [Fact]
    public void BuildShapeCandidates_ExcludesGivenIds()
    {
        var slide = new Slide();
        // Shape at x=100 EMU, width=200 EMU (very small, but valid for unit test).
        slide.Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "A",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 100,
            OffsetYEmu  = 50,
            ExtentCxEmu = 200,
            ExtentCyEmu = 100,
        });
        slide.Shapes.Add(new SlideShape
        {
            Id          = 2,
            Name        = "B",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 500,
            OffsetYEmu  = 300,
            ExtentCxEmu = 200,
            ExtentCyEmu = 100,
        });

        var candidates = SnapEngine.BuildShapeCandidates(slide, new[] { 1u });

        // Only shape B's edges should appear.
        candidates.Should().NotContain(c => !c.IsHorizontal && c.Position.Equals(100.0 / 9525.0));
        candidates.Should().Contain(c => !c.IsHorizontal && c.Label == "left edge");
        candidates.All(c =>
            // All came from shape 2 (offset 500 / 9525).
            c.IsHorizontal || Math.Abs(c.Position - 500.0 / 9525.0) < 0.001
                           || Math.Abs(c.Position - 700.0 / 9525.0) < 0.001
                           || Math.Abs(c.Position - 600.0 / 9525.0) < 0.001)
        .Should().BeTrue();
    }

    [Fact]
    public void BuildShapeCandidates_IncludesLeftRightCenterTopBottomCenterY()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 5,
            Name        = "C",
            Kind        = SlideShapeKind.AutoShape,
            OffsetXEmu  = 9525,    // 1 DIP
            OffsetYEmu  = 19050,   // 2 DIP
            ExtentCxEmu = 95250,   // 10 DIP
            ExtentCyEmu = 47625,   // 5 DIP
        });

        var candidates = SnapEngine.BuildShapeCandidates(slide, Array.Empty<uint>());

        // 6 candidates per shape: left, right, centerX, top, bottom, centerY.
        candidates.Should().HaveCount(6);

        var verts = candidates.Where(c => !c.IsHorizontal).ToList();
        verts.Should().HaveCount(3);
        verts.Should().Contain(c => Math.Abs(c.Position - 1.0)  < 0.001); // left
        verts.Should().Contain(c => Math.Abs(c.Position - 11.0) < 0.001); // right
        verts.Should().Contain(c => Math.Abs(c.Position - 6.0)  < 0.001); // center

        var horizs = candidates.Where(c => c.IsHorizontal).ToList();
        horizs.Should().HaveCount(3);
        horizs.Should().Contain(c => Math.Abs(c.Position - 2.0)   < 0.001); // top
        horizs.Should().Contain(c => Math.Abs(c.Position - 7.0)   < 0.001); // bottom
        horizs.Should().Contain(c => Math.Abs(c.Position - 4.5)   < 0.001); // center
    }
}
