namespace FreeW.Core.Model.Tests;

public sealed class RevisionEditPlannerTests
{
    [Fact]
    public void CloneRunWithText_PreservesRunMetadataAndReplacesOnlyText()
    {
        var formatting = RunFormatting.Default with { Bold = true, Italic = true };
        var source = new Run("source", formatting)
        {
            HyperlinkUrl = "https://example.com",
            HyperlinkAnchor = "bookmark",
            HyperlinkTooltip = "Example",
            FieldKind = RunFieldKind.PageNumber,
            FootnoteId = 7,
            EndnoteId = 8,
            CommentId = 9,
            IsCommentReference = true,
            IsPageBreak = true,
            IsColumnBreak = true,
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-07-06T12:00:00Z",
            FormatRevision = new FormatRevision(RunFormatting.Default, "Reviewer", "2026-07-06T00:00:00Z")
        };

        var clone = RevisionEditPlanner.CloneRunWithText(source, "replacement");

        clone.Text.Should().Be("replacement");
        clone.Formatting.Should().BeSameAs(formatting);
        clone.HyperlinkUrl.Should().Be(source.HyperlinkUrl);
        clone.HyperlinkAnchor.Should().Be(source.HyperlinkAnchor);
        clone.HyperlinkTooltip.Should().Be(source.HyperlinkTooltip);
        clone.FieldKind.Should().Be(source.FieldKind);
        clone.FootnoteId.Should().Be(source.FootnoteId);
        clone.EndnoteId.Should().Be(source.EndnoteId);
        clone.CommentId.Should().Be(source.CommentId);
        clone.IsCommentReference.Should().Be(source.IsCommentReference);
        clone.IsPageBreak.Should().Be(source.IsPageBreak);
        clone.IsColumnBreak.Should().Be(source.IsColumnBreak);
        clone.Revision.Should().Be(source.Revision);
        clone.RevisionAuthor.Should().Be(source.RevisionAuthor);
        clone.RevisionDateXml.Should().Be(source.RevisionDateXml);
        clone.FormatRevision.Should().BeSameAs(source.FormatRevision);
    }

    [Fact]
    public void InsertTrackedText_PreservesSplitRunMetadata()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("abcdef", RunFormatting.Default with { Bold = true })
        {
            CommentId = 42,
            HyperlinkUrl = "https://example.com",
            HyperlinkTooltip = "Example",
            FormatRevision = new FormatRevision(RunFormatting.Default, "Reviewer", "2026-07-06T00:00:00Z")
        });

        var next = RevisionEditPlanner.InsertTrackedText(
            paragraph,
            3,
            "X",
            RunFormatting.Default with { Italic = true },
            "Alice",
            "2026-07-06T12:00:00Z",
            hyperlinkUrl: "https://inserted.example");

        next.Should().Be(4);
        paragraph.Runs.Select(r => r.Text).Should().Equal("abc", "X", "def");
        paragraph.Runs[0].CommentId.Should().Be(42);
        paragraph.Runs[2].HyperlinkUrl.Should().Be("https://example.com");
        paragraph.Runs[2].FormatRevision.Should().NotBeNull();
        paragraph.Runs[1].Revision.Should().Be(RevisionKind.Inserted);
        paragraph.Runs[1].RevisionAuthor.Should().Be("Alice");
        paragraph.Runs[1].HyperlinkUrl.Should().Be("https://inserted.example");
    }

    [Fact]
    public void DeleteRangeAsRevision_MarksOrdinaryTextDeleted()
    {
        var paragraph = new Paragraph("abcdef");

        var result = RevisionEditPlanner.DeleteRangeAsRevision(
            paragraph,
            2,
            5,
            "Alice",
            "2026-07-06T12:00:00Z");

        result.CaretOffset.Should().Be(2);
        result.KeptDeletedText.Should().BeTrue();
        paragraph.PlainText.Should().Be("abcdef");
        paragraph.Runs.Select(r => r.Text).Should().Equal("ab", "cde", "f");
        paragraph.Runs[1].Revision.Should().Be(RevisionKind.Deleted);
        paragraph.Runs[1].RevisionAuthor.Should().Be("Alice");
    }

    [Fact]
    public void DeleteRangeAsRevision_RemovesOwnPendingInsertion()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Hello "));
        paragraph.Runs.Add(new Run("X")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-07-06T12:00:00Z"
        });

        var result = RevisionEditPlanner.DeleteRangeAsRevision(
            paragraph,
            6,
            7,
            "Alice",
            "2026-07-06T12:01:00Z");

        result.KeptDeletedText.Should().BeFalse();
        paragraph.PlainText.Should().Be("Hello ");
        paragraph.Runs.Should().NotContain(r => r.Revision == RevisionKind.Deleted);
        paragraph.Runs.Should().NotContain(r => r.Revision == RevisionKind.Inserted);
    }

    [Fact]
    public void AcceptReject_AfterPlannedLiveEdits_MatchesWordStyleSemantics()
    {
        var accepted = new TextDocument();
        var acceptedParagraph = new Paragraph("abc");
        accepted.Blocks.Add(acceptedParagraph);
        RevisionEditPlanner.DeleteRangeAsRevision(acceptedParagraph, 2, 3, "Alice", "2026-07-06T12:00:00Z");
        RevisionEditPlanner.InsertTrackedText(acceptedParagraph, 2, "Z", RunFormatting.Default, "Alice", "2026-07-06T12:00:01Z");

        TrackChanges.AcceptAll(accepted);

        acceptedParagraph.PlainText.Should().Be("abZ");
        acceptedParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);

        var rejected = new TextDocument();
        var rejectedParagraph = new Paragraph("abc");
        rejected.Blocks.Add(rejectedParagraph);
        RevisionEditPlanner.DeleteRangeAsRevision(rejectedParagraph, 2, 3, "Alice", "2026-07-06T12:00:00Z");
        RevisionEditPlanner.InsertTrackedText(rejectedParagraph, 2, "Z", RunFormatting.Default, "Alice", "2026-07-06T12:00:01Z");

        TrackChanges.RejectAll(rejected);

        rejectedParagraph.PlainText.Should().Be("abc");
        rejectedParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }
}
