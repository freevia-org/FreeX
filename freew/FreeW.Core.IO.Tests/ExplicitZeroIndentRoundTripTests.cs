using System.IO;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A direct <c>w:ind w:left="0"</c> must cancel an indented paragraph style (Word's "List Paragraph" 36pt left
/// indent) instead of being mistaken for an absent attribute; the stress015 table cells depend on it.
/// </summary>
public sealed class ExplicitZeroIndentRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void ExplicitZeroLeftIndent_SurvivesRoundTripAndIsMarkedExplicit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["ListPara"] = new DocumentStyle
        {
            Id = "ListPara",
            Name = "List Paragraph",
            Paragraph = new ParagraphFormatting { IndentLeftPt = 36, IndentLeftIsSet = true },
        };
        var paragraph = new Paragraph("cell")
        {
            StyleId = "ListPara",
            Formatting = new ParagraphFormatting { IndentLeftIsSet = true },
        };
        doc.Blocks.Add(paragraph);

        var reloaded = RoundTrip(doc);

        var read = reloaded.Blocks.OfType<Paragraph>().First(p => p.Runs.Any(r => r.Text == "cell"));
        read.Formatting.IndentLeftPt.Should().Be(0);
        read.Formatting.IndentLeftIsSet.Should().BeTrue();
        read.Formatting.IndentRightIsSet.Should().BeFalse();
        reloaded.Styles[read.StyleId!].Paragraph.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void AbsentIndent_IsNotMarkedExplicit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph("plain"));

        var read = RoundTrip(doc).Blocks.OfType<Paragraph>().First(p => p.Runs.Any(r => r.Text == "plain"));

        read.Formatting.IndentLeftIsSet.Should().BeFalse();
    }
}
