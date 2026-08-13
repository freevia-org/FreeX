using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class CommentInitialsPolicyTests
{
    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Ada Byron Lovelace", "AL")]
    [InlineData("Ada\tByron\nLovelace", "AL")]
    [InlineData("Ada", "A")]
    [InlineData("", "")]
    public void Derive_UsesCanonicalFirstAndLastWordInitials(string author, string expected)
    {
        CommentInitialsPolicy.Derive(author)
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
