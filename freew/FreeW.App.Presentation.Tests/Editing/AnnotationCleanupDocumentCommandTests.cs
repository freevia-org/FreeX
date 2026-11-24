using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class AnnotationCleanupDocumentCommandTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemovingNoteWithSoleCommentRangePrunesItsThreadAndReferencesInOneReversibleCommand(bool footnote)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var thread = new Comment(1, "Range thread");
        thread.AddReply(3, "Reply");
        var pointThread = new Comment(2, "Point thread");
        document.Comments[1] = thread;
        document.Comments[2] = pointThread;
        var noteReference = footnote ? Run.FootnoteReference(403) : Run.EndnoteReference(403);
        noteReference.CommentId.Should().BeNull("the comment's range is inside the note, not on its outer reference");
        var sourceReference = Run.CommentReference(3);
        var sourcePoint = Run.CommentReference(2);
        var sourceTail = new Run("tail");
        var source = new Paragraph();
        source.Runs.AddRange([noteReference, sourceReference, sourcePoint, sourceTail]);
        Bookmark(source, "source", 0, 1);
        var sourceRuns = source.Runs.ToArray();
        var sourceBookmarks = source.BookmarkBoundaries.ToArray();
        document.Blocks.Add(source);
        var external = new Paragraph();
        var left = new Run("left");
        var externalReference = Run.CommentReference(3);
        var externalPoint = Run.CommentReference(2);
        var right = new Run("right");
        external.Runs.AddRange([left, externalReference, externalPoint, right]);
        Bookmark(external, "external", 1, 2);
        var externalRuns = external.Runs.ToArray();
        var externalBookmarks = external.BookmarkBoundaries.ToArray();
        document.Blocks.Add(external);
        var noteParagraph = new Paragraph();
        var anchoredText = new Run("Only range anchor") { CommentId = 3 };
        noteParagraph.Runs.Add(anchoredText);
        var footnoteObject = new Footnote(403);
        var endnoteObject = new Endnote(403);
        if (footnote)
        {
            footnoteObject.Content.Add(noteParagraph);
            document.Footnotes[403] = footnoteObject;
        }
        else
        {
            endnoteObject.Content.Add(noteParagraph);
            document.Endnotes[403] = endnoteObject;
        }
        IDocumentCommand inner = new ReplaceParagraphRunsCommand(0, paragraph => paragraph.Runs.RemoveAt(0));
        var command = new AnnotationCleanupDocumentCommand(inner, [noteReference], "Delete note");
        var context = new Context(document);

        command.Apply(context);

        document.Footnotes.Should().NotContainKey(403);
        document.Endnotes.Should().NotContainKey(403);
        document.Comments.Should().NotContainKey(1);
        document.Comments[2].Should().BeSameAs(pointThread);
        source.Runs.Should().Equal(sourcePoint, sourceTail);
        external.Runs.Should().Equal(left, externalPoint, right);
        external.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 1);
        command.EstimatedBytes.Should().BeGreaterThan(inner.EstimatedBytes + 128,
            "removed note/thread data and reference snapshots remain retained for Undo");

        command.Revert(context);

        if (footnote) document.Footnotes[403].Should().BeSameAs(footnoteObject);
        else document.Endnotes[403].Should().BeSameAs(endnoteObject);
        noteParagraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(anchoredText);
        document.Comments[1].Should().BeSameAs(thread);
        thread.Replies.Single().Id.Should().Be(3);
        source.Runs.Should().Equal(sourceRuns, "the wrapped command's pre-edit source snapshot must win");
        source.BookmarkBoundaries.Should().Equal(sourceBookmarks);
        external.Runs.Should().Equal(externalRuns);
        external.BookmarkBoundaries.Should().Equal(externalBookmarks);
        document.Comments[2].Should().BeSameAs(pointThread);

        command.Apply(context);
        document.Comments.Should().NotContainKey(1);
        source.Runs.Should().Equal(sourcePoint, sourceTail);
        external.Runs.Should().Equal(left, externalPoint, right);
    }

    [Theory]
    [InlineData("point")]
    [InlineData("equation")]
    [InlineData("control")]
    public void PointCommentsAndSubstantiveEmptyTextAnchorsSurvive(string kind)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var thread = new Comment(1, "Comment");
        document.Comments[1] = thread;
        var source = new Paragraph();
        var removedText = new Run("text") { CommentId = kind == "point" ? null : 1 };
        source.Runs.Add(removedText);
        if (kind != "point")
        {
            var remaining = kind == "equation"
                ? new Run("") { Equation = Equation.FromText("x+y") }
                : Run.PlainTextControl("", tag: "remaining-field");
            remaining.CommentId = 1;
            source.Runs.Add(remaining);
        }
        var reference = Run.CommentReference(1);
        source.Runs.Add(reference);
        document.Blocks.Add(source);
        var originalRuns = source.Runs.ToArray();
        var command = new AnnotationCleanupDocumentCommand(
            new ReplaceParagraphRunsCommand(0, paragraph => paragraph.Runs.RemoveAt(0)), [removedText], "Delete text");
        var context = new Context(document);

        command.Apply(context);

        document.Comments[1].Should().BeSameAs(thread);
        source.Runs.Should().Equal(originalRuns.Skip(1));
        source.Runs.Last().Should().BeSameAs(reference);
        command.Revert(context);
        source.Runs.Should().Equal(originalRuns);
    }

    [Fact]
    public void WrappedDescriptorFlagsAndMemoryEstimateArePreserved()
    {
        var inner = new DescriptorCommand();
        var command = new AnnotationCleanupDocumentCommand(inner, [], "Edit annotation anchors");

        command.Label.Should().Be("Edit annotation anchors");
        command.MutationKind.Should().Be(DocumentCommandMutationKind.FormField);
        command.HasEffect(new Context(TextDocument.CreateEmpty())).Should().BeFalse();
        command.EstimatedBytes.Should().Be(inner.EstimatedBytes + 128);
    }

    private static void Bookmark(Paragraph paragraph, string name, int start, int end)
    {
        paragraph.BookmarkNames.Add(name);
        paragraph.BookmarkBoundaries.Add(new(name, BookmarkBoundaryKind.Start, start, name));
        paragraph.BookmarkBoundaries.Add(new(name, BookmarkBoundaryKind.End, end));
    }

    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private sealed class DescriptorCommand : IDocumentCommand
    {
        public string Label => "Inner";
        public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;
        public int EstimatedBytes => 729;
        public bool HasEffect(IDocumentCommandContext context) => false;
        public void Apply(IDocumentCommandContext context) { }
        public void Revert(IDocumentCommandContext context) { }
    }
}
