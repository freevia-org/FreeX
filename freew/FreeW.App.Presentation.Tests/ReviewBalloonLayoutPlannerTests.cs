using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewBalloonLayoutPlannerTests
{
    [Fact]
    public void BuildSources_orders_comments_and_revisions_by_document_anchor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Intro ") { CommentId = 7 });
        paragraph.Runs.Add(Run.CommentReference(7));
        paragraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ann" });
        document.Blocks.Add(paragraph);
        document.Comments[7] = new Comment(7, "Check the introduction.", "Casey", "C");

        var sources = ReviewBalloonLayoutPlanner.BuildSources(document, ReviewDisplayPolicy.Default);

        sources.Select(source => source.Kind).Should().Equal(ReviewBalloonKind.Comment, ReviewBalloonKind.Insertion);
        sources.Select(source => source.Author).Should().Equal("Casey", "Ann");
        sources.Select(source => source.BlockIndex).Should().Equal(0, 0);
        sources.Select(source => source.Offset).Should().Equal(0, 6);
    }

    [Fact]
    public void BuildSources_respects_show_markup_filters()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("old") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        paragraph.Runs.Add(new Run("styled")
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Deb", "2026-07-03T10:00:00Z")
        });
        paragraph.Runs.Add(new Run("note") { CommentId = 2 });
        paragraph.Runs.Add(Run.CommentReference(2));
        document.Blocks.Add(paragraph);
        document.Comments[2] = new Comment(2, "Needs review.", "Commenter", "C");

        var policy = new ReviewDisplayPolicy(
            ReviewDisplayMode.AllMarkup,
            ShowInsertionsAndDeletions: false,
            ShowComments: false,
            ShowFormatting: true);

        var sources = ReviewBalloonLayoutPlanner.BuildSources(document, policy);

        sources.Should().ContainSingle();
        sources[0].Kind.Should().Be(ReviewBalloonKind.Formatting);
    }

    [Fact]
    public void BuildSources_exposes_review_card_metadata_without_mixing_it_into_body_text()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("note") { CommentId = 4 });
        paragraph.Runs.Add(Run.CommentReference(4));
        paragraph.Runs.Add(new Run("added")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Riley",
            RevisionDateXml = "2026-07-03T09:30:00Z"
        });
        document.Blocks.Add(paragraph);

        var comment = new Comment(4, "Please clarify.\nSecond line.", "Casey", "C")
        {
            DateXml = "2026-07-02T11:00:00Z",
            Resolved = true
        };
        comment.AddReply(5, "Done.", "Riley", "R");
        comment.AddReply(6, "Thanks.", "Casey", "C");
        document.Comments[4] = comment;

        var sources = ReviewBalloonLayoutPlanner.BuildSources(document, ReviewDisplayPolicy.Default);

        var commentSource = sources[0];
        commentSource.KindLabel.Should().Be("Resolved comment");
        commentSource.HeaderText.Should().Be("Casey");
        commentSource.BodyText.Should().Be("Please clarify. Second line.");
        commentSource.ReplyCount.Should().Be(2);
        commentSource.MetadataText.Should().Be("Resolved - 2 replies - 2026-07-02");
        commentSource.BodyText.Should().NotContain("replies");

        var revisionSource = sources[1];
        revisionSource.KindLabel.Should().Be("Inserted");
        revisionSource.HeaderText.Should().Be("Riley");
        revisionSource.MetadataText.Should().Be("Tracked change - 2026-07-03");
    }

    [Fact]
    public void BuildLayout_matches_wpf_balloon_anchor_and_leader_geometry()
    {
        var sources = new[]
        {
            new ReviewBalloonSource(ReviewBalloonKind.Comment, "A", "one", 0, 0, 1),
            new ReviewBalloonSource(ReviewBalloonKind.Insertion, "B", "two", 0, 8, 0),
            new ReviewBalloonSource(ReviewBalloonKind.Deletion, "C", "three", 1, 0, 0),
        };

        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(sources, viewportHeight: 600);

        layouts.Select(layout => layout.BalloonY).Should().Equal(72, 272, 472);
        layouts.Select(layout => layout.LeaderStartY).Should().Equal(100, 300, 500);
        layouts.Should().OnlyContain(layout => layout.BalloonX == 12);
        layouts.Should().OnlyContain(layout => layout.LeaderStartX == 0);
        layouts.Should().OnlyContain(layout => layout.LeaderEndX == layout.BalloonX);
        layouts.Should().OnlyContain(layout => layout.LeaderEndY == layout.BalloonMidY);
    }

    [Fact]
    public void BuildLayout_keeps_balloons_close_to_viewport_anchors_when_space_allows()
    {
        var sources = new[]
        {
            new ReviewBalloonSource(ReviewBalloonKind.Comment, "A", "one", 0, 0, 1),
            new ReviewBalloonSource(ReviewBalloonKind.Insertion, "B", "two", 1, 0, 0),
        };

        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(sources, viewportHeight: 420);

        layouts.Select(layout => layout.LeaderStartY).Should().Equal(105, 315);
        layouts.Should().OnlyContain(layout => layout.BalloonMidY == layout.LeaderStartY);
        layouts.Select(layout => layout.BalloonY).Should().Equal(77, 287);
    }

    [Fact]
    public void BuildLayout_uses_laid_out_anchor_coordinates_and_falls_back_when_one_is_missing()
    {
        var sources = new[]
        {
            new ReviewBalloonSource(ReviewBalloonKind.Comment, "A", "one", 0, 0, 1),
            new ReviewBalloonSource(ReviewBalloonKind.Insertion, "B", "two", 1, 0, 0),
        };

        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(
            sources,
            viewportHeight: 400,
            anchorYs: [42, null]);

        layouts[0].LeaderStartY.Should().Be(42);
        layouts[0].BalloonMidY.Should().Be(42);
        layouts[1].LeaderStartY.Should().Be(300);
        layouts[1].BalloonMidY.Should().Be(300);
    }

    [Fact]
    public void BuildLayout_clamps_laid_out_anchor_coordinates_to_canvas()
    {
        var source = new ReviewBalloonSource(ReviewBalloonKind.Comment, "A", "one", 0, 0, 1);

        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(
            [source],
            viewportHeight: 400,
            anchorYs: [-20]);

        layouts[0].LeaderStartY.Should().Be(0);
    }

    [Fact]
    public void BuildLayout_avoids_balloon_collisions_when_viewport_is_short()
    {
        var sources = Enumerable.Range(0, 4)
            .Select(index => new ReviewBalloonSource(
                ReviewBalloonKind.Comment,
                $"Author {index}",
                $"note {index}",
                index,
                0,
                1))
            .ToArray();

        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(sources, viewportHeight: 180);

        layouts[0].BalloonY.Should().Be(8);
        layouts.Zip(layouts.Skip(1), (previous, next) => next.BalloonY - previous.BalloonY)
            .Should().OnlyContain(delta => delta >= 64);
        layouts.Should().OnlyContain(layout => layout.LeaderEndY == layout.BalloonMidY);
    }
}
