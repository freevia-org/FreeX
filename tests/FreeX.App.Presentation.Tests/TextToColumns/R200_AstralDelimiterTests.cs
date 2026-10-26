using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

/// <summary>
/// r200: the Text to Columns "Other" box takes one character, and the code took one UTF-16 code
/// UNIT. An astral character typed there became a lone high surrogate, and because
/// <see cref="TextToColumnsSplitter"/> scans the cell text one code unit at a time, it then split
/// inside every unrelated astral character sharing that high surrogate -- roughly 1024 codepoints
/// per surrogate -- writing the halves into new cells. The corruption lands in text the user never
/// meant to touch, from a character they typed into a delimiter box.
/// </summary>
public sealed class R200_AstralDelimiterTests
{
    private const string Astral = "\U0001F600";

    [Fact]
    public void ACustomDelimiter_KeepsAnAstralCharacterWhole()
    {
        TextToColumnsDelimiters.DelimiterFor(TextToColumnsDelimiterKind.Custom, Astral)
            .Should().Be(Astral);
        TextToColumnsDelimiters.CharacterFor(TextToColumnsDelimiterKind.Custom, Astral)
            .Should().Be(Astral);
    }

    [Fact]
    public void ACustomDelimiter_StillTakesOnlyTheLeadingCharacter()
    {
        // The control: the box is one character, and extra typing is still trimmed as before.
        TextToColumnsDelimiters.DelimiterFor(TextToColumnsDelimiterKind.Custom, "|xyz")
            .Should().Be("|");
        TextToColumnsDelimiters.CharacterFor(TextToColumnsDelimiterKind.Custom, "|xyz")
            .Should().Be("|");
    }

    [Fact]
    public void SplittingOnAnAstralDelimiter_DoesNotCutUnrelatedAstralCharacters()
    {
        // U+1F600 and U+1F601 share the high surrogate U+D83D. Splitting on the first must leave
        // the second intact rather than break it into two orphaned halves.
        var delimiters = TextToColumnsDelimiters.Resolve([TextToColumnsDelimiterKind.Custom], Astral);

        var parts = TextToColumnsSplitter.SplitDelimited("a\U0001F601b" + Astral + "c", delimiters);

        parts.Should().HaveCount(2);
        parts[0].Should().Be("a\U0001F601b", "the unrelated emoji must survive whole");
        parts[1].Should().Be("c");
    }

    [Fact]
    public void SplittingOnAnOrdinaryDelimiter_IsUnchanged()
    {
        var delimiters = TextToColumnsDelimiters.Resolve([TextToColumnsDelimiterKind.Custom], "|");

        TextToColumnsSplitter.SplitDelimited("a|b|c", delimiters).Should().Equal("a", "b", "c");
    }
}
