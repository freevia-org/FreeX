using System.IO;
using System.Threading.Tasks;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r160, freew-comments-review F1: <see cref="DocumentView.DeleteSelection"/>'s same-block branch and
/// <see cref="DocumentView.DeleteForward"/>/Backspace's single-character body branch all rebuild a
/// paragraph's runs from <c>ParaCells</c> with zero knowledge of comment anchors, so deleting a selection
/// (or the last character) that carries a comment's whole anchored range drops the textless
/// comment-reference run outright -- but nothing ever called
/// <see cref="DocumentInspector.PruneOrphanedNoteAndCommentAnchors"/> afterward. The comment survives
/// forever in <see cref="TextDocument.Comments"/>, invisible to <see cref="CommentListPlanner"/> (which
/// only matches comments via a surviving <c>Run.CommentId</c>) yet still serialized into every future
/// save by <see cref="DocxWriter"/> (which flattens the whole dictionary unconditionally).
/// </summary>
public sealed class DocumentViewCommentPruningTests
{
    private static DocumentView BuildWithComment(out int commentId)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Hello world", RunFormatting.Default));
        document.Blocks.Add(p);

        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetSelectionRangePublic(0, 6, 0, 11); // "world"
        var id = view.AddComment("Please revise", "Ann Reviewer", "AR");
        id.Should().NotBeNull("AddComment over a real selection must anchor a comment");
        commentId = id!.Value;
        return view;
    }

    // ── Delete (DeleteSelection, same-block branch) ──────────────────────────────────────────────────

    [Fact]
    public void Deleting_a_selection_covering_a_whole_commented_range_prunes_the_orphaned_comment()
    {
        var view = BuildWithComment(out var commentId);

        // Re-select exactly the commented range and delete it, like Delete/Backspace/Cut over a
        // selection that fully contains the comment's anchor.
        view.SetSelectionRangePublic(0, 6, 0, 11);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeFalse(
            "the comment's only anchor was just deleted, so the dictionary entry must be pruned, not linger forever");
        CommentListPlanner.Build(view.Document).Should().NotContain(item => item.Id == commentId);
    }

    [Fact]
    public void Deleting_a_selection_covering_a_whole_commented_range_stops_the_writer_from_serializing_it()
    {
        var view = BuildWithComment(out var commentId);
        view.SetSelectionRangePublic(0, 6, 0, 11);
        view.TryDeleteSelection().Should().BeTrue();

        using var stream = new MemoryStream();
        DocxWriter.Write(view.Document, stream);
        stream.Position = 0;
        var roundTripped = DocxReader.Read(stream);

        roundTripped.Comments.Should().BeEmpty(
            "the writer flattens TextDocument.Comments unconditionally, so a pruned model must round-trip empty");
    }

    // ── Single-character Delete (DeleteForward) ──────────────────────────────────────────────────────

    [Fact]
    public void DeleteForward_removing_a_single_character_comment_anchor_prunes_the_orphaned_comment()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Hi X", RunFormatting.Default));
        document.Blocks.Add(p);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetSelectionRangePublic(0, 3, 0, 4); // "X"
        var id = view.AddComment("note", "Bob", "B");
        id.Should().NotBeNull();

        view.MoveCaretToBlockForTest(0, 3);
        view.DeleteForwardPublic(); // removes the single "X" character carrying the comment's last anchor

        view.Document.Comments.ContainsKey(id!.Value).Should().BeFalse(
            "the single remaining anchored character was deleted forward, so the comment must be pruned");
    }

    // ── Single-character Backspace ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Backspace_removing_a_single_character_comment_anchor_prunes_the_orphaned_comment()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Hi X", RunFormatting.Default));
        document.Blocks.Add(p);
        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetSelectionRangePublic(0, 3, 0, 4); // "X"
        var id = view.AddComment("note", "Bob", "B");
        id.Should().NotBeNull();

        view.MoveCaretToBlockForTest(0, 4);
        view.BackspacePublic(); // removes the single "X" character carrying the comment's last anchor

        view.Document.Comments.ContainsKey(id!.Value).Should().BeFalse(
            "the single remaining anchored character was deleted backward, so the comment must be pruned");
    }

    // ── Sibling / no-regression: deleting text that DOESN'T remove the whole anchor leaves it alone ──

    [Fact]
    public void Deleting_only_part_of_a_commented_range_leaves_the_comment_in_place()
    {
        var view = BuildWithComment(out var commentId);

        // Delete just "w" at the start of "world" -- the comment still anchors to "orld".
        view.SetSelectionRangePublic(0, 6, 0, 7);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Comments.ContainsKey(commentId).Should().BeTrue(
            "the comment still has a surviving anchor (\"orld\"), so it must not be pruned");
        var paragraph = view.Document.Paragraphs.Single();
        paragraph.Runs.Any(r => r.CommentId == commentId && !r.IsCommentReference).Should().BeTrue();
    }

    // ── Sibling / no-regression: deleting unrelated text in a document with no comments is unaffected ─

    [Fact]
    public void Deleting_text_in_a_document_with_no_comments_is_unaffected()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Head tail"));
        var view = new DocumentView();
        view.LoadDocument(document);

        view.SetSelectionRangePublic(0, 0, 0, 5);
        view.TryDeleteSelection().Should().BeTrue();

        view.Document.Paragraphs.Single().PlainText.Should().Be("tail");
        view.Document.Comments.Should().BeEmpty();
    }
}
