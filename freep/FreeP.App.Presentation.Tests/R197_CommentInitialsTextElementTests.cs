using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r197: <c>NormalizeInitials</c> derived a comment's initials with <c>part[0]</c> -- one UTF-16
/// char. For an author whose name starts outside the BMP that is a lone high surrogate, and the
/// result is STORED as <see cref="SlideComment.Initials"/> and written straight into the OOXML
/// author element's <c>initials</c> attribute. Constructing that XElement then throws and aborts the
/// WHOLE .pptx save, permanently -- the same class as the r193 Drop Cap and r194 sheet-name fixes,
/// found by re-asking that question on ground it had already swept.
///
/// The author name is read verbatim from <c>dc:creator</c>, which any producer may set to anything.
/// </summary>
public sealed class R197_CommentInitialsTextElementTests
{
    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }

    private static string Initials(string? initials, string? author) =>
        PresentationReviewWorkflowPlanner.NormalizeInitialsForTest(initials, author);

    [Theory]
    [InlineData("\U0001F600 Alex Kim")]
    [InlineData("\U0001F600lex Kim")]
    [InlineData("\U00010400ada Lovelace")]
    public void DerivedInitials_FromAnAstralAuthorName_CarryNoLoneSurrogate(string author)
    {
        var result = Initials(null, author);

        HasLoneSurrogate(result).Should().BeFalse(
            "a lone surrogate here aborts every later .pptx save; got '{0}'",
            result);
    }

    [Fact]
    public void ExplicitInitials_AreTruncatedOnATextElementBoundary()
    {
        // The explicit branch truncated with [..3], which splits a pair just as readily.
        var result = Initials("AB\U0001F600C", author: null);

        HasLoneSurrogate(result).Should().BeFalse();
        result.Length.Should().BeLessThanOrEqualTo(4, "at most three text elements");
    }

    [Theory]
    [InlineData("Alex Kim", "AK")]
    [InlineData("Ada Lovelace Byron", "ALB")]
    [InlineData("Cher", "C")]
    public void DerivedInitials_ForOrdinaryNames_AreUnchanged(string author, string expected)
    {
        Initials(null, author).Should().Be(expected);
    }

    [Fact]
    public void DerivedInitials_ForAnAstralFirstLetter_KeepTheWholeCharacter()
    {
        Initials(null, "\U0001F600lex Kim").Should().Be("\U0001F600K");
    }

    [Fact]
    public void DerivedInitials_FallBackWhenNothingUsableRemains()
    {
        Initials(null, null).Should().Be("FU");
    }
}
