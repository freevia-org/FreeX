namespace FreeW.Core.Model.Tests;

public class OutlineToolsTests
{
    // --- Promote: raises a heading one rank toward the top (Title) ---

    [Theory]
    [InlineData("Heading6", "Heading5")]
    [InlineData("Heading3", "Heading2")]
    [InlineData("Heading2", "Heading1")]
    [InlineData("Heading1", "Title")] // Heading1 promotes to the title
    [InlineData("Title", "Title")]    // Title is already the top — stays put
    public void Promote_RaisesHeadingOneRank(string styleId, string expected)
    {
        OutlineTools.Promote(styleId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Normal")]
    [InlineData("Subtitle")] // a built-in style, but not part of the heading outline
    [InlineData("Quote")]
    public void Promote_NonHeading_ReturnsStyleUnchanged(string? styleId)
    {
        OutlineTools.Promote(styleId).Should().Be(styleId);
    }

    // --- Demote: lowers a heading one rank toward the bottom, capped at Heading6 ---

    [Theory]
    [InlineData("Title", "Heading1")] // Title demotes to Heading1
    [InlineData("Heading1", "Heading2")]
    [InlineData("Heading2", "Heading3")]
    [InlineData("Heading5", "Heading6")]
    [InlineData("Heading6", "Heading6")] // capped at the deepest level
    [InlineData("Heading10", "Heading6")] // already past the cap — clamps down to the cap
    public void Demote_LowersHeadingOneRankCapped(string styleId, string expected)
    {
        OutlineTools.Demote(styleId).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Normal")]
    [InlineData("Subtitle")]
    public void Demote_NonHeading_BecomesHeading1(string? styleId)
    {
        OutlineTools.Demote(styleId).Should().Be("Heading1");
    }

    [Fact]
    public void MaxHeadingLevel_IsHeading6()
    {
        OutlineTools.MaxHeadingLevel.Should().Be(6);
    }

    // --- Round-trip / inverse-ish behaviour across the registered heading range ---

    [Theory]
    [InlineData("Heading1")]
    [InlineData("Heading2")]
    [InlineData("Heading3")]
    public void PromoteThenDemote_RoundTripsInteriorHeadings(string styleId)
    {
        // Promote then demote returns to the same level for headings that are not at an outline edge.
        OutlineTools.Demote(OutlineTools.Promote(styleId)).Should().Be(styleId);
    }

    [Fact]
    public void Demote_OnDocumentParagraph_SetsHeadingStyle()
    {
        // Demonstrates the helper used against a real model paragraph's StyleId.
        var doc = new TextDocument();
        var paragraph = new Paragraph("A plain body line");
        doc.Blocks.Add(paragraph);

        paragraph.StyleId = OutlineTools.Demote(paragraph.StyleId);

        paragraph.StyleId.Should().Be("Heading1");
        DocumentOutline.TryGetLevel(paragraph.StyleId, out var level).Should().BeTrue();
        level.Should().Be(1);
    }

    [Fact]
    public void Promote_OnHeadingParagraph_RaisesStyle()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("Section") { StyleId = "Heading2" };
        doc.Blocks.Add(paragraph);

        paragraph.StyleId = OutlineTools.Promote(paragraph.StyleId);

        paragraph.StyleId.Should().Be("Heading1");
    }
}
