namespace FreeW.Core.Model.Tests;

/// <summary>
/// Model-level coverage for modern (threaded) comments: a top-level comment can carry an ordered list of
/// replies (each a full comment with its own globally-unique id) and a resolved/done flag, and the
/// document's id allocator accounts for reply ids too.
/// </summary>
public class ThreadedCommentsTests
{
    [Fact]
    public void AddReply_AppendsRepliesInOrder_WithGivenIdentity()
    {
        var doc = new TextDocument();
        var parent = new Comment(0, "Original note", "Alice", "A");
        doc.Comments[0] = parent;

        var first = parent.AddReply(doc.NextCommentId(), "Good point", "Bob", "B");
        var second = parent.AddReply(doc.NextCommentId(), "Agreed", "Cara", "C");

        parent.Replies.Should().HaveCount(2);
        parent.Replies.Should().ContainInOrder(first, second);
        first.Id.Should().Be(1);
        second.Id.Should().Be(2);
        first.Author.Should().Be("Bob");
        first.PlainText.Should().Be("Good point");
        // A reply is itself an ordinary comment with no nested replies and not resolved.
        first.Replies.Should().BeEmpty();
        first.Resolved.Should().BeFalse();
    }

    [Fact]
    public void NextCommentId_AccountsForReplyIds()
    {
        var doc = new TextDocument();
        var parent = new Comment(0, "Note", "Alice", "A");
        doc.Comments[0] = parent;
        parent.AddReply(doc.NextCommentId(), "Reply", "Bob", "B"); // id 1

        // The next id must clear the reply (id 1), not just the top-level comment (id 0).
        doc.NextCommentId().Should().Be(2);
    }

    [Fact]
    public void Resolved_TogglesOnTopLevelComment()
    {
        var comment = new Comment(0, "Note", "Alice", "A");
        comment.Resolved.Should().BeFalse();

        comment.Resolved = true;

        comment.Resolved.Should().BeTrue();
    }

    [Fact]
    public void ThreadInOrder_YieldsParentThenReplies()
    {
        var doc = new TextDocument();
        var parent = new Comment(0, "Note", "Alice", "A");
        doc.Comments[0] = parent;
        var r1 = parent.AddReply(doc.NextCommentId(), "R1");
        var r2 = parent.AddReply(doc.NextCommentId(), "R2");

        parent.ThreadInOrder().Should().ContainInOrder(parent, r1, r2);
        parent.ThreadInOrder().Should().HaveCount(3);
    }
}
