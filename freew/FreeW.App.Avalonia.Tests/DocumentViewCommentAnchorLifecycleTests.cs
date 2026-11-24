using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewCommentAnchorLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("selection")]
    [InlineData("backspace")]
    [InlineData("delete")]
    [InlineData("replace")]
    public Task LastRangeAnchorRemovesItsThreadReferencesButPreservesPointCommentsAndUndo(string operation) => OnUiThread(() =>
    {
        var (document, thread, reply, pointThread) = Document();
        var paragraph = new Paragraph();
        var anchor = new Run("abc") { CommentId = reply.Id };
        var replyReference = Run.CommentReference(reply.Id);
        var rootReference = Run.CommentReference(thread.Id);
        var pointReference = Run.CommentReference(pointThread.Id);
        paragraph.Runs.AddRange([anchor, replyReference, rootReference, pointReference, new Run("tail")]);
        paragraph.BookmarkNames.Add("range");
        paragraph.BookmarkBoundaries.Add(new("range-pair", BookmarkBoundaryKind.Start, 0, "range"));
        paragraph.BookmarkBoundaries.Add(new("range-pair", BookmarkBoundaryKind.End, 3));
        document.Blocks.Add(paragraph);
        var runs = paragraph.Runs.ToArray();
        var bookmarks = paragraph.BookmarkBoundaries.ToArray();
        var view = View(document);
        view.SetBodySelectionForTest(0, 0, 0, 3);

        if (operation == "backspace") view.BackspaceForTest();
        else if (operation == "delete") view.DeleteForwardForTest();
        else if (operation == "replace") view.InsertText("X");
        else view.TryDeleteSelection().Should().BeTrue();

        document.Comments.Should().NotContainKey(thread.Id);
        document.Comments[pointThread.Id].Should().BeSameAs(pointThread);
        paragraph.Runs.Where(run => run.IsCommentReference).Should().ContainSingle().Which.Should().BeSameAs(pointReference);
        paragraph.PlainText.Should().Be(operation == "replace" ? "Xtail" : "tail");
        if (operation == "replace")
            paragraph.Runs.First().CommentId.Should().BeNull("new text must not recreate a wholly removed range");
        view.Undo();
        paragraph.Runs.Should().Equal(runs, "reference cleanup must not overwrite the source's pre-edit run snapshot");
        paragraph.BookmarkBoundaries.Should().Equal(bookmarks);
        document.Comments[thread.Id].Should().BeSameAs(thread);
        thread.Replies.Single().Should().BeSameAs(reply);
        document.Comments[pointThread.Id].Should().BeSameAs(pointThread);
        view.Redo();
        document.Comments.Should().NotContainKey(thread.Id);
        paragraph.Runs.Where(run => run.IsCommentReference).Should().ContainSingle().Which.Should().BeSameAs(pointReference);
    });

    [Theory]
    [InlineData("body")]
    [InlineData("cell")]
    [InlineData("header")]
    [InlineData("shape")]
    [InlineData("group")]
    public Task ExternalReferenceParagraphsAndTheirBookmarksRestoreBeforeTheSourceUndo(string story) => OnUiThread(() =>
    {
        var (document, thread, reply, pointThread) = Document();
        var source = new Paragraph();
        var anchor = new Run("abc") { CommentId = reply.Id };
        source.Runs.Add(anchor);
        source.BookmarkNames.Add("source");
        source.BookmarkBoundaries.Add(new("source-pair", BookmarkBoundaryKind.Start, 0, "source"));
        source.BookmarkBoundaries.Add(new("source-pair", BookmarkBoundaryKind.End, 1));
        document.Blocks.Add(source);
        var external = new Paragraph();
        var reference = Run.CommentReference(reply.Id);
        var pointReference = Run.CommentReference(pointThread.Id);
        var left = new Run("left");
        var right = new Run("right");
        external.Runs.AddRange([left, reference, pointReference, right]);
        external.BookmarkNames.Add("external");
        external.BookmarkBoundaries.Add(new("external-pair", BookmarkBoundaryKind.Start, 1, "external"));
        external.BookmarkBoundaries.Add(new("external-pair", BookmarkBoundaryKind.End, 2));
        AddExternalParagraph(document, external, story);
        var sourceBookmarks = source.BookmarkBoundaries.ToArray();
        var externalBookmarks = external.BookmarkBoundaries.ToArray();
        var externalRuns = external.Runs.ToArray();
        var view = View(document);
        view.SetBodySelectionForTest(0, 0, 0, 3);

        view.TryDeleteSelection().Should().BeTrue();

        document.Comments.Should().NotContainKey(thread.Id);
        external.Runs.Should().Equal(left, pointReference, right);
        external.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 1);
        source.Runs.Should().BeEmpty();
        view.Undo();
        source.Runs.Should().ContainSingle().Which.Should().BeSameAs(anchor);
        source.BookmarkBoundaries.Should().Equal(sourceBookmarks);
        external.Runs.Should().Equal(externalRuns);
        external.BookmarkBoundaries.Should().Equal(externalBookmarks);
        document.Comments[thread.Id].Should().BeSameAs(thread);
        document.Comments[pointThread.Id].Should().BeSameAs(pointThread);
        view.Redo();
        external.Runs.Should().Equal(left, pointReference, right);
        document.Comments.Should().NotContainKey(thread.Id);
    });

    [Fact]
    public Task TrueReferenceOnlyPointCommentSurvivesUnrelatedTextDeletion() => OnUiThread(() =>
    {
        var (document, _, _, pointThread) = Document();
        var paragraph = new Paragraph();
        var text = new Run("abc");
        var point = Run.CommentReference(pointThread.Id);
        paragraph.Runs.AddRange([text, point]);
        document.Blocks.Add(paragraph);
        var view = View(document);
        view.SetBodySelectionForTest(0, 0, 0, 3);

        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.Should().ContainSingle().Which.Should().BeSameAs(point);
        document.Comments[pointThread.Id].Should().BeSameAs(pointThread);
        view.Undo();
        paragraph.Runs.Should().Equal(text, point);
    });

    [Theory]
    [InlineData("equation")]
    [InlineData("control")]
    public Task EmptyTextObjectOrControlIsStillASubstantiveCommentAnchor(string kind) => OnUiThread(() =>
    {
        var (document, thread, reply, _) = Document();
        var paragraph = new Paragraph();
        var text = new Run("abc") { CommentId = reply.Id };
        var remaining = kind == "equation" ? new Run("") { Equation = Equation.FromText("x+y") }
            : Run.PlainTextControl("", tag: "commented-empty-control");
        remaining.CommentId = reply.Id;
        var reference = Run.CommentReference(reply.Id);
        paragraph.Runs.AddRange([text, remaining, reference]);
        document.Blocks.Add(paragraph);
        var view = View(document);
        view.SetBodySelectionForTest(0, 0, 0, 3);

        view.TryDeleteSelection().Should().BeTrue();

        paragraph.Runs.Should().Equal(remaining, reference);
        document.Comments[thread.Id].Should().BeSameAs(thread);
        view.Undo();
        paragraph.Runs.Should().Equal(text, remaining, reference);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TrackedDeletionKeepsRangeUntilTextIsActuallyRemoved(bool ownInsertion) => OnUiThread(() =>
    {
        var (document, thread, reply, _) = Document();
        var paragraph = new Paragraph();
        var text = new Run("abc") { CommentId = reply.Id };
        if (ownInsertion)
        {
            text.Revision = RevisionKind.Inserted;
            text.RevisionAuthor = "Reviewer";
        }
        var reference = Run.CommentReference(reply.Id);
        paragraph.Runs.AddRange([text, reference]);
        document.Blocks.Add(paragraph);
        var view = View(document);
        view.RevisionAuthor = "Reviewer";
        view.ToggleTrackChanges();
        view.SetBodySelectionForTest(0, 0, 0, 3);

        view.TryDeleteSelection().Should().BeTrue();

        if (ownInsertion)
        {
            document.Comments.Should().NotContainKey(thread.Id);
            paragraph.Runs.Should().BeEmpty();
        }
        else
        {
            document.Comments[thread.Id].Should().BeSameAs(thread);
            paragraph.Runs.Should().Contain(run => ReferenceEquals(run, reference));
            paragraph.Runs.Single(run => !run.IsCommentReference).Revision.Should().Be(RevisionKind.Deleted);
        }
        view.Undo();
        paragraph.Runs.Should().Equal(text, reference);
        document.Comments[thread.Id].Should().BeSameAs(thread);
    });

    [Fact]
    public Task PartialReplacementKeepsTheRangeButWholeReplacementRemovesIt() => OnUiThread(() =>
    {
        var (document, thread, reply, _) = Document();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("abc") { CommentId = reply.Id });
        var reference = Run.CommentReference(reply.Id);
        paragraph.Runs.Add(reference);
        document.Blocks.Add(paragraph);
        var view = View(document);
        view.SetBodySelectionForTest(0, 1, 0, 2);

        view.InsertText("X");

        paragraph.PlainText.Should().Be("aXc");
        paragraph.Runs.Single(run => run.Text == "X").CommentId.Should().Be(reply.Id);
        document.Comments[thread.Id].Should().BeSameAs(thread);
        view.SetBodySelectionForTest(0, 0, 0, 3);
        view.InsertText("Y");
        document.Comments.Should().NotContainKey(thread.Id);
        paragraph.Runs.Should().ContainSingle().Which.CommentId.Should().BeNull();
        view.Undo();
        paragraph.PlainText.Should().Be("aXc");
        paragraph.Runs.Should().Contain(run => ReferenceEquals(run, reference));
        document.Comments[thread.Id].Should().BeSameAs(thread);
    });

    private static (TextDocument Document, Comment Thread, Comment Reply, Comment PointThread) Document()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var thread = new Comment(1, "Range comment");
        thread.AddReply(3, "Reply comment");
        var point = new Comment(2, "Point comment");
        document.Comments[thread.Id] = thread;
        document.Comments[point.Id] = point;
        return (document, thread, thread.Replies.Single(), point);
    }

    private static DocumentView View(TextDocument document)
    {
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }

    private static void AddExternalParagraph(TextDocument document, Paragraph paragraph, string story)
    {
        if (story == "body") document.Blocks.Add(paragraph);
        else if (story == "cell")
        {
            var table = Table.Create(1, 1);
            table.Rows[0].Cells[0].Paragraphs.Clear();
            table.Rows[0].Cells[0].Paragraphs.Add(paragraph);
            document.Blocks.Add(table);
        }
        else if (story == "header")
        {
            document.FinalSectionHeadersFooters.Header = new HeaderFooter();
            document.FinalSectionHeadersFooters.Header.Paragraphs.Add(paragraph);
        }
        else
        {
            var shape = new Shape(ShapeKind.Rectangle, 120, 60);
            shape.TextParagraphs.Add(paragraph);
            var host = new Paragraph();
            if (story == "group")
            {
                var group = new DrawingGroup { WidthPt = 160, HeightPt = 80 };
                group.Children.Add(shape);
                group.ChildOffsets.Add((0, 0));
                host.Runs.Add(new Run("") { DrawingGroup = group });
            }
            else host.Runs.Add(new Run("") { Shape = shape });
            document.Blocks.Add(host);
        }
    }

    private static Task OnUiThread(Action action) => Session.Dispatch(action, CancellationToken.None);
}
