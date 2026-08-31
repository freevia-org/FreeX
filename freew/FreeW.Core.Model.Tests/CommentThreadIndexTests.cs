namespace FreeW.Core.Model.Tests;

public sealed class CommentThreadIndexTests
{
    [Fact]
    public void BuildTopLevelByCommentId_PrefersDirectKeysAndFirstThreadForMalformedDuplicates()
    {
        var document = new TextDocument();
        var keyedRoot = new Comment(1, "Keyed root");
        document.Comments[20] = keyedRoot;

        var duplicateReplyRoot = new Comment(30, "Duplicate reply root");
        duplicateReplyRoot.AddReply(20, "Duplicate reply");
        document.Comments[30] = duplicateReplyRoot;

        var firstReplyRoot = new Comment(40, "First reply root");
        firstReplyRoot.AddReply(50, "First duplicate reply");
        document.Comments[40] = firstReplyRoot;

        var secondReplyRoot = new Comment(60, "Second reply root");
        secondReplyRoot.AddReply(50, "Second duplicate reply");
        document.Comments[60] = secondReplyRoot;

        var index = CommentThreadIndex.BuildTopLevelByCommentId(document);

        index[20].Should().BeSameAs(keyedRoot, "direct dictionary keys take precedence over reply ids");
        index[50].Should().BeSameAs(firstReplyRoot, "the first thread wins a malformed duplicate reply id");
    }
}
