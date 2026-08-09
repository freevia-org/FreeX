namespace FreeW.Core.Model.Tests;

public sealed class WordHyperlinkFieldParserTests
{
    [Fact]
    public void ExternalAddressAndScreenTipAreProjected()
    {
        var field = new ComplexField(" HYPERLINK \"https://example.com/docs\" \\o \"Open documentation\" \\n ");

        WordHyperlinkFieldParser.TryParse(field, out var target).Should().BeTrue();
        target.Should().Be(new HyperlinkFieldTarget(
            "https://example.com/docs",
            Anchor: null,
            "Open documentation"));
    }

    [Fact]
    public void LocationOnlyBecomesAnInternalBookmarkLink()
    {
        var field = new ComplexField(" HYPERLINK \\l \"Section_2\" \\o \"Jump to section\" ");

        WordHyperlinkFieldParser.TryParse(field, out var target).Should().BeTrue();
        target.Should().Be(new HyperlinkFieldTarget(
            Url: null,
            "Section_2",
            "Jump to section"));
    }

    [Fact]
    public void FileLocationIsCombinedWithoutChangingTheFieldInstruction()
    {
        const string instruction = " HYPERLINK \"https://example.com/manual\" \\l \"Install\" \\t \"_blank\" ";
        var field = new ComplexField(instruction);

        WordHyperlinkFieldParser.TryParse(field, out var target).Should().BeTrue();
        target.Url.Should().Be("https://example.com/manual#Install");
        target.Anchor.Should().BeNull();
        field.Instruction.Should().Be(instruction);
    }

    [Theory]
    [InlineData(" DATE ")]
    [InlineData(" HYPERLINK \\n ")]
    public void NonNavigableFieldIsNotProjected(string instruction)
    {
        WordHyperlinkFieldParser.TryParse(new ComplexField(instruction), out _).Should().BeFalse();
    }
}
