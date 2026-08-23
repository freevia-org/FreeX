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
            MoveRevisionId = 17,
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
        clone.MoveRevisionId.Should().Be(source.MoveRevisionId);
        clone.FormatRevision.Should().BeSameAs(source.FormatRevision);
    }

    [Fact]
    public void ApplyFormattingRange_SplitsExactlyAndPreservesMetadataAndBookmarkOffsets()
    {
        var hyperlink = new Run("abcdef", RunFormatting.Default)
        {
            HyperlinkUrl = "https://example.com",
            CommentId = 7,
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(hyperlink);
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "bookmark-1", BookmarkBoundaryKind.Start, 1, "target"));

        var changed = RevisionEditPlanner.ApplyFormattingRange(
            paragraph,
            2,
            4,
            formatting => formatting with { LanguageTag = "fr-FR" });

        changed.Should().BeTrue();
        paragraph.Runs.Select(run => run.Text).Should().Equal("ab", "cd", "ef");
        paragraph.Runs[1].Formatting.LanguageTag.Should().Be("fr-FR");
        paragraph.Runs[0].Formatting.LanguageTag.Should().BeNull();
        paragraph.Runs[2].Formatting.LanguageTag.Should().BeNull();
        paragraph.Runs.Should().OnlyContain(run =>
            run.HyperlinkUrl == hyperlink.HyperlinkUrl && run.CommentId == hyperlink.CommentId);
        paragraph.BookmarkBoundaries.Should().ContainSingle().Which.RunIndex.Should().Be(3);
    }

    [Fact]
    public void ApplyFormattingRange_RecordsTrackedFormattingRevisionWhenEnabled()
    {
        var original = RunFormatting.Default with { Bold = true };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text", original));
        var document = new TextDocument
        {
            TrackRevisions = true,
            DoNotTrackFormatting = false,
        };

        RevisionEditPlanner.ApplyFormattingRange(
            paragraph,
            0,
            4,
            formatting => formatting with { LanguageTag = "de-DE" },
            document,
            "Reviewer",
            "2026-08-11T12:00:00Z");

        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].FormatRevision.Should().Be(new FormatRevision(
            original,
            "Reviewer",
            "2026-08-11T12:00:00Z"));
    }

    [Fact]
    public void InsertRunAtOffset_DoesNotSplitRubyAnnotation()
    {
        var ruby = new RubyAnnotation();
        ruby.BaseFragments.Add(new RubyTextFragment("Alpha beta", RunFormatting.Default));
        ruby.PhoneticFragments.Add(new RubyTextFragment("guide", RunFormatting.Default));
        var rubyRun = Run.FromRuby(ruby);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(rubyRun);
        var mark = DocumentIndex.MarkRun("Alpha");

        var effectiveOffset = RevisionEditPlanner.InsertRunAtOffset(paragraph, 5, mark);

        effectiveOffset.Should().Be(rubyRun.Text.Length);
        paragraph.Runs.Should().Equal(rubyRun, mark);
        paragraph.Runs[0].Ruby.Should().BeSameAs(ruby);
        paragraph.PlainText.Should().Be("Alpha beta");
    }

    [Fact]
    public void InsertRunAtOffset_DoesNotSplitContentControl()
    {
        var controlRun = Run.PlainTextControl("Controlled text", tag: "Customer");
        var inserted = new Run("X");
        var paragraph = new Paragraph();
        paragraph.Runs.Add(controlRun);

        var effectiveOffset = RevisionEditPlanner.InsertRunAtOffset(paragraph, 5, inserted);

        effectiveOffset.Should().Be(controlRun.Text.Length);
        paragraph.Runs.Should().Equal(controlRun, inserted);
        paragraph.Runs[0].Control.Should().BeSameAs(controlRun.Control);
        paragraph.Runs[0].Text.Should().Be("Controlled text");
    }

    [Fact]
    public void InsertText_ReturnsCaretAfterAdjustedContentControlBoundary()
    {
        var controlRun = Run.PlainTextControl("Controlled text", tag: "Customer");
        var paragraph = new Paragraph { Runs = { controlRun } };

        var nextOffset = RevisionEditPlanner.InsertText(
            paragraph,
            5,
            "X",
            RunFormatting.Default);

        nextOffset.Should().Be(controlRun.Text.Length + 1);
        paragraph.Runs.Select(run => run.Text).Should().Equal("Controlled text", "X");
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
    public void MarkRevisionRange_RemapsBookmarkAfterSplitRuns()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("abcdef"));
        paragraph.Runs.Add(new Run("tail"));
        paragraph.BookmarkNames.Add("TailBookmark");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("5", BookmarkBoundaryKind.Start, 1, "TailBookmark"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("5", BookmarkBoundaryKind.End, 2));

        RevisionEditPlanner.MarkRevisionRange(
            paragraph,
            2,
            5,
            RevisionKind.Inserted,
            "Alice",
            "2026-07-06T12:00:00Z").Should().BeTrue();

        paragraph.Runs.Select(run => run.Text).Should().Equal("ab", "cde", "f", "tail");
        paragraph.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(3, 4);
        paragraph.Runs[paragraph.BookmarkBoundaries[0].RunIndex].Text.Should().Be("tail");
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
