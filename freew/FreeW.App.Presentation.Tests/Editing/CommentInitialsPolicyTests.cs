using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class CommentInitialsPolicyTests
{
    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("Ada Byron Lovelace", "ABL")]
    [InlineData("Ada\tByron\nLovelace", "ABL")]
    [InlineData("ada lovelace", "AL")]
    [InlineData("Ada", "A")]
    [InlineData("", "")]
    public void Derive_UsesTheFirstLetterOfEveryNameWord(string author, string expected)
    {
        CommentInitialsPolicy.Derive(author)
            .Should().Be(expected);
    }

    [Fact]
    public void Derive_AndBadge_NeverSplitASurrogatePair()
    {
        // U+1F600 and U+1F601 sit outside the BMP, so each is two UTF-16 code units.
        CommentInitialsPolicy.Derive("\U0001F600lex \U0001F601da Kim")
            .Should().Be("\U0001F600\U0001F601K");
        CommentInitialsPolicy.ResolveBadge(null, "\U0001F600lex Kim")
            .Should().Be("\U0001F600");
        CommentInitialsPolicy.ResolveBadge("\U0001F600K", "Alex Kim")
            .Should().Be("\U0001F600");
    }

    [Fact]
    public void FreeWAndFreePDeriveInitialsThroughTheSharedOfficeRule()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var freew = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Presentation", "Editing", "CommentInitialsPolicy.cs"));
        var freep = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Presentation", "PresentationReviewWorkflowPlanner.cs"));

        freew.Should().Contain("AuthorInitials.Derive(");
        freew.Should().NotContain("[..1]");
        freep.Should().Contain("AuthorInitials.Derive(");
        freep.Should().NotContain("FirstLetterOf(");
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
