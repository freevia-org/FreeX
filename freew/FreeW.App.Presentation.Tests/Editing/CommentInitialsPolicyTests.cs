using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class CommentInitialsPolicyTests
{
    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Ada Byron Lovelace", "ABL")]
    [InlineData("Ada\tByron\nLovelace", "ABL")]
    [InlineData("", "?")]
    public void First_three_words_preserves_wpf_comment_stamp_semantics(string author, string expected)
    {
        CommentInitialsPolicy.Derive(author, CommentInitialsPolicy.FirstThreeWords)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Ada Byron Lovelace", "AL")]
    [InlineData("Ada", "A")]
    [InlineData("", "")]
    public void First_and_last_words_preserves_avalonia_comment_stamp_semantics(string author, string expected)
    {
        CommentInitialsPolicy.Derive(author, CommentInitialsPolicy.FirstAndLastWords)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(" al ", "Ada", "A")]
    [InlineData("", " Ada ", "A")]
    [InlineData("", "", "C")]
    public void Badge_prefers_stored_initials_then_author_then_fallback(
        string initials,
        string author,
        string expected)
    {
        CommentInitialsPolicy.ResolveBadge(initials, author).Should().Be(expected);
    }
}
