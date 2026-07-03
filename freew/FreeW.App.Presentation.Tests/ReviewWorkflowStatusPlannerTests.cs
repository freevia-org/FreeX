using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewWorkflowStatusPlannerTests
{
    [Fact]
    public void Build_summarizes_revisions_comments_and_track_changes_state()
    {
        var doc = ReviewDocument();

        var status = ReviewWorkflowStatusPlanner.Build(
            doc,
            ReviewDisplayPolicy.Default,
            trackChangesEnabled: true);

        status.TrackChangesEnabled.Should().BeTrue();
        status.DisplayMode.Should().Be(ReviewDisplayMode.AllMarkup);
        status.DisplayModeLabel.Should().Be("All Markup");
        status.RevisionCount.Should().Be(3);
        status.InsertionCount.Should().Be(1);
        status.DeletionCount.Should().Be(1);
        status.FormattingCount.Should().Be(1);
        status.CommentThreadCount.Should().Be(2);
        status.OpenCommentThreadCount.Should().Be(1);
        status.ResolvedCommentThreadCount.Should().Be(1);
        status.VisibleReviewItemCount.Should().Be(5);
        status.CanNavigateChanges.Should().BeTrue();
        status.CanAcceptOrRejectChanges.Should().BeTrue();
        status.HasHiddenMarkup.Should().BeFalse();
        status.StatusText.Should().Be("Track Changes: On - 3 changes - 2 comments");
    }

    [Fact]
    public void Build_exposes_show_markup_filter_descriptors()
    {
        var policy = new ReviewDisplayPolicy(
            ReviewDisplayMode.AllMarkup,
            ShowInsertionsAndDeletions: false,
            ShowComments: true,
            ShowFormatting: false);

        var status = ReviewWorkflowStatusPlanner.Build(
            ReviewDocument(),
            policy,
            trackChangesEnabled: false);

        status.VisibleReviewItemCount.Should().Be(2);
        status.HasHiddenMarkup.Should().BeTrue();
        status.StatusText.Should().Be("Track Changes: Off - 3 changes - 2 comments - some markup hidden");

        status.MarkupDescriptors.Select(descriptor => descriptor.Id)
            .Should().Equal("insertions-deletions", "comments", "formatting");
        status.MarkupDescriptors[0].IsChecked.Should().BeFalse();
        status.MarkupDescriptors[0].ItemCount.Should().Be(2);
        status.MarkupDescriptors[0].StatusText.Should().Be("Hidden - 2 items");
        status.MarkupDescriptors[1].IsChecked.Should().BeTrue();
        status.MarkupDescriptors[1].ItemCount.Should().Be(2);
        status.MarkupDescriptors[2].IsChecked.Should().BeFalse();
        status.MarkupDescriptors[2].ItemCount.Should().Be(1);
    }

    [Theory]
    [InlineData(ReviewDisplayMode.AllMarkup, "All Markup", "Shows all tracked changes inline.")]
    [InlineData(ReviewDisplayMode.SimpleMarkup, "Simple Markup", "Shows final text with change bars.")]
    [InlineData(ReviewDisplayMode.NoMarkup, "No Markup", "Shows final text without revision markup.")]
    [InlineData(ReviewDisplayMode.Original, "Original", "Shows original text before tracked changes.")]
    public void Build_describes_display_for_review_modes(
        ReviewDisplayMode mode,
        string expectedLabel,
        string expectedDescription)
    {
        var status = ReviewWorkflowStatusPlanner.Build(
            ReviewDocument(),
            new ReviewDisplayPolicy(mode),
            trackChangesEnabled: true);

        status.DisplayModeLabel.Should().Be(expectedLabel);
        status.DisplayModeDescription.Should().Be(expectedDescription);
    }

    [Fact]
    public void Build_disables_change_navigation_and_resolution_for_clean_document()
    {
        var doc = TextDocument.CreateEmpty();

        var status = ReviewWorkflowStatusPlanner.Build(
            doc,
            ReviewDisplayPolicy.Default,
            trackChangesEnabled: false);

        status.RevisionCount.Should().Be(0);
        status.VisibleReviewItemCount.Should().Be(0);
        status.CanNavigateChanges.Should().BeFalse();
        status.CanAcceptOrRejectChanges.Should().BeFalse();
        status.StatusText.Should().Be("Track Changes: Off - 0 changes - 0 comments");
    }

    private static TextDocument ReviewDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Base "));
        paragraph.Runs.Add(new Run("added ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice" });
        paragraph.Runs.Add(new Run("removed ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        paragraph.Runs.Add(new Run("styled", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Cara", "2026-07-03T09:00:00Z")
        });
        paragraph.Runs.Add(new Run(" note") { CommentId = 10 });
        doc.Blocks.Add(paragraph);

        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("cell") { CommentId = 20 });
        doc.Blocks.Add(table);

        doc.Comments[10] = new Comment(10, "Open note", "Dana");
        doc.Comments[20] = new Comment(20, "Done note", "Eli") { Resolved = true };
        return doc;
    }
}
