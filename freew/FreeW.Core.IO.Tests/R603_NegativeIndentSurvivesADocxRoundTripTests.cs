using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r603: DocxWriter gated w:ind on <c>IndentLeftPt &gt; 0 || IndentRightPt &gt; 0</c>, so a NEGATIVE
/// left or right indent produced no w:ind element at all and the outdent was lost on save. Word
/// writes negative values there routinely -- w:left and w:right are ST_SignedTwipsMeasure, and an
/// outdent into the margin is ordinary authoring -- so the accept form had to be <c>!= 0</c>, which
/// is what the sibling FirstLineIndentPt term already used.
///
/// <para>Found while probing the reader for culture sensitivity: the round trip failed under EVERY
/// culture including the invariant one, which is what distinguished a write-side gate from the
/// locale hypothesis being tested. Both writer sites carried the gate.</para>
/// </summary>
public sealed class R603_NegativeIndentSurvivesADocxRoundTripTests
{
    private static TextDocument Outdented(double leftPt, double rightPt)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph
        {
            Formatting = new ParagraphFormatting { IndentLeftPt = leftPt, IndentRightPt = rightPt },
        };
        paragraph.Runs.Add(new Run("outdented", new RunFormatting { FontSizePt = 10.5 }));
        document.Blocks.Add(paragraph);
        return document;
    }

    [Theory]
    [InlineData(-18, 0)]
    [InlineData(0, -12)]
    [InlineData(-18, -12)]
    // Positive is the case that already worked; it is here so a fix that inverts the gate is caught
    // rather than trading one silent loss for another.
    [InlineData(24, 36)]
    public void ASignedIndentSurvivesADocxRoundTrip(double leftPt, double rightPt)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(Outdented(leftPt, rightPt), stream);
        stream.Position = 0;

        var formatting = DocxReader.Read(stream).Paragraphs.First().Formatting!;
        formatting.IndentLeftPt.Should().Be(leftPt);
        formatting.IndentRightPt.Should().Be(rightPt);
    }
}
