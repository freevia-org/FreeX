using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class ReviewAuthorIdentityPlannerTests
{
    [Theory]
    [InlineData(" Current Reviewer ", "Document Author", "OS User", "Current Reviewer")]
    [InlineData(" ", " Document Author ", "OS User", "Document Author")]
    [InlineData(null, null, " OS User ", "OS User")]
    [InlineData("", "", "", ReviewAuthorIdentityPlanner.DefaultAuthor)]
    public void ResolveAuthor_UsesOneReviewIdentityFallbackOrder(
        string? revisionAuthor,
        string? documentAuthor,
        string? operatingSystemAuthor,
        string expected)
    {
        ReviewAuthorIdentityPlanner.ResolveAuthor(
                revisionAuthor,
                documentAuthor,
                operatingSystemAuthor)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildCommentStamp_UsesResolvedAuthorAndCanonicalInitials()
    {
        ReviewAuthorIdentityPlanner.BuildCommentStamp(
                " Ada Byron Lovelace ",
                "Document Author",
                "OS User")
            .Should().Be(new CommentStampIdentity("Ada Byron Lovelace", "AL"));
    }
}
