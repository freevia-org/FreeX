using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentReviewEditingSessionTests
{
    [Fact]
    public void AddComment_OwnsIdentityMetadataMutationAndUndo()
    {
        var document = DocumentWith("abcdef");
        var session = DeterministicSession();
        session.LoadDocument(document);

        var commentId = session.Review.TryAddComment(
            0,
            1,
            4,
            "Please revise",
            "Ann Reviewer",
            "AR");

        commentId.Should().NotBeNull();
        var comment = document.Comments[commentId!.Value];
        comment.PlainText.Should().Be("Please revise");
        comment.Author.Should().Be("Ann Reviewer");
        comment.Initials.Should().Be("AR");
        comment.DateXml.Should().Be("2026-08-05T10:20:30Z");
        ((Paragraph)document.Blocks[0]).Runs.Should().Contain(run =>
            run.CommentId == commentId && run.IsCommentReference);
        session.Commands.CanUndo.Should().BeTrue();

        session.Commands.Undo().Should().BeTrue();
        document.Comments.Should().BeEmpty();
        ((Paragraph)document.Blocks[0]).Runs.Should().NotContain(run => run.CommentId == commentId);

        session.Commands.Redo().Should().BeTrue();
        document.Comments.Should().ContainKey(commentId.Value);
    }

    [Fact]
    public void CommentLifecycle_ResolvesRepliesAndPreservesRendererTextPolicy()
    {
        var document = DocumentWith("body");
        document.Protection = new ProtectionSettings(ProtectionMode.CommentsOnly);
        var session = DeterministicSession();
        session.LoadDocument(document);
        var commentId = session.Review.TryAddComment(0, 0, int.MaxValue, "note", "A", "A")!.Value;

        session.Review.TryReplyToComment(
                commentId,
                "  preserved  ",
                "B",
                "B",
                CommentTextNormalization.Preserve)
            .Should().BeTrue();
        var preservedReply = document.Comments[commentId].Replies.Single();
        preservedReply.PlainText.Should().Be("  preserved  ");
        session.Review.ResolveTopLevelCommentId(preservedReply.Id).Should().Be(commentId);

        session.Commands.Undo().Should().BeTrue();
        session.Review.TryReplyToComment(
                commentId,
                "  trimmed  ",
                "B",
                "B",
                CommentTextNormalization.Trim)
            .Should().BeTrue();
        document.Comments[commentId].Replies.Single().PlainText.Should().Be("trimmed");

        session.Review.TryToggleCommentResolved(commentId).Should().BeTrue();
        document.Comments[commentId].Resolved.Should().BeTrue();
        session.Review.TryDeleteComment(commentId).Should().BeTrue();
        document.Comments.Should().BeEmpty();

        session.Commands.Undo().Should().BeTrue();
        document.Comments[commentId].Resolved.Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        document.Comments[commentId].Resolved.Should().BeFalse();
    }

    [Fact]
    public void ProtectionPolicy_BlocksCommentsOutsideCommentsOnlyMode()
    {
        var document = DocumentWith("body");
        document.Protection = new ProtectionSettings(ProtectionMode.ReadOnly);
        var session = DeterministicSession();
        session.LoadDocument(document);

        session.Review.DecisionFor(RestrictEditingOperationKind.CommentInsert).IsAllowed.Should().BeFalse();
        session.Review.TryAddComment(0, 0, int.MaxValue, "blocked", "A", "A").Should().BeNull();
        session.Commands.CanUndo.Should().BeFalse();

        document.Protection = new ProtectionSettings(ProtectionMode.CommentsOnly);
        session.Review.DecisionFor(RestrictEditingOperationKind.CommentInsert).IsAllowed.Should().BeTrue();
        session.Review.TryAddComment(0, 0, int.MaxValue, "allowed", "A", "A").Should().NotBeNull();
    }

    [Fact]
    public void CommentListAndAdjacentSelection_UseDocumentOrderAndWrap()
    {
        var document = DocumentWith("first", "second");
        var session = DeterministicSession();
        session.LoadDocument(document);
        var secondId = session.Review.TryAddComment(1, 0, int.MaxValue, "second", "B", "B")!.Value;
        var firstId = session.Review.TryAddComment(0, 0, int.MaxValue, "first", "A", "A")!.Value;

        session.Review.BuildCommentList().Select(item => item.Id).Should().Equal(firstId, secondId);
        session.Review.SelectAdjacentComment(firstId, direction: 1)!.Id.Should().Be(secondId);
        session.Review.SelectAdjacentComment(secondId, direction: 1)!.Id.Should().Be(firstId);
        session.Review.SelectAdjacentComment(firstId, direction: -1)!.Id.Should().Be(secondId);
    }

    public static IEnumerable<object[]> InspectorCategoryCombinations()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            yield return
            [
                (mask & 1) != 0,
                (mask & 2) != 0,
                (mask & 4) != 0,
                (mask & 8) != 0,
            ];
        }
    }

    [Theory]
    [MemberData(nameof(InspectorCategoryCombinations))]
    public void InspectorRemovalDecision_AppliesExactlyTheSelectedCategories(
        bool comments,
        bool revisions,
        bool properties,
        bool bookmarks)
    {
        var document = DocumentWithInspectableMetadata();
        var session = DeterministicSession();
        session.LoadDocument(document);

        var decision = session.Review.PlanInspectorRemovals(
            new InspectorRemovalChoice(comments, revisions, properties, bookmarks));
        decision.Any.Should().Be(comments || revisions || properties || bookmarks);

        decision.Apply(document);

        var result = DocumentInspector.Inspect(document);
        result.HasComments.Should().Be(!comments);
        result.HasRevisions.Should().Be(!revisions);
        result.HasProperties.Should().Be(!properties);
        result.HasBookmarks.Should().Be(!bookmarks);
    }

    [Fact]
    public void RevisionTargets_ResolveTopLevelAndTableCellOwners()
    {
        var document = new TextDocument();
        var topLevel = RevisionParagraph("top");
        var table = Table.Create(1, 1);
        var tableParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        tableParagraph.Runs.Clear();
        tableParagraph.Runs.Add(new Run("cell") { Revision = RevisionKind.Inserted });
        var afterTable = RevisionParagraph("after");
        document.Blocks.Add(topLevel);
        document.Blocks.Add(table);
        document.Blocks.Add(afterTable);
        var session = DeterministicSession();
        session.LoadDocument(document);

        var revisions = session.Review.ListRevisions();
        session.Review.ResolveRevisionTarget(revisions[0])!.TopLevelBlockIndex.Should().Be(0);
        session.Review.ResolveRevisionTarget(revisions[1])!.TopLevelBlockIndex.Should().Be(1);
        session.Review.ResolveRevisionTarget(revisions[2])!.TopLevelBlockIndex.Should().Be(2);

        var tableTarget = session.Review.ResolveRevisionTargetAtOrAfterTopLevelBlock(1);
        tableTarget.Should().NotBeNull();
        tableTarget!.RevisionIndex.Should().Be(1);
        tableTarget.TopLevelBlockIndex.Should().Be(1);

        session.Review.ResolveRevisionTargetAtOrAfterTopLevelBlock(3)!.RevisionIndex.Should().Be(0,
            "navigation wraps to the first revision after the final top-level block");
    }

    [Fact]
    public void RevisionTarget_RejectsStaleIdentityInsteadOfResolvingTheReplacementAtItsIndex()
    {
        var document = DocumentWith("first", "second");
        var first = (Paragraph)document.Blocks[0];
        first.Runs[0].Revision = RevisionKind.Inserted;
        var second = (Paragraph)document.Blocks[1];
        second.Runs[0].Revision = RevisionKind.Inserted;
        var session = DeterministicSession();
        session.LoadDocument(document);
        var target = session.Review.ResolveRevisionTarget(session.Review.ListRevisions()[0])!;

        RevisionList.Accept(document, target.Entry).Should().BeTrue();

        target.TryApply(document, RevisionResolutionAction.Accept).Should().BeFalse();
        second.Runs[0].Revision.Should().Be(RevisionKind.Inserted);
    }

    private static DocumentEditingSession DeterministicSession() =>
        new(revisionAuthor: null, revisionDateXml: () => "2026-08-05T10:20:30Z");

    private static TextDocument DocumentWith(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static Paragraph RevisionParagraph(string text)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text) { Revision = RevisionKind.Inserted });
        return paragraph;
    }

    private static TextDocument DocumentWithInspectableMetadata()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph { BookmarkName = "target" };
        paragraph.Runs.Add(new Run("commented") { CommentId = 1 });
        paragraph.Runs.Add(Run.CommentReference(1));
        paragraph.Runs.Add(new Run(" revised") { Revision = RevisionKind.Inserted });
        document.Blocks.Add(paragraph);
        document.Comments[1] = new Comment(1, "note", "Reviewer", "R");
        document.Properties.Title = "Inspectable";
        return document;
    }
}

