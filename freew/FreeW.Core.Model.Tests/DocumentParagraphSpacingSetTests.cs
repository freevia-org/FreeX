namespace FreeW.Core.Model.Tests;

public class DocumentParagraphSpacingSetTests
{
    [Fact]
    public void Catalog_ContainsWordStyleParagraphSpacingPresets_InOrder()
    {
        DocumentParagraphSpacingSet.Catalog.Select(s => s.Name)
            .Should().Equal("No Paragraph Space", "Compact", "Tight", "Open", "Relaxed", "Double");
        DocumentParagraphSpacingSet.Default.Name.Should().Be("Tight");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        DocumentParagraphSpacingSet.FindByName("relaxed").Should().BeSameAs(DocumentParagraphSpacingSet.Catalog[4]);
        DocumentParagraphSpacingSet.FindByName("Nope").Should().BeNull();
    }

    [Fact]
    public void Apply_RewritesDefaultAndBuiltInStyleSpacing()
    {
        var doc = TextDocument.CreateEmpty();
        var relaxed = DocumentParagraphSpacingSet.FindByName("Relaxed")!;

        DocumentParagraphSpacingSet.Apply(doc, relaxed);

        AssertSpacing(doc.DefaultParagraph, before: 0, after: 6, line: 1.5);
        AssertSpacing(doc.Styles["Normal"].Paragraph, before: 0, after: 6, line: 1.5);
        AssertSpacing(doc.Styles["Heading1"].Paragraph, before: 0, after: 6, line: 1.5);
        AssertSpacing(doc.Styles["Quote"].Paragraph, before: 0, after: 6, line: 1.5);
    }

    [Fact]
    public void Apply_PreservesFontsColorsIndentsAndCustomStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var headingRun = doc.Styles["Heading1"].Run;
        var quoteIndent = doc.Styles["Quote"].Paragraph.IndentLeftPt;
        var custom = StyleManager.CreateStyle(
            doc,
            "Callout",
            "Normal",
            new RunFormatting { FontFamily = "Georgia", ColorHex = "#FF0000" },
            ParagraphFormatting.Default with { SpaceAfterPt = 18, SpaceAfterIsSet = true });

        DocumentParagraphSpacingSet.Apply(doc, DocumentParagraphSpacingSet.FindByName("Double")!);

        doc.Styles["Heading1"].Run.Should().Be(headingRun);
        doc.Styles["Quote"].Paragraph.IndentLeftPt.Should().Be(quoteIndent);
        doc.Styles["Callout"].Should().BeSameAs(custom);
        doc.Styles["Callout"].Paragraph.SpaceAfterPt.Should().Be(18);
        doc.Styles["Callout"].Run.FontFamily.Should().Be("Georgia");
    }

    private static void AssertSpacing(ParagraphFormatting paragraph, double before, double after, double line)
    {
        paragraph.SpaceBeforePt.Should().Be(before);
        paragraph.SpaceAfterPt.Should().Be(after);
        paragraph.LineSpacing.Should().Be(line);
        paragraph.LineRule.Should().Be(LineSpacingRule.Multiple);
        paragraph.LineHeightPt.Should().Be(0);
        paragraph.SpaceBeforeIsSet.Should().BeTrue();
        paragraph.SpaceAfterIsSet.Should().BeTrue();
        paragraph.LineSpacingIsSet.Should().BeTrue();
    }
}
