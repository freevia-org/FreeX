using System.Xml.Linq;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class PreservedNumberingMarkerPlannerTests
{
    [Fact]
    public void Build_ResolvesStyleLevelRomanMarkersAcrossConsecutiveParagraphs()
    {
        var document = CreateDocument();
        document.Styles["Legal"] = new DocumentStyle
        {
            Id = "Legal",
            Name = "Legal",
            PreservedNumbering = new PreservedNumbering(2, 0)
        };
        document.Blocks.Add(new Paragraph("First") { StyleId = "Legal" });
        document.Blocks.Add(new Paragraph("Second") { StyleId = "Legal" });
        document.Blocks.Add(new Paragraph("Third") { StyleId = "Legal" });

        var plan = PreservedNumberingMarkerPlanner.Build(document);

        plan.Select(pair => pair.Value.Text).Should().Equal("Section I.", "Section II.", "Section III.");
        plan.Values.Should().OnlyContain(marker => marker.Level == 0);
    }

    [Fact]
    public void Build_UsesDirectNumberingBeforeStyleAndAppliesLevelTextAndStartOverride()
    {
        var document = CreateDocument();
        document.Styles["Legal"] = new DocumentStyle
        {
            Id = "Legal",
            Name = "Legal",
            PreservedNumbering = new PreservedNumbering(2, 0)
        };
        document.Blocks.Add(new Paragraph("Styled") { StyleId = "Legal" });
        document.Blocks.Add(new Paragraph("Direct parent")
        {
            StyleId = "Legal",
            PreservedNumbering = new PreservedNumbering(3, 0)
        });
        document.Blocks.Add(new Paragraph("Direct child")
        {
            PreservedNumbering = new PreservedNumbering(3, 1)
        });
        document.Blocks.Add(new Paragraph("Next direct parent")
        {
            PreservedNumbering = new PreservedNumbering(3, 0)
        });

        var plan = PreservedNumberingMarkerPlanner.Build(document);

        plan[0].Text.Should().Be("Section I.");
        plan[1].Text.Should().Be("(III)");
        plan[2].Text.Should().Be("III-a");
        plan[2].Level.Should().Be(1);
        plan[3].Text.Should().Be("(IV)");
    }

    [Fact]
    public void Build_SkipsMissingDefinitionAndNativeLists()
    {
        var document = CreateDocument();
        document.Blocks.Add(new Paragraph("Missing")
        {
            PreservedNumbering = new PreservedNumbering(99, 0)
        });
        document.Blocks.Add(new Paragraph("Native")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number },
            PreservedNumbering = new PreservedNumbering(2, 0)
        });

        PreservedNumberingMarkerPlanner.Build(document).Should().BeEmpty();
    }

    private static TextDocument CreateDocument()
    {
        var document = new TextDocument();
        document.Preserved.OriginalNumbering = XElement.Parse(
            """
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="10">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="Section %1."/></w:lvl>
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="11">
                <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="(%1)"/></w:lvl>
                <w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%1-%2"/></w:lvl>
              </w:abstractNum>
              <w:num w:numId="2"><w:abstractNumId w:val="10"/></w:num>
              <w:num w:numId="3"><w:abstractNumId w:val="11"/><w:lvlOverride w:ilvl="0"><w:startOverride w:val="3"/></w:lvlOverride></w:num>
            </w:numbering>
            """);
        return document;
    }
}