public sealed class DocumentReviewEditingSessionSourceOwnershipTests
{
    [Fact]
    public void BothRenderersDelegateCommentDecisionsMutationsAndOrdering()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("_editingSession.Review.TryAddComment(");
            source.Should().Contain("_editingSession.Review.TryReplyToComment(");
            source.Should().Contain("_editingSession.Review.TryToggleCommentResolved(");
            source.Should().Contain("_editingSession.Review.TryDeleteComment(");
            source.Should().Contain("_editingSession.Review.SelectAdjacentComment(");
            source.Should().Contain("_editingSession.Review.ResolveTopLevelCommentId(");
            source.Should().Contain("_editingSession.Review.RestrictEditingPolicy");
            source.Should().NotContain("new AddCommentCommand(");
            source.Should().NotContain("new AddCommentReplyCommand(");
            source.Should().NotContain("new SetCommentResolvedCommand(");
            source.Should().NotContain("new DeleteCommentCommand(");
            source.Should().NotContain("DeleteCommentCommand.ResolveTopLevel(");
        }

        wpf.Should().Contain("CommentTextNormalization.Trim");
        avalonia.Should().Contain("CommentTextNormalization.Preserve");
        avalonia.Should().Contain("_editingSession.Review.TrySetCommentResolved(");
        avalonia.Should().Contain("_editingSession.Review.BuildCommentList(");
    }

    [Fact]
    public void PortableReviewCoordinatorHasNoRendererDependencies()
    {
        var source = ReadSource(
            "freew", "FreeW.App.Presentation", "Editing", "DocumentReviewEditingSession.cs");

        source.Should().NotContain("using Avalonia");
        source.Should().NotContain("using System.Windows");
        source.Should().NotContain("TextPointer");
        source.Should().NotContain("DocPosition");
        source.Should().NotContain("InvalidateVisual");
        source.Should().NotContain("Render()");
    }

    [Fact]
    public void BothRenderersDelegateInspectorDispatchAndRevisionTargetResolution()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var revisionCommands = ReadSource(
            "freew", "FreeW.App.Avalonia", "Editing", "RevisionCommands.cs");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("_editingSession.Review.PlanInspectorRemovals(choice).Apply(");
            source.Should().Contain("_editingSession.Review.ResolveRevisionTarget(entry)");
            source.Should().NotContain("DocumentInspector.RemoveComments(");
            source.Should().NotContain("DocumentInspector.RemoveRevisions(");
            source.Should().NotContain("DocumentInspector.RemoveProperties(");
            source.Should().NotContain("DocumentInspector.RemoveBookmarks(");
        }

        avalonia.Should().Contain("ResolveRevisionTargetAtOrAfterTopLevelBlock(_caret.Block)");
        revisionCommands.Should().Contain("RevisionTargetDecision target");
        revisionCommands.Should().Contain("target.TryApply(document, RevisionResolutionAction.Accept)");
        revisionCommands.Should().Contain("target.TryApply(document, RevisionResolutionAction.Reject)");
        revisionCommands.Should().NotContain("RevisionList.Enumerate(document)");

        wpf.Should().NotContain("private int TopLevelBlockIndexOf(");
        wpf.Should().NotContain("private static void MarkRevisionRange(");
        wpf.Should().NotContain("private void InsertTocAt(");
        wpf.Should().NotContain("private static double EstimateWordArtWidth(");
        wpf.Should().NotContain("private static double EstimateWordArtHeight(");
        wpf.Should().NotContain("private static BitmapSource DecodePng(");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
