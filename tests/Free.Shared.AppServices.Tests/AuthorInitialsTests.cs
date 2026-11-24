namespace Free.Shared.AppServices.Tests;

public sealed class AuthorInitialsTests
{
    [Theory]
    [InlineData("John Smith", "JS")]
    [InlineData("John Quincy Adams", "JQA")]
    [InlineData("  john   smith ", "JS")]
    [InlineData("Ada\tByron\nLovelace", "ABL")]
    [InlineData("Cher", "C")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Derive_UsesTheFirstLetterOfEachNameWord(string? name, string expected)
    {
        AuthorInitials.Derive(name).Should().Be(expected);
    }

    [Fact]
    public void Derive_NeverSplitsASurrogatePair()
    {
        // U+1F600 and U+1F601 are outside the BMP: two UTF-16 code units each.
        var initials = AuthorInitials.Derive("\U0001F600lex \U0001F601da Kim");

        initials.Should().Be("\U0001F600\U0001F601K");
        initials.Any(char.IsSurrogate).Should().BeTrue();
        for (var i = 0; i < initials.Length; i++)
        {
            if (char.IsHighSurrogate(initials[i]))
            {
                char.IsLowSurrogate(initials[i + 1]).Should().BeTrue();
                i++;
            }
            else
            {
                char.IsLowSurrogate(initials[i]).Should().BeFalse();
            }
        }
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abc", "a")]
    [InlineData("\U0001F600bc", "\U0001F600")]
    public void FirstTextElement_ReturnsTheWholeLeadingCharacter(string? text, string expected)
    {
        AuthorInitials.FirstTextElement(text).Should().Be(expected);
    }
}
