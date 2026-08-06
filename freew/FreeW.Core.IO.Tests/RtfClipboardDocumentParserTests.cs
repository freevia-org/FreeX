namespace FreeW.Core.IO.Tests;

public sealed class RtfClipboardDocumentParserTests
{
    [Fact]
    public void TryParse_PreservesRunsAndParagraphs()
    {
        const string rtf = @"{\rtf1\ansi\b Bold\b0  plain\par\i Second\i0}";

        var parsed = RtfClipboardDocumentParser.TryParse(rtf, out var document);

        parsed.Should().BeTrue();
        document.Should().NotBeNull();
        var paragraphs = document!.Blocks.OfType<Paragraph>().ToArray();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].Runs.Should().Contain(run => run.Text == "Bold" && run.Formatting.Bold);
        paragraphs[1].Runs.Should().Contain(run => run.Text == "Second" && run.Formatting.Italic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"{\rtf1\ansi\fs999999999999999999 text}")]
    public void TryParse_MissingOrMalformedPayloadReturnsFalse(string? rtf)
    {
        RtfClipboardDocumentParser.TryParse(rtf, out var document).Should().BeFalse();
        document.Should().BeNull();
    }
}
