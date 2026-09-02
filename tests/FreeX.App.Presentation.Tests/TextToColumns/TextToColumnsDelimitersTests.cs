using FluentAssertions;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Presentation.Tests.TextToColumns;

public sealed class TextToColumnsDelimitersTests
{
    [Theory]
    [InlineData(TextToColumnsDelimiterKind.Comma, ",")]
    [InlineData(TextToColumnsDelimiterKind.Semicolon, ";")]
    [InlineData(TextToColumnsDelimiterKind.Tab, "\t")]
    [InlineData(TextToColumnsDelimiterKind.Space, " ")]
    public void CharacterFor_WellKnownKinds_MapToCharacters(TextToColumnsDelimiterKind kind, string expected)
    {
        TextToColumnsDelimiters.CharacterFor(kind).Should().Be(expected);
    }

    [Fact]
    public void CharacterFor_Custom_UsesProvidedChar()
    {
        TextToColumnsDelimiters.CharacterFor(TextToColumnsDelimiterKind.Custom, "#").Should().Be("#");
    }

    [Fact]
    public void CharacterFor_Custom_WithoutChar_Throws()
    {
        var act = () => TextToColumnsDelimiters.CharacterFor(TextToColumnsDelimiterKind.Custom);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_MultipleKinds_ConcatenatesCharacters()
    {
        var resolved = TextToColumnsDelimiters.Resolve(
            [TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Tab]);

        resolved.Should().Be(",\t");
    }

    [Fact]
    public void Resolve_DuplicateKinds_AreDeduplicated()
    {
        var resolved = TextToColumnsDelimiters.Resolve(
            [TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Comma]);

        resolved.Should().Be(",");
    }

    [Fact]
    public void Resolve_EmptySet_Throws()
    {
        var act = () => TextToColumnsDelimiters.Resolve([]);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(TextToColumnsTextQualifier.DoubleQuote, '"')]
    [InlineData(TextToColumnsTextQualifier.SingleQuote, '\'')]
    public void QualifierChar_MapsToCharacter(TextToColumnsTextQualifier qualifier, char expected)
    {
        TextToColumnsOptions.QualifierChar(qualifier).Should().Be(expected);
    }

    [Fact]
    public void QualifierChar_None_IsNull()
    {
        TextToColumnsOptions.QualifierChar(TextToColumnsTextQualifier.None).Should().BeNull();
    }
}
